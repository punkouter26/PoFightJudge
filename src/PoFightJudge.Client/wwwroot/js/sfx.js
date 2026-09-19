// PoSfx — every sound in this app that is not a voice, made up on the spot out of oscillators and noise. Nothing
// here is a file: a bell is four inharmonic partials on an exponential decay, a slap is a filtered noise burst with
// a pitch drop under it. That keeps the download at zero bytes and lets a sound take a parameter — the counting
// blip climbs with the number it is counting.
//
// Kept apart from PoAudio (a watch's synthesized lines) and PoLive (a fight's host), which own their own contexts.
// This one owns the third: a master gain into a compressor, so a bell landing under a voice cannot clip the mix.
// When an effect wants to be heard over a voice it asks those two to duck, which they do on their own bus.
//
// Muting is read from localStorage here rather than waited for from .NET: a sound cannot be taken back once it has
// been played, so the very first one has to know. Same key shape as theme.js, same tolerance for a quoted value.
window.PoSfx = (function () {
  "use strict";

  const STORAGE_KEY = "po-sound";
  const OFF = "off";
  const MASTER_GAIN = 0.7;
  const NOISE_SECONDS = 2;

  // Gains ramp exponentially, and an exponential ramp can neither reach nor pass through zero, so silence is a very
  // small number instead. Below about 1e-4 nothing is audible on any output.
  const SILENT = 0.0001;

  let ctx = null;
  let master = null;
  let noise = null;
  let muted = readMuted();
  let broken = false;

  function readMuted() {
    try {
      let stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        stored = stored.replace(/^"|"$/g, "");
      }

      return stored === OFF;
    } catch (e) {
      return false;
    }
  }

  // The context is built on the first sound, never at load: one created before a gesture starts suspended, and the
  // browser logs a warning about it on every page of the app whether or not anything ever plays.
  function ensure() {
    if (broken) {
      return null;
    }

    try {
      if (!ctx) {
        const Ctor = window.AudioContext || window.webkitAudioContext;
        ctx = new Ctor();
        const limiter = ctx.createDynamicsCompressor();
        limiter.threshold.value = -10;
        limiter.ratio.value = 12;
        limiter.attack.value = 0.003;
        limiter.release.value = 0.12;
        master = ctx.createGain();
        master.gain.value = muted ? 0 : MASTER_GAIN;
        master.connect(limiter).connect(ctx.destination);
      }

      if (ctx.state === "suspended") {
        ctx.resume().catch(function () { /* still waiting on a gesture */ });
      }

      return ctx;
    } catch (e) {
      // No Web Audio at all (an old browser, a locked-down test host): every sound from here on is a no-op.
      broken = true;
      return null;
    }
  }

  /** White noise, made once and re-used: every percussive sound here is a filtered slice of the same two seconds. */
  function noiseBuffer() {
    if (!noise) {
      noise = ctx.createBuffer(1, ctx.sampleRate * NOISE_SECONDS, ctx.sampleRate);
      const data = noise.getChannelData(0);
      for (let i = 0; i < data.length; i++) {
        data[i] = Math.random() * 2 - 1;
      }
    }

    return noise;
  }

  /** One oscillator with an attack/decay envelope, optionally sweeping in pitch. Fire and forget. */
  function tone(at, options) {
    const o = ctx.createOscillator();
    const g = ctx.createGain();
    const duration = options.duration;
    o.type = options.type || "sine";
    o.frequency.setValueAtTime(options.freq, at);
    if (options.sweepTo) {
      o.frequency.exponentialRampToValueAtTime(Math.max(options.sweepTo, 1), at + duration);
    }

    const peak = Math.max(options.gain, SILENT * 2);
    const attack = options.attack || 0.004;
    g.gain.setValueAtTime(SILENT, at);
    g.gain.exponentialRampToValueAtTime(peak, at + attack);
    g.gain.exponentialRampToValueAtTime(SILENT, at + duration);

    o.connect(g).connect(master);
    o.start(at);
    o.stop(at + duration + 0.02);
  }

  /** A slice of the noise buffer through one filter — the transient half of anything that hits something. */
  function hit(at, options) {
    const src = ctx.createBufferSource();
    src.buffer = noiseBuffer();
    const filter = ctx.createBiquadFilter();
    filter.type = options.filter || "bandpass";
    filter.frequency.setValueAtTime(options.freq, at);
    if (options.sweepTo) {
      filter.frequency.exponentialRampToValueAtTime(Math.max(options.sweepTo, 20), at + options.duration);
    }

    if (options.q) {
      filter.Q.value = options.q;
    }

    const g = ctx.createGain();
    g.gain.setValueAtTime(Math.max(options.gain, SILENT * 2), at);
    g.gain.exponentialRampToValueAtTime(SILENT, at + options.duration);
    src.connect(filter).connect(g).connect(master);
    src.start(at, Math.random() * (NOISE_SECONDS - options.duration - 0.05));
    src.stop(at + options.duration + 0.02);
  }

  /** A boxing bell: four inharmonic partials, struck. The ratios are what stop it sounding like a doorbell. */
  function bell(at, gain) {
    const ratios = [1, 1.51, 2.31, 3.07];
    const gains = [1, 0.5, 0.32, 0.18];
    for (let i = 0; i < ratios.length; i++) {
      tone(at, { type: "sine", freq: 660 * ratios[i], duration: 1.6 - i * 0.25, gain: gain * gains[i], attack: 0.002 });
    }

    hit(at, { freq: 5200, duration: 0.04, gain: gain * 0.5, q: 0.8 });
  }

  function gavel(at, gain) {
    hit(at, { freq: 1100, duration: 0.05, gain: gain, filter: "lowpass" });
    tone(at, { type: "triangle", freq: 190, sweepTo: 110, duration: 0.16, gain: gain * 0.8 });
  }

  /** A short run of notes. Playing them from one call is what makes a motif read as one gesture. */
  function motif(at, notes, type, gain, step, duration) {
    for (let i = 0; i < notes.length; i++) {
      tone(at + i * step, { type: type, freq: notes[i], duration: duration, gain: gain, attack: 0.008 });
    }
  }

  // The catalogue. A name that is not here plays nothing — an old page asking for a sound this build does not have
  // is not worth an exception. `value` is 0–1 and only means anything to the voices that read it.
  const VOICES = {
    "bell": function (at) { bell(at, 0.5); },
    "bell3": function (at) { bell(at, 0.45); bell(at + 0.3, 0.45); bell(at + 0.6, 0.5); },
    "slap": function (at) {
      hit(at, { freq: 2600, duration: 0.09, gain: 0.9, q: 1.1 });
      hit(at, { freq: 6000, duration: 0.03, gain: 0.5, filter: "highpass" });
      tone(at, { type: "sine", freq: 220, sweepTo: 48, duration: 0.18, gain: 0.7 });
    },
    "gavel": function (at) { gavel(at, 0.7); },
    "gavel3": function (at) { gavel(at, 0.6); gavel(at + 0.22, 0.6); gavel(at + 0.44, 0.75); },
    "tick": function (at) { tone(at, { type: "sine", freq: 1400, duration: 0.03, gain: 0.09 }); },
    "whoosh": function (at) { hit(at, { freq: 300, sweepTo: 3400, duration: 0.38, gain: 0.35, q: 2.4 }); },
    "stinger-intro": function (at) { motif(at, [392, 523.3, 659.3], "square", 0.16, 0.09, 0.2); },
    "stinger-probe": function (at) { motif(at, [587.3, 440], "triangle", 0.2, 0.11, 0.24); },
    "stinger-verdict": function (at) {
      motif(at, [523.3, 659.3, 784, 1046.5], "sawtooth", 0.12, 0.07, 0.22);
      bell(at + 0.34, 0.34);
    },
    "fanfare": function (at) {
      motif(at, [523.3, 659.3, 784, 1046.5, 1318.5], "square", 0.14, 0.075, 0.3);
      bell(at + 0.42, 0.4);
    },
    "clipping": function (at) {
      tone(at, { type: "sine", freq: 1800, duration: 0.05, gain: 0.22 });
      tone(at + 0.08, { type: "sine", freq: 1800, duration: 0.05, gain: 0.22 });
    },
    // T111: a wax-seal stamp landing on a fallacy — a soft thump under a paper-rough click.
    "thump": function (at) {
      tone(at, { type: "sine", freq: 90, sweepTo: 40, duration: 0.18, gain: 0.55, attack: 0.001 });
      hit(at, { freq: 2200, duration: 0.03, gain: 0.32, q: 1.4 });
      hit(at + 0.01, { freq: 900, duration: 0.04, gain: 0.22, filter: "lowpass" });
    },
    "cue": function (at) { tone(at, { type: "sine", freq: 660, sweepTo: 880, duration: 0.26, gain: 0.13, attack: 0.03 }); },
    // The one voice the caller shapes: a scoreboard counting up plays this per tick, climbing with the number.
    "count": function (at, value) { tone(at, { type: "sine", freq: 440 + value * 620, duration: 0.035, gain: 0.06 }); },
  };

  /** Dips the voice buses so an effect lands over the top of a line instead of underneath it. */
  function duck(seconds) {
    const span = typeof seconds === "number" && seconds > 0 ? seconds : 0.35;
    const buses = [window.PoAudio, window.PoLive];
    for (let i = 0; i < buses.length; i++) {
      const bus = buses[i];
      if (bus && typeof bus.duck === "function") {
        try {
          bus.duck(span);
        } catch (e) {
          // A bus that will not duck still plays; the effect is simply level with it.
        }
      }
    }
  }

  return {
    /** Wakes the context on a user gesture, so the first real sound is not swallowed by the autoplay policy. */
    arm: function () {
      ensure();
    },

    isMuted: function () {
      return muted;
    },

    setMuted: function (value) {
      muted = !!value;
      if (!ctx) {
        return;
      }

      try {
        if (muted) {
          // Whatever is already scheduled goes with it: silence that arrives half a second late is not silence.
          master.gain.cancelScheduledValues(ctx.currentTime);
          master.gain.setValueAtTime(0, ctx.currentTime);
        } else {
          master.gain.setValueAtTime(MASTER_GAIN, ctx.currentTime);
        }
      } catch (e) {
        // Nothing scheduled, or a context that has gone away underneath us.
      }
    },

    /**
     * Plays one effect. `value` is 0–1 for the voices that take one. Silent when muted, when the name is unknown,
     * and when the browser has no Web Audio at all — an app that stopped because a bell would not ring is worse
     * than one that rings no bell.
     */
    play: function (name, value) {
      if (muted || typeof name !== "string") {
        return;
      }

      const voice = VOICES[name];
      if (!voice || !ensure()) {
        return;
      }

      try {
        voice(ctx.currentTime + 0.005, typeof value === "number" ? Math.min(Math.max(value, 0), 1) : 0);
      } catch (e) {
        // One malformed schedule must not take the page with it.
      }
    },

    duck: duck,
  };
})();
