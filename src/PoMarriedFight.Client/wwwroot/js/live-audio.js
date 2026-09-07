// PoLive: the audio of a live fight. Microphone frames go up to .NET at 16 kHz; the host's voice comes back as
// 24 kHz PCM and is scheduled gaplessly. Kept apart from PoAudio, which plays whole synthesized lines for a watch:
// the two have different contracts and only one of them is ever on screen at a time.
//
// Levels are published as CSS custom properties (--po-mic-level, --po-host-level) so meters animate in the browser
// rather than crossing into .NET sixty times a second.
window.PoLive = (function () {
  "use strict";

  const OUTPUT_RATE = 24000;

  let ctx = null;
  let hostIn = null;
  let analyser = null;
  let levelData = null;
  let stream = null;
  let worklet = null;
  let source = null;
  let dotnet = null;
  let scheduled = [];
  let nextStart = 0;
  let micLevel = 0;
  let hostLevel = 0;
  let meterTimer = 0;

  function setVar(name, value) {
    document.documentElement.style.setProperty(name, value.toFixed(3));
  }

  function pumpLevels() {
    if (!ctx || !analyser) {
      return;
    }

    analyser.getByteTimeDomainData(levelData);
    let sum = 0;
    for (let i = 0; i < levelData.length; i++) {
      const v = (levelData[i] - 128) / 128;
      sum += v * v;
    }

    // Curved, because a linear RMS meter sits near zero for ordinary speech and tells the room nothing.
    hostLevel = Math.min(1, Math.pow(Math.sqrt(sum / levelData.length) * 3.2, 0.7));
    setVar("--po-host-level", hostLevel);
    setVar("--po-mic-level", Math.min(1, Math.pow(micLevel * 3.2, 0.7)));
  }

  async function ensureContext() {
    if (!ctx) {
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      hostIn = ctx.createGain();
      analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      analyser.smoothingTimeConstant = 0.35;
      levelData = new Uint8Array(analyser.fftSize);
      hostIn.connect(analyser);
      analyser.connect(ctx.destination);
      meterTimer = window.setInterval(pumpLevels, 100);
    }

    if (ctx.state === "suspended") {
      try {
        await ctx.resume();
      } catch {
        // Needs a user gesture; the click that starts the fight will do it.
      }
    }

    return ctx;
  }

  // Starts capture. `ref` is a DotNetObjectReference exposing [JSInvokable] OnAudioFrame(byte[]).
  // Returns the device sample rate, or an error name when the microphone was refused.
  async function startCapture(ref) {
    try {
      dotnet = ref;
      await ensureContext();

      stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });

      await ctx.audioWorklet.addModule("js/pcm-worklet.js");
      source = ctx.createMediaStreamSource(stream);
      worklet = new AudioWorkletNode(ctx, "pcm-capture", { numberOfInputs: 1, numberOfOutputs: 0 });
      worklet.port.onmessage = function (e) {
        micLevel = e.data.rms;
        if (dotnet) {
          // A dropped frame is better than a stalled microphone: the next one is 100 ms away.
          dotnet.invokeMethodAsync("OnAudioFrame", e.data.pcm).catch(function () {});
        }
      };

      source.connect(worklet);
      return { sampleRate: ctx.sampleRate, error: "" };
    } catch (err) {
      await stop();
      return { sampleRate: 0, error: (err && err.name) || "NotAllowedError" };
    }
  }

  // Queues 16-bit little-endian 24 kHz mono PCM so consecutive chunks play without a seam.
  function play(bytes) {
    if (!ctx || !bytes || bytes.byteLength < 2) {
      return;
    }

    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const samples = bytes.byteLength >> 1;
    const buffer = ctx.createBuffer(1, samples, OUTPUT_RATE);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < samples; i++) {
      data[i] = view.getInt16(i * 2, true) / 32768;
    }

    const src = ctx.createBufferSource();
    src.buffer = buffer;
    src.connect(hostIn);

    // A little ahead of now, so a late chunk never tries to start in the past and click.
    const startAt = Math.max(ctx.currentTime + 0.05, nextStart);
    src.start(startAt);
    nextStart = startAt + buffer.duration;
    scheduled.push(src);
    src.onended = function () {
      scheduled = scheduled.filter(function (s) { return s !== src; });
    };
  }

  // The host was interrupted: drop what is queued rather than talking over whoever cut in.
  function clear() {
    for (const src of scheduled) {
      try {
        src.stop();
      } catch {
        // Already finished.
      }
    }

    scheduled = [];
    nextStart = 0;
  }

  function isPlaying() {
    return !!ctx && nextStart > ctx.currentTime;
  }

  async function stop() {
    clear();
    dotnet = null;

    if (worklet) {
      worklet.port.onmessage = null;
      worklet.disconnect();
      worklet = null;
    }

    if (source) {
      source.disconnect();
      source = null;
    }

    if (stream) {
      stream.getTracks().forEach(function (t) { t.stop(); });
      stream = null;
    }

    micLevel = 0;
    setVar("--po-mic-level", 0);
    setVar("--po-host-level", 0);

    if (meterTimer) {
      window.clearInterval(meterTimer);
      meterTimer = 0;
    }
  }

  return {
    startCapture: startCapture,
    play: play,
    clear: clear,
    isPlaying: isPlaying,
    stop: stop,
    micLevel: function () { return Math.min(1, Math.pow(micLevel * 3.2, 0.7)); },
    hostLevel: function () { return hostLevel; },
  };
})();
