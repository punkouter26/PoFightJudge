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

## The real-key leg — measured 2026-09-07

The Gemini secrets were seeded from `SCRIPTS/seed-secrets.ps1` (approved that day), and the app was run in
Development against the real endpoints: `/api/diag` reports `fakeAi: false` with Gemini, Fish Audio and Azure
Speech all `Configured`. A whole watch was then driven over HTTP — six generated rounds, the audio for one of
them, and the judge — with `MAH` against `KSH` on the thermostat.

| Measurement | Target | Measured |
| --- | --- | --- |
| Round, time to first token (`generate-round-stream`) | ≤ 5 s | **1.06 s** |
| Round, whole line (`generate-round`) | — | 0.84–1.47 s across six rounds |
| First audio (`round-audio`, mp3 through the routing chain) | ≤ 8 s | **0.91 s** |
| The judge (`verdict`, including persistence) | — | 1.08 s |
| Six rounds and a ruling, end to end | — | **8.5 s** |

The ruling was a genuine draw — 25–25, neither side named — with the advanced statistics computed from the
transcript (word dominance 49.4, lexical complexity 50.9, one logical fallacy) and the match persisted.

### The bug this leg found

The judge's response schema declared `winner` as an enum of the two initials plus an empty string for a draw.
Gemini rejects that outright:

```
400 GenerateContentRequest.generation_config.response_schema.properties[winner].enum[2]: cannot be empty
```

So **every** real-key verdict failed with a 500 while the fakes, which never see a schema, passed. The draw is now
the word `NEITHER` (`WatchRules.NoWinner`), which the parser turns back into an empty winner before anything else
sees it, and `WatchAiTests` asserts both halves. Fixed in `fix: a draw the judge can actually say`.
