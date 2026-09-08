// Records one spoken turn and hands it back as a 16 kHz mono WAV. The server's transcribers take WAV and nothing
// else, so the conversion happens here rather than shipping a container the API would have to unpack.
window.PoMic = (function () {
  "use strict";

  const RATE = 16000;

  let stream = null;
  let recorder = null;
  let chunks = [];

  // The meter runs off the live stream rather than the recording, so the page can show that the microphone is
  // working while somebody is still deciding what to say.
  let meterContext = null;
  let analyser = null;
  let samples = null;

  let meterFrame = 0;

  function publish(value) {
    try {
      document.documentElement.style.setProperty("--po-mic-level", String(value));
    } catch {
      // No document (a worker, a test host): the meter simply does not move.
    }
  }

  // The same custom property the live fight publishes, so one meter reads either. Painted by the browser rather
  // than pushed from .NET: a level is sixty small changes a second and none of them is worth a round trip.
  function pump() {
    if (!analyser) {
      return;
    }

    publish(level());
    meterFrame = requestAnimationFrame(pump);
  }

  function listen() {
    try {
      meterContext = new (window.AudioContext || window.webkitAudioContext)();
      analyser = meterContext.createAnalyser();
      analyser.fftSize = 1024;
      samples = new Float32Array(analyser.fftSize);
      meterContext.createMediaStreamSource(stream).connect(analyser);
      meterFrame = requestAnimationFrame(pump);
    } catch {
      // No meter, but the recording itself is unaffected: level() answers 0 and the page simply shows no movement.
      analyser = null;
    }
  }

  function release() {
    if (meterFrame) {
      cancelAnimationFrame(meterFrame);
      meterFrame = 0;
    }

    publish(0);

    if (stream) {
      stream.getTracks().forEach(function (t) { t.stop(); });
      stream = null;
    }

    if (meterContext) {
      meterContext.close().catch(function () { /* already closing */ });
      meterContext = null;
    }

    analyser = null;
    samples = null;
    recorder = null;
  }

  /** How loud the microphone is right now, 0 to 1. Root-mean-square, so it tracks speech rather than clicks. */
  function level() {
    if (!analyser) {
      return 0;
    }

    analyser.getFloatTimeDomainData(samples);
    let sum = 0;
    for (let i = 0; i < samples.length; i++) {
      sum += samples[i] * samples[i];
    }

    // Speech sits well below full scale, so the reading is lifted to make an ordinary voice fill the meter.
    return Math.min(1, Math.sqrt(sum / samples.length) * 8);
  }

  // Returns "" when recording started, or the DOMException name when it did not. The caller turns that name into a
  // sentence: a refused permission and a missing device need different advice.
  // The chosen microphone, when there is one. `exact` on purpose: silently falling back to the default is how
  // somebody ends up recording on the wrong device after deliberately picking one.
  function constrain(deviceId) {
    const audio = { channelCount: 1, echoCancellation: true, noiseSuppression: true };
    if (deviceId) {
      audio.deviceId = { exact: deviceId };
    }

    return audio;
  }

  // The microphones this browser will admit to. Labels are blank until permission has been given once, which is why
  // the setup screen asks for the microphone before it offers the list.
  async function devices() {
    if (!navigator.mediaDevices || typeof navigator.mediaDevices.enumerateDevices !== "function") {
      return [];
    }

    try {
      const all = await navigator.mediaDevices.enumerateDevices();
      return all
        .filter(function (d) { return d.kind === "audioinput"; })
        .map(function (d, i) {
          return { id: d.deviceId, label: d.label || "Microphone " + (i + 1) };
        });
    } catch (err) {
      return [];
    }
  }

  async function start(deviceId) {
    try {
      if (recorder) {
        return "";
      }

      if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
        return "NotSupportedError";
      }

      stream = await navigator.mediaDevices.getUserMedia({
        audio: constrain(deviceId),
      });

      chunks = [];
      recorder = new MediaRecorder(stream);
      recorder.ondataavailable = function (e) {
        if (e.data && e.data.size > 0) {
          chunks.push(e.data);
        }
      };
      recorder.start();
      listen();
      return "";
    } catch (err) {
      release();
      return (err && err.name) || "NotAllowedError";
    }
  }

  // Stops and returns the clip as base64 WAV. An empty string means nothing was captured, which the page reports as
  // "we did not catch that" rather than as a failure.
  async function stop() {
    if (!recorder) {
      return "";
    }

    const active = recorder;
    const finished = new Promise(function (resolve) {
      active.onstop = resolve;
    });

    try {
      active.stop();
      await finished;
    } catch {
      release();
      return "";
    }

    const captured = chunks;
    chunks = [];
    release();

    if (captured.length === 0) {
      return "";
    }

    try {
      const blob = new Blob(captured, { type: captured[0].type || "audio/webm" });
      const bytes = await blob.arrayBuffer();
      // Decoding into a 16 kHz context is what resamples the clip; the browser does the rate conversion.
      const ctx = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: RATE });
      const decoded = await ctx.decodeAudioData(bytes);
      const wav = encodeWav(decoded.getChannelData(0), decoded.sampleRate);
      await ctx.close();
      return base64(wav);
    } catch {
      return "";
    }
  }

  function cancel() {
    try {
      if (recorder && recorder.state !== "inactive") {
        recorder.onstop = null;
        recorder.stop();
      }
    } catch {
      // Already stopped; releasing the tracks below is all that is left to do.
    }

    chunks = [];
    release();
  }

  function recording() {
    return recorder !== null;
  }

  function encodeWav(samples, rate) {
    const bytes = new ArrayBuffer(44 + (samples.length * 2));
    const view = new DataView(bytes);

    function text(offset, s) {
      for (let i = 0; i < s.length; i++) {
        view.setUint8(offset + i, s.charCodeAt(i));
      }
    }

    text(0, "RIFF");
    view.setUint32(4, 36 + (samples.length * 2), true);
    text(8, "WAVE");
    text(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, 1, true);
    view.setUint32(24, rate, true);
    view.setUint32(28, rate * 2, true);
    view.setUint16(32, 2, true);
    view.setUint16(34, 16, true);
    text(36, "data");
    view.setUint32(40, samples.length * 2, true);

    let at = 44;
    for (let i = 0; i < samples.length; i++) {
      const s = Math.max(-1, Math.min(1, samples[i]));
      view.setInt16(at, s < 0 ? s * 0x8000 : s * 0x7fff, true);
      at += 2;
    }

    return new Uint8Array(bytes);
  }

  // Chunked so a minute of speech does not blow the argument limit of String.fromCharCode.
  function base64(bytes) {
    let binary = "";
    const step = 0x8000;
    for (let i = 0; i < bytes.length; i += step) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + step));
    }

    return window.btoa(binary);
  }

  return { start: start, stop: stop, cancel: cancel, recording: recording, level: level, devices: devices };
})();
