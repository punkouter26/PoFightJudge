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

## The real-key leg — measured 2026-09-07

`GeminiLiveSmokeTests` ran against the live endpoint and passed in 5 s: the setup frame is accepted, the session
answers out loud, and the wire format this whole phase was written against is confirmed. It is the only test that
can say so, and until now it had never run.

Then a whole fight in a browser against live Gemini (`POMARRIEDFIGHT_E2E_REAL=1`), passing in 3 m 29 s:

| Measurement | Target | Measured |
| --- | --- | --- |
| Host greets both fighters by tag after joining | ≤ 15 s | **14 s** (`set_players` accepted) |
| Debate phase | ≤ 3:00 | 39 s |
| Probe | a question each, one at a time | two `ask_probe` calls, 37 s apart |
| Verdict | delivered | `deliver_verdict -> ok` |

`docs/screenshots/fight-live.png` is from that run: "Al and SM, ready to settle who does the dishes?" — the host
names both tags without asking who anybody is.

### The bugs this leg found

The live half of the fight did not work at first, and none of it was visible to the scripted host:

- A host that never called `start_turn` left the session in Setup. One real fight sat there for eight minutes
  while the host asked to end the argument eleven times and was refused eleven times. Another asked its first
  question during Intro and never left it. Skipping forward is now allowed from both; going back still is not.
- The host reached for a fighter's tag in a tool call — the name it had been using all night — and was told
  "unknown player". Tags count as names now.
- The caption feed printed the host's greeting twice. A live session interleaves the host's transcript with the
  players', so the line to extend is that speaker's own last line, not the last line in the log.
