# CP6 — the verdict

What the checkpoint asks for, and what ran on 2026-09-07.

## A fight is readable end to end

`FightFlowTests` now runs past the ruling and into the report: a fight argued in a browser, read by the offline
stand-ins, and rendered in full. No key anywhere in the path.

| Tier | Tests | Result |
| --- | --- | --- |
| Unit | 510 | pass |
| Integration | 20 (+1 skipped) | pass |
| E2EAPI | 40 | pass |
| E2EUI | 13 | pass |

Asserted on the page itself, not just on the API:

- Both fighters reported on, with the winner marked.
- Twenty trait bars — the ten judged traits, each side.
- Every measured number present. The description of the metrics is held to the contract by a unit test, so a field
  added to the report and not described would fail before it could silently vanish from the page.
- Three pieces of advice each.

`docs/screenshots/verdict.png` is taken on each run.

## Decisions worth keeping

- All four tabs render rather than only the selected one. A report is a document, and find-in-page and printing
  should reach every part of it.
- A clip is fetched through the API and played as a data URL, not linked. The recording lives in a private
  container and the route carries the caller's own credentials.
- With fewer than two points there is nothing to draw, so the emotion chart shows the whole shape instead of a dot
  pretending to be a trend.
- An uncertain speaker attribution is said at the top of the ruling, before anybody reads a word of it.

## The real-key leg — measured 2026-09-07

A whole fight against live Gemini, analysed by the real pipeline: **ready in 38.1 s**, inside the forty-five the
checkpoint asks for. 381 words came from the live-caption transcript; 197 words of host speech were recognised as
the host and left out.

### The bugs this leg found

- **The judge asked for both players in one response schema, and Gemini refused every such request.** Bisected
  against the live endpoint: one player's assessment is accepted; the same schema with a second player carrying
  fourteen of the twenty-seven fields is not; `$ref` is rejected outright, so the assessment cannot be declared
  once and referenced twice. Each player is now assessed in a call of their own, sent in order so the second finds
  the shared prefix — the instructions, the recording, the session data — still warm.
- **A busy discount tier lost the whole analysis.** The judge runs on flex, which answers "this model is currently
  experiencing high demand" under load. Five patient retries over half a minute were all refused, and the fight
  ended with no report. The same request now goes again at the standard tier rather than being abandoned.
- **The retry was too impatient to be a retry**: three attempts inside two seconds, and a full-jitter backoff that
  could try again after almost no wait at all. Five attempts, seconds apart, half the window fixed.

### Still unverified

The transcription fallback (`gemini-3.5-transcribe`) has not run against the real service. Both real fights had
usable live captions, which the pipeline prefers, so the fallback was never reached.
