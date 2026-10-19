// PoTerrain — a clip, seen as mountains. When a highlight is played, its audio is decoded here, cut into short
// overlapping frames, and each frame's spectrum becomes one ridge: forty-eight bands, log-spaced across the range a
// voice lives in, loudest as the peaks. The ridges are laid out in perspective, the one being heard at the front and
// the ones still to come receding toward a horizon, and the range rolls toward the listener as the clip plays.
//
// Canvas 2D, not WebGL: forty ridges of forty-eight points is a few thousand line segments, and the painter's
// algorithm (back to front, each ridge filled with the background so it hides what is behind it) is the whole trick.
// Everything is best-effort: a clip that will not decode keeps its player and loses its picture.
window.PoTerrain = (function () {
  "use strict";

  const BANDS = 48;
  const ROWS = 40;
  const FRAME = 1024;
  const HOP = 512;
  const RATE = 16000;

  const drawn = new WeakSet();

  function still() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  function colour(token, fallback) {
    try {
      return (window.PoGfx && window.PoGfx.colour(token)) || fallback;
    } catch (e) {
      return fallback;
    }
  }

  /** An iterative radix-2 FFT, in place. `re` and `im` are the same power-of-two length. */
  function fft(re, im) {
    const n = re.length;
    for (let i = 1, j = 0; i < n; i++) {
      let bit = n >> 1;
      for (; j & bit; bit >>= 1) {
        j ^= bit;
      }

      j ^= bit;
      if (i < j) {
        let t = re[i]; re[i] = re[j]; re[j] = t;
        t = im[i]; im[i] = im[j]; im[j] = t;
      }
    }

    for (let size = 2; size <= n; size <<= 1) {
      const half = size >> 1;
      const angle = -2 * Math.PI / size;
      const wr = Math.cos(angle);
      const wi = Math.sin(angle);
      for (let start = 0; start < n; start += size) {
        let cr = 1;
        let ci = 0;
        for (let k = 0; k < half; k++) {
          const a = start + k;
          const b = a + half;
          const br = re[b] * cr - im[b] * ci;
          const bi = re[b] * ci + im[b] * cr;
          re[b] = re[a] - br;
          im[b] = im[a] - bi;
          re[a] += br;
          im[a] += bi;
          const next = cr * wr - ci * wi;
          ci = cr * wi + ci * wr;
          cr = next;
        }
      }
    }
  }

  /**
   * The clip as rows of band levels, 0 to 1: a Hann-windowed FFT every HOP samples, each band the peak of the bins
   * it covers (log-spaced from 80 Hz to 8 kHz, where a voice is), then decibels against the loudest band anywhere in
   * the clip over a 60 dB range — so a quiet clip still has mountains, and silence is flat.
   */
  function spectrogram(samples, rate) {
    const hann = new Float32Array(FRAME);
    for (let i = 0; i < FRAME; i++) {
      hann[i] = 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / (FRAME - 1));
    }

    const low = 80;
    const high = Math.min(8000, rate / 2);
    const edges = [];
    for (let b = 0; b <= BANDS; b++) {
      edges.push(Math.max(1, Math.round((low * Math.pow(high / low, b / BANDS) * FRAME) / rate)));
    }

    const re = new Float32Array(FRAME);
    const im = new Float32Array(FRAME);
    const frames = [];
    let loudest = 1e-12;
    for (let start = 0; start + FRAME <= samples.length; start += HOP) {
      for (let i = 0; i < FRAME; i++) {
        re[i] = samples[start + i] * hann[i];
        im[i] = 0;
      }

      fft(re, im);
      const row = new Float32Array(BANDS);
      for (let b = 0; b < BANDS; b++) {
        let peak = 0;
        for (let k = edges[b]; k <= Math.max(edges[b], edges[b + 1] - 1); k++) {
          peak = Math.max(peak, re[k] * re[k] + im[k] * im[k]);
        }

        row[b] = peak;
        loudest = Math.max(loudest, peak);
      }

      frames.push(row);
    }

    frames.forEach(function (row) {
      for (let b = 0; b < BANDS; b++) {
        row[b] = Math.max(0, 1 + (10 * Math.log10(row[b] / loudest + 1e-12)) / 60);
      }
    });

    return { frames: frames, step: HOP / rate };
  }

  /**
   * One picture of the range: the row being heard at the front, the rows still to come behind it, receding. The
   * fraction of a frame already played slides every row toward the viewer, so the range rolls rather than jumps.
   */
  function draw(state) {
    const canvas = state.canvas;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }

    const ctx = canvas.getContext("2d");
    ctx.clearRect(0, 0, width, height);

    const position = state.audio.currentTime / state.spec.step;
    const now = Math.floor(position);
    const slide = position - now;
    const horizon = height * 0.1;
    const ground = height * 0.94;
    const ink = ctx.createLinearGradient(0, 0, width, 0);
    ink.addColorStop(0, state.cool);
    ink.addColorStop(0.5, state.accent);
    ink.addColorStop(1, state.warm);

    for (let r = ROWS - 1; r >= 0; r--) {
      const depth = Math.max(0, (r - slide) / ROWS);
      const scale = 1 / (1 + depth * 3);
      const base = horizon + (ground - horizon) * scale;
      const span = width * 0.94 * scale;
      const left = (width - span) / 2;
      const lift = height * 0.62 * scale;
      const row = state.spec.frames[now + r];

      ctx.beginPath();
      ctx.moveTo(left, base);
      for (let b = 0; b < BANDS; b++) {
        const level = row ? row[b] : 0;
        ctx.lineTo(left + (span * b) / (BANDS - 1), base - level * level * lift);
      }

      ctx.lineTo(left + span, base);
      ctx.closePath();
      ctx.fillStyle = state.floor;
      ctx.fill();

      ctx.globalAlpha = 0.18 + 0.82 * (1 - depth);
      ctx.strokeStyle = ink;
      ctx.lineWidth = (r === 0 ? 2.2 : 1.1) * dpr;
      ctx.shadowColor = r === 0 ? state.accent : "transparent";
      ctx.shadowBlur = r === 0 ? 10 * dpr : 0;
      ctx.stroke();
      ctx.globalAlpha = 1;
      ctx.shadowBlur = 0;
    }
  }

  /** Redraws every frame while the clip plays, and once on every seek or pause. Reduced motion: only on those. */
  function follow(state) {
    let frame = 0;
    function tick() {
      frame = 0;
      draw(state);
      if (!state.audio.paused && !state.audio.ended && state.canvas.isConnected) {
        frame = requestAnimationFrame(tick);
      }
    }

    function wake() {
      if (still()) {
        draw(state);
      } else if (!frame) {
        frame = requestAnimationFrame(tick);
      }
    }

    ["play", "seeked", "pause", "ended", "timeupdate"].forEach(function (name) {
      state.audio.addEventListener(name, wake);
    });
    wake();
  }

  return {
    /**
     * Draws the clip playing in `audioSelector` onto the canvas at `canvasSelector`, in `token`'s colour at the cool
     * end. The clip has to be a data URL (it is: the reel fetches it with the caller's credentials and inlines it).
     * True when it was taken on; false, and nothing drawn, for anything else.
     */
    attach: function (audioSelector, canvasSelector, token) {
      try {
        const audio = document.querySelector(audioSelector);
        const canvas = document.querySelector(canvasSelector);
        const source = audio ? audio.getAttribute("src") || "" : "";
        const comma = source.indexOf(",");
        if (!audio || !canvas || drawn.has(canvas) || !source.startsWith("data:") || comma < 0) {
          return false;
        }

        const Offline = window.OfflineAudioContext || window.webkitOfflineAudioContext;
        if (!Offline) {
          return false;
        }

        const binary = atob(source.slice(comma + 1));
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }

        drawn.add(canvas);
        new Offline(1, 1, RATE).decodeAudioData(bytes.buffer).then(function (buffer) {
          follow({
            canvas: canvas,
            audio: audio,
            spec: spectrogram(buffer.getChannelData(0), buffer.sampleRate),
            cool: colour(token || "--po-p1", "#3fa9f5"),
            accent: colour("--po-accent", "#ffcc33"),
            warm: colour("--po-danger", "#ff5a5a"),
            floor: colour("--po-surface", "#151923"),
          });
        }).catch(function () {
          // Not a clip this browser can decode: the player stays, the picture does not come.
        });
        return true;
      } catch (e) {
        return false;
      }
    },
  };
})();
