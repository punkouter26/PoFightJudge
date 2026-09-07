# CP4 — WATCH end to end

What the checkpoint asks for, and what actually ran on 2026-09-06.

## Green with fakes

Every tier passes with no Gemini key present, which is the condition the checkpoint is about: the whole mode has to
work offline.

| Tier | Tests | Result |
| --- | --- | --- |
| Unit | 265 | pass |
| Integration | 20 | pass |
| E2EAPI | 24 | pass |
| E2EUI | 10 | pass |

The browser tier covers the three matchups a watch can be:

- **Two personas.** Setup, six lines, the ruling. `WatchFlowTests`.
- **A person answering a persona.** The argument stops at their turn, they speak into the microphone, the clip is
  transcribed and lands in the box before it counts. `WatchFlowTests`.
- **A person opening the argument.** The first turn is theirs, on a 390 px screen, with nothing scrolling sideways.
  `WatchHardeningTests`.

Plus leaving mid-argument, a rematch after the ruling, and the screenshots below.

## Screenshots

`docs/screenshots/watch-setup.png`, `watch-play.png`, `watch-verdict.png` — taken by `WatchHardeningTests` on each
run, so they cannot drift from what the app does.

## Fixed while hardening

- The loading skeleton showed while the argument was waiting for a person to speak, which read as the page still
  loading rather than as their turn.
- The transcript timeline left half the page empty and its points photographed as icon ligature names.
- Two inputs published on blur rather than on typing, so the Start and Say buttons stayed grey while a tag or a line
  was being typed into them.
- `ProfilePicker` held a NUL byte in the sentinel that marks the human side, in place of the space its own comment
  described.

## Outstanding

The real-key leg of this checkpoint has **not** run: time to first token and time to first audio against the live
Gemini endpoints. No key is configured on this machine — no environment variable, no user secrets — and copying the
secrets out of Key Vault is an ask-first step that belongs to T51. The measurement is carried there rather than
recorded as done here.
