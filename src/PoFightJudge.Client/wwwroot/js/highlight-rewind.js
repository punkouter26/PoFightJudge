// PoHighlightRewind — T116. The moment a highlight clip plays, the card it sits on runs a quick rewind
// effect (radial blur + chromatic aberration via CSS), and the audio is pitched down a half-step for
// the duration. The card is found by walking up from the audio element; the effect is added by class so
// the CSS owns the look and the JS only decides when it fires.
//
// Reduced motion turns the visual half off; the audio pitch dip stays because it is the moment, not
// decoration.
window.PoHighlightRewind = (function () {
  "use strict";

  const PITCH = 0.9438; // one semitone down
  const DURATION_MS = 800;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  function onPlay(event) {
    const audio = event.currentTarget;
    const card = audio.closest ? audio.closest(".moment") : null;
    if (!card) {
      return;
    }

    if (!reducedMotion()) {
      card.classList.add("rewinding");
      window.setTimeout(function () {
        card.classList.remove("rewinding");
      }, DURATION_MS);
    }

    try {
      audio.preservesPitch = false;
      audio.playbackRate = PITCH;
    } catch (e) {
      // Some browsers (notably mobile Safari) refuse playbackRate on a media element without a Web Audio
      // source; the visual half is the user's reward and still works.
    }

    const onEnd = function () {
      try {
        audio.playbackRate = 1;
      } catch (e) {
        // Best effort: the audio element is going away anyway.
      }
      audio.removeEventListener("ended", onEnd);
    };
    audio.addEventListener("ended", onEnd);
  }

  /** Wires the play listener onto every audio in the reel. Idempotent: a second call is a no-op. */
  function wire(root) {
    const container = root || document;
    const audios = container.querySelectorAll ? container.querySelectorAll(".reel audio.player") : [];
    audios.forEach(function (audio) {
      if (audio.dataset.rewindWired === "1") {
        return;
      }
      audio.dataset.rewindWired = "1";
      audio.addEventListener("play", onPlay);
    });
  }

  return { wire: wire };
})();