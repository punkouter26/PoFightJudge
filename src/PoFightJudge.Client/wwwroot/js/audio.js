// PoAudio — the browser side of playback. One AudioContext, one rolling schedule clock, so consecutive chunks of a
// line butt up against each other instead of leaving a gap or overlapping. This is WATCH playback; a live fight
// captures through mic.js and plays the host through live-audio.js, which owns its own context and bus.
const PoAudio = (() => {
  const SAMPLE_RATE = 24000; // raw PCM from Gemini TTS / the fake is always 24 kHz mono 16-bit
  let context = null;
  let nextStartTime = 0;
  let sources = [];
  let endedCallback = null;

  function ensureContext() {
    if (!context) {
      const Ctor = window.AudioContext || window.webkitAudioContext;
      context = new Ctor();
    }
    if (context.state === 'suspended') {
      // Autoplay policy: a click has already happened by the time anything plays.
      context.resume();
    }
    return context;
  }

  function base64ToBytes(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  }

  /** Raw 16-bit little-endian PCM has no container, so it is decoded by hand into a mono buffer. */
  function pcmToBuffer(ctx, bytes) {
    const samples = Math.floor(bytes.length / 2);
    const buffer = ctx.createBuffer(1, Math.max(samples, 1), SAMPLE_RATE);
    const channel = buffer.getChannelData(0);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (let i = 0; i < samples; i++) {
      channel[i] = view.getInt16(i * 2, true) / 32768;
    }
    return buffer;
  }

  async function toBuffer(ctx, base64, format) {
    const bytes = base64ToBytes(base64);
    if (format === 'pcm') {
      return pcmToBuffer(ctx, bytes);
    }
    // mp3 (and anything else the browser knows) goes through the platform decoder.
    return await ctx.decodeAudioData(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
  }

  return {
    /**
     * Plays one utterance immediately, replacing whatever was playing. Resolves with its duration in seconds —
     * once it is actually scheduled, which is the point: the caller waits out the line before moving on, and a
     * promise that resolved before the decode finished had the speaker cut off a second into their sentence.
     */
    async play(base64, format) {
      if (!base64) {
        return 0;
      }
      PoAudio.stop();
      nextStartTime = ensureContext().currentTime;
      return await PoAudio.enqueue(base64, format);
    },

    /**
     * Appends a chunk to the rolling schedule: each chunk starts where the previous one ends, so a line synthesized
     * clause by clause still plays as one continuous utterance. Returns the scheduled duration in seconds.
     */
    async enqueue(base64, format) {
      if (!base64) {
        return 0;
      }
      const ctx = ensureContext();
      const buffer = await toBuffer(ctx, base64, format);
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(ctx.destination);
      const startAt = Math.max(nextStartTime, ctx.currentTime);
      source.start(startAt);
      nextStartTime = startAt + buffer.duration;
      sources.push(source);
      source.onended = () => {
        sources = sources.filter((s) => s !== source);
        if (sources.length === 0 && endedCallback) {
          const callback = endedCallback;
          endedCallback = null;
          callback.invokeMethodAsync('OnPlaybackEnded');
        }
      };
      return buffer.duration;
    },

    /** Registers a .NET object to be told when the queue drains. */
    onEnded(dotNetRef) {
      endedCallback = dotNetRef;
    },

    /** Stops everything and resets the schedule clock. */
    stop() {
      for (const source of sources) {
        try {
          source.stop();
        } catch {
          // Already finished; nothing to stop.
        }
      }
      sources = [];
      endedCallback = null;
      nextStartTime = context ? context.currentTime : 0;
    },

    /** Seconds of audio still scheduled ahead of now. */
    pending() {
      return context ? Math.max(0, nextStartTime - context.currentTime) : 0;
    },
  };
})();

window.PoAudio = PoAudio;
