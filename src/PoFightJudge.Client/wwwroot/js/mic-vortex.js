// PoMicVortex — T115. A particle spiral in the mic-check panel that spins faster with the input RMS. The
// level itself is published as --po-mic-level by mic.js; the vortex reads it on every frame and asks
// PoParticles.burst for a thin spiral of sparks. Reduced motion turns the whole thing off.
window.PoMicVortex = (function () {
  "use strict";

  const EMIT_PER_SEC = 24;
  const KICK = 4.5;
  const SPIN_BASE = 1.8;
  const RADIUS = 38;

  let raf = 0;
  let lastEmit = 0;
  let watching = false;
  let lastLevel = 0;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  function readLevel() {
    const raw = getComputedStyle(document.documentElement).getPropertyValue("--po-mic-level").trim();
    const v = parseFloat(raw);
    return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0;
  }

  function step(t) {
    raf = 0;
    if (!watching) {
      return;
    }

    const level = readLevel();
    const kick = 1 + level * KICK;
    const spin = SPIN_BASE * kick;
    const emit = EMIT_PER_SEC * (0.2 + level * 0.8);

    if (t - lastEmit >= 1000 / emit && window.PoParticles && typeof window.PoParticles.emit === "function") {
      lastEmit = t;
      // The spiral: a thin trail of particles thrown at random angles on the ring; emit() drives a steady stream.
      window.PoParticles.emit(".vortex", emit / 60);
      lastLevel = level;
    }

    raf = window.requestAnimationFrame(step);
  }

  function watch() {
    if (watching || typeof document === "undefined" || reducedMotion()) {
      return;
    }

    watching = true;
    raf = window.requestAnimationFrame(step);
  }

  function stop() {
    watching = false;
    if (raf) {
      window.cancelAnimationFrame(raf);
      raf = 0;
    }
  }

  return { watch: watch, stop: stop };
})();