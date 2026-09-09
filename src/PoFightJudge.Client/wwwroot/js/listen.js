// PoListen — the browser's own speech recogniser, running alongside a live fight.
//
// This is not the microphone: mic.js and the capture worklet own that, and this opens its own recognition session
// on top of the same device. What it produces is a free second-best transcript for the analysis to read when the
// host's live captions came out too thin, so a fight does not have to pay a model to diarize its recording.
//
// Everything here answers "no" rather than throwing. A browser without the API, a refused permission, a session
// that dies mid-fight — all of them mean the analysis falls back to what it did before, which is a working path.
const PoListen = (() => {
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;

  let recognition = null;
  let sink = null;
  let startedAt = 0;
  let utteranceStart = null;
  let stopping = false;

  /** Seconds since listening began — the same clock the recording is measured on. */
  function now() {
    return Math.max(0, (performance.now() - startedAt) / 1000);
  }

  function handleResult(event) {
    // The first interim result of an utterance is the closest thing this API gives to "they started talking".
    if (utteranceStart === null) {
      utteranceStart = now();
    }

    for (let i = event.resultIndex; i < event.results.length; i++) {
      const result = event.results[i];
      if (!result.isFinal) {
        continue;
      }

      const text = (result[0] && result[0].transcript ? result[0].transcript : '').trim();
      const from = utteranceStart;
      utteranceStart = null;
      if (!text || !sink) {
        continue;
      }

      try {
        sink.invokeMethodAsync('OnHeard', text, from, now());
      } catch {
        // The component went away mid-fight; there is nobody left to tell.
      }
    }
  }

  return {
    /** Whether this browser has the API at all. The fight page asks before it offers to use it. */
    available() {
      return Boolean(Recognition);
    },

    /**
     * Starts listening. Returns true when a session is running. Continuous, with interim results on — not to show
     * them, but because the first interim of an utterance is what dates it.
     */
    start(dotNetRef) {
      if (!Recognition || recognition) {
        return false;
      }

      try {
        recognition = new Recognition();
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.lang = document.documentElement.lang || 'en-US';
        recognition.onresult = handleResult;

        // A continuous session still ends on its own — a long silence, a service timeout. While the fight is on,
        // it is started again; the clock is not reset, so offsets stay measured from the same zero.
        recognition.onend = () => {
          if (stopping || !recognition) {
            return;
          }
          try {
            recognition.start();
          } catch {
            // Already restarting, or the browser has had enough. What was heard so far still counts.
          }
        };
        recognition.onerror = () => {
          // 'no-speech' and 'aborted' are ordinary; onend decides whether to go again.
        };

        sink = dotNetRef;
        startedAt = performance.now();
        utteranceStart = null;
        stopping = false;
        recognition.start();
        return true;
      } catch {
        recognition = null;
        sink = null;
        return false;
      }
    },

    /** Stops listening and releases the session. Safe to call when nothing was ever started. */
    stop() {
      stopping = true;
      const session = recognition;
      recognition = null;
      sink = null;
      if (!session) {
        return;
      }
      try {
        session.onend = null;
        session.onresult = null;
        session.stop();
      } catch {
        // Already stopped.
      }
    },
  };
})();

window.PoListen = PoListen;
