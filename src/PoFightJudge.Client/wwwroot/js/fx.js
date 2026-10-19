// One-shot stage effects for the play screen. Nothing here is load-bearing: every function is safe to no-op.
window.PoFx = (function () {
  "use strict";

  const DURATION = 700;
  let timer = 0;

  // The play screen's stage, unless the caller names something else — the verdict jolts its report.
  function target(selector) {
    return (selector && document.querySelector(selector)) || document.querySelector(".stage") || document.body;
  }

  // Re-triggers a CSS animation on the stage. Removing the class and forcing a reflow before adding it back is
  // what makes a second slap animate at all; without the reflow the browser coalesces the two class changes.
  function burst(kind, selector) {
    if (typeof kind !== "string" || kind.length === 0) {
      return;
    }

    const el = target(selector);
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

  function scrollBottom(el) {
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }

  // Table mode, for a phone lying between two people: the class on <html> moves the app's own chrome out of the way
  // of the stage (app.css), and fullscreen — only when `full`, because only a click may ask for it — takes the
  // browser's too. A browser that will not go fullscreen (an iPhone has no element fullscreen) still gets the page.
  function table(on, full) {
    document.documentElement.classList.toggle("po-table", !!on);
    if (!full) {
      return;
    }

    try {
      if (on && !document.fullscreenElement && document.documentElement.requestFullscreen) {
        document.documentElement.requestFullscreen().catch(function () {});
      } else if (!on && document.fullscreenElement && document.exitFullscreen) {
        document.exitFullscreen().catch(function () {});
      }
    } catch (e) {
      // Not here, not now. The takeover does not depend on it.
    }
  }

  return { burst: burst, scrollBottom: scrollBottom, reveal: reveal, table: table };
})();
