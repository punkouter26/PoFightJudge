// PoLevelClip — fires sparks from the level meter when the bar crosses into the danger zone.
//
// The level lives on document.documentElement as --po-mic-level (set by mic.js on a spoken turn, live-audio.js
// during a fight), and the meter reads it from CSS. That bar paint path is untouched: this file watches the
// variable on requestAnimationFrame and, when it crosses 0.9, asks PoParticles.burst to throw a 'sparks' burst
// from the lamp. The same mic-safety rule the rest of the effects layer honours is honoured here — reduced motion
// turns the whole thing off, no canvas means no canvas, and there is no throw inside a fight's recording window
// because the meter is not the source of the particles' audio (Sfx.Clipping already runs there).
window.PoLevelClip = (function () {
  "use strict";

  const CLIP_AT = 0.9;
  const COOLDOWN_MS = 450;
  const PROPERTY = "--po-mic-level";

  let raf = 0;
  let lastFire = 0;
  let watching = false;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  function readLevel() {
    const raw = getComputedStyle(document.documentElement).getPropertyValue(PROPERTY).trim();
    const value = parseFloat(raw);
    return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
  }

  function step() {
    raf = 0;
    if (!watching) {
      return;
    }

    const now = performance.now();
    const level = readLevel();
    if (level >= CLIP_AT && now - lastFire >= COOLDOWN_MS) {
      lastFire = now;
      // PoParticles answers no rather than throwing when the canvas isn't there or motion is off.
      if (window.PoParticles && typeof window.PoParticles.burst === "function") {
        window.PoParticles.burst(".meter", "sparks", ".lamp");
      }
    }

    raf = window.requestAnimationFrame(step);
  }

  /** Watches the meter level. Idempotent — a second call is a no-op. Safe under prerendering (no document yet). */
  function watch() {
    if (watching || typeof document === "undefined") {
      return;
    }

    if (reducedMotion()) {
      return;
    }

    watching = true;
    raf = window.requestAnimationFrame(step);
  }

  /** Stops watching and drops the pending frame. Called on dispose so an unmounted meter does not keep firing. */
  function stop() {
    watching = false;
    if (raf) {
      window.cancelAnimationFrame(raf);
      raf = 0;
    }
  }

  return { watch: watch, stop: stop };
})();
