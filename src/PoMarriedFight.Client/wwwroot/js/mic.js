// Records one spoken turn and hands it back as a 16 kHz mono WAV. The server's transcribers take WAV and nothing
// else, so the conversion happens here rather than shipping a container the API would have to unpack.
window.PoMic = (function () {
  "use strict";

  const RATE = 16000;

  let stream = null;
  let recorder = null;
  let chunks = [];

  function release() {
    if (stream) {
      stream.getTracks().forEach(function (t) { t.stop(); });
      stream = null;
    }
    recorder = null;
  }

  // Returns "" when recording started, or the DOMException name when it did not. The caller turns that name into a
  // sentence: a refused permission and a missing device need different advice.
  async function start() {
    try {
      if (recorder) {
        return "";
      }

      if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
        return "NotSupportedError";
      }

      stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
      });

      chunks = [];
      recorder = new MediaRecorder(stream);
      recorder.ondataavailable = function (e) {
        if (e.data && e.data.size > 0) {
          chunks.push(e.data);
        }
      };
      recorder.start();
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

  return { start: start, stop: stop, cancel: cancel, recording: recording };
})();
