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

  function stop() {
    window.clearTimeout(timer);
    const el = target();
    el.className = el.className.replace(/\bfx-[a-z0-9-]+\b/gi, "").trim();
  }

  function scrollBottom(el) {
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }

  return { burst: burst, stop: stop, scrollBottom: scrollBottom };
})();
