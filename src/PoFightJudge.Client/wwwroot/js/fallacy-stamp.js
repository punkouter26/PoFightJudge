// PoFallacyStamp — T111. When the verdict page puts the fallacy list on screen, the seals slam in one by one
// and a thump lands under each one. The CSS carries the slam; this script just decides when to start it.
//
// The verifier is the parent .fallacy-item entering view, not the <svg> by itself: a stamp without its quote
// is just a tilted circle. A short delay after the first paint gives the browser a frame to lay everything
// out, which keeps the stamps from landing before the quote is actually there to read them on.
window.PoFallacyStamp = (function () {
  "use strict";

  const STAGGER_MS = 90;
  const THUMP_GAP_MS = 110;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  /** Drops every seal into place and fires the thump for each, in DOM order. Idempotent — a second call is a no-op. */
  function slam(selector) {
    const root = selector ? document.querySelector(selector) : document;
    if (!root) {
      return;
    }

    const seals = root.querySelectorAll(".fallacy-stamp");
    seals.forEach((seal, index) => {
      if (seal.dataset.stamped === "1") {
        return;
      }

      seal.dataset.stamped = "1";
      const delay = index * STAGGER_MS;
      window.setTimeout(function () {
        seal.classList.add("stamp-in");
        if (!reducedMotion() && window.PoSfx && typeof window.PoSfx.play === "function") {
          window.PoSfx.play("thump");
        }
      }, delay + THUMP_GAP_MS * index);
    });
  }

  return { slam: slam };
})();
