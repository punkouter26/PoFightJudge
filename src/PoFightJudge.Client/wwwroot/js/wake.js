// Keeping the screen on while an argument is running.
//
// A fight is several minutes of two people talking at a phone that nobody is touching, which is exactly when a
// phone decides to sleep — and a sleeping phone closes the microphone. The lock is asked for when the argument
// starts and given back when it ends. The browser drops it by itself whenever the tab is hidden, so it is asked for
// again when the tab comes back. Where there is no such thing (older Safari), nothing happens and nothing breaks.
window.PoWake = (function () {
  let wanted = false;
  let lock = null;

  async function take() {
    if (!wanted || lock || !('wakeLock' in navigator) || document.visibilityState !== 'visible') {
      return;
    }

    try {
      lock = await navigator.wakeLock.request('screen');
      lock.addEventListener('release', function () { lock = null; });
    } catch (e) {
      // Refused: low battery, or a browser that only pretends to have it. The argument goes on regardless.
    }
  }

  document.addEventListener('visibilitychange', take);

  return {
    hold: function () {
      wanted = true;
      take();
    },
    release: function () {
      wanted = false;
      if (lock) {
        lock.release().catch(function () {});
        lock = null;
      }
    },
  };
})();
