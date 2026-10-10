// One-shot stage effects for the play screen. Nothing here is load-bearing: every function is safe to no-op.
window.PoFx = (function () {
  "use strict";

  const DURATION = 700;
  let timer = 0;

  function target() {
    return document.querySelector(".stage") || document.body;
  }

  // Re-triggers a CSS animation on the stage. Removing the class and forcing a reflow before adding it back is
  // what makes a second slap animate at all; without the reflow the browser coalesces the two class changes.
  function burst(kind) {
    if (typeof kind !== "string" || kind.length === 0) {
      return;
    }

    const el = target();
    const name = "fx-" + kind.replace(/[^a-z0-9-]/gi, "");
    el.classList.remove(name);
    void el.offsetWidth;
    el.classList.add(name);

    window.clearTimeout(timer);
    timer = window.setTimeout(function () {
      el.classList.remove(name);
    }, DURATION);
  }

  // Brings the first match into the middle of whatever scrolls it. Nothing happens when there is none.
  function reveal(selector) {
    const el = document.querySelector(selector);
    if (el) {
      el.scrollIntoView({ block: "center", behavior: "smooth" });
    }
  }

  // The same, for something the page did not draw itself and so holds no reference to.
  function scrollEnd(selector) {
    scrollBottom(document.querySelector(selector));
  }

  function scrollBottom(el) {
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }

  return { burst: burst, scrollBottom: scrollBottom, scrollEnd: scrollEnd, reveal: reveal };
})();
