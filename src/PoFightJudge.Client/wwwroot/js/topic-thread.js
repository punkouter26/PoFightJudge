// PoTopicThread — T112. The lightning rod that grows while the WATCH topic is being typed. A short SVG
// arc above the topic field, length-clamped to the topic text, plus a quick bolt when the topic crosses
// a six-character threshold so a one-word topic doesn't fire.
//
// Reduced motion turns the bolt off; the arc still draws, since drawing it is the point.
window.PoTopicThread = (function () {
  "use strict";

  const THRESHOLD = 6;
  const ARC_X = 100;
  const ARC_BASE_Y = 100;
  const ARC_Y_STEP = 6;
  const MAX_PATH = 1800;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  /** Builds an SVG path string for the topic. The arc's height grows with the length. */
  function pathFor(length) {
    if (length <= 0) {
      return "";
    }

    const segments = Math.min(length, 60);
    let d = "M0 " + ARC_BASE_Y;
    for (let i = 1; i <= segments; i++) {
      const x = (i / segments) * (ARC_X * 4);
      const y = ARC_BASE_Y - Math.min(i * ARC_Y_STEP, 60);
      d += " L" + x.toFixed(1) + " " + y.toFixed(1);
    }

    return d.length > MAX_PATH ? d.slice(0, MAX_PATH) : d;
  }

  /** Called whenever the topic changes. Builds the path and updates the SVG; fires the bolt at the threshold. */
  function update(selector, topic) {
    const host = typeof selector === "string" ? document.querySelector(selector) : null;
    if (!host) {
      return;
    }

    const path = host.querySelector("[data-topic-thread-path]");
    const bolt = host.querySelector("[data-topic-thread-bolt]");
    if (!path) {
      return;
    }

    const length = (topic || "").length;
    const d = pathFor(length);
    path.setAttribute("d", d);

    if (!bolt) {
      return;
    }

    const aboveThreshold = length >= THRESHOLD;
    const alreadyFired = bolt.dataset.fired === "1";
    if (aboveThreshold && !alreadyFired && !reducedMotion()) {
      bolt.dataset.fired = "1";
      bolt.classList.add("thread-bolt--on");
      if (window.PoSfx && typeof window.PoSfx.play === "function") {
        window.PoSfx.play("whoosh");
      }
      window.setTimeout(function () {
        bolt.classList.remove("thread-bolt--on");
      }, 420);
    } else if (!aboveThreshold) {
      bolt.dataset.fired = "0";
    }
  }

  return { update: update, pathFor: pathFor };
})();
