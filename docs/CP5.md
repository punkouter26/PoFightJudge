# CP5 — FIGHT end to end

What the checkpoint asks for, and what actually ran on 2026-09-07.

## Green with the scripted host

A whole fight runs in a real browser with no Gemini key: two tags, a microphone playing the fixture, the scripted
host driving the show from those frames, and the ruling at the end.

| Tier | Tests | Result |
| --- | --- | --- |
| Unit | 423 | pass |
| Integration | 20 (+1 skipped) | pass |
| E2EAPI | 32 | pass |
| E2EUI | 13 | pass |

`FightFlowTests` covers the three things worth proving:

- **A fight from setup to ruling.** Tags typed, microphone opened, captions arriving, the phase moving off the
  introduction, and the page handing over to the verdict. Because the scripted host only advances on microphone
  frames, captions appearing at all is proof the whole audio path works: worklet, hub, orchestrator, recording.
- **Stopping a fight from the room**, which closes the microphone with it.
- **A fight that never existed**, which says so instead of opening a microphone to record into nothing.

Both fighters are created the moment a fight starts, so a record has somewhere to land even if it ends badly.

## Screenshots

`docs/screenshots/fight-setup.png` and `fight-live.png`, taken on each run.

## Fixed while getting here

- The capture worklet was loaded by a relative path, which resolved to `/fight/js/...` on the live page and would
  have failed on the only page that uses it.
- The host could overwrite the topic the couple agreed before the microphone went on. It is told to restate an
  agreed topic, and a restatement that came back slightly different replaced what they had actually typed. The topic
  is now theirs, on the same rule that stops the host renaming them.
- The scoreboard printed a fresh fighter's tag twice, once as the tag and once as a display name that was the tag.
- The roster endpoint did not exist, so every fight page logged a 404 fetching it. Listing and reading a fighter
  were brought forward from T46.

## Outstanding

The real-key leg has **not** run: greeting by tag within fifteen seconds against the live endpoint. No key is
configured on this machine, and copying the secrets out of Key Vault is an ask-first step in T51. The live smoke
test exists and skips (`GeminiLiveSmokeTests`); it is the only thing that can confirm the wire format against the
real service, and it is carried to T51 with the CP4 measurement rather than recorded as done.
