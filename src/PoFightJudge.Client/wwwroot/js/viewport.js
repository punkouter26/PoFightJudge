// PoViewport — one media query, shared. The breakpoint lives in the design system (40rem, the phone → large phone
// line), and .NET needs to know which side of it we are on because a RadzenDataGrid drops columns through a
// parameter rather than through CSS: a column hidden with display:none is still measured into the table's width.
//
// One matchMedia per query, however many components ask. Nothing here throws: a browser with no matchMedia simply
// reports the wide layout, which is the one that shows everything.
window.PoViewport = (function () {
  "use strict";

  const watchers = new Map();

  function get(query) {
    let entry = watchers.get(query);
    if (entry) {
      return entry;
    }

    let list = null;
    try {
      list = window.matchMedia(query);
    } catch (e) {
      return null;
    }

    entry = { list: list, refs: [] };
    const notify = function () {
      for (const ref of entry.refs.slice()) {
        try {
          ref.invokeMethodAsync("OnViewportChanged", list.matches);
        } catch (e) {
          // The component is gone and did not say so; drop it rather than keep failing.
          const at = entry.refs.indexOf(ref);
          if (at >= 0) { entry.refs.splice(at, 1); }
        }
      }
    };

    if (typeof list.addEventListener === "function") {
      list.addEventListener("change", notify);
    } else if (typeof list.addListener === "function") {
      list.addListener(notify);
    }

    watchers.set(query, entry);
    return entry;
  }

  /** Registers a .NET object for changes and answers where we are right now. */
  function watch(query, ref) {
    const entry = get(query);
    if (!entry) {
      return false;
    }

    entry.refs.push(ref);
    return entry.list.matches;
  }

  function unwatch(query, ref) {
    const entry = watchers.get(query);
    if (!entry) {
      return;
    }

    const at = entry.refs.indexOf(ref);
    if (at >= 0) {
      entry.refs.splice(at, 1);
    }
  }

  return { watch: watch, unwatch: unwatch };
})();
