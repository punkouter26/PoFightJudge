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

## Outstanding

The timing the checkpoint names — a full analysis inside forty-five seconds — is a real-key measurement. With the
stand-ins it is instantaneous, which proves the path and not the budget. It joins the CP4 and CP5 measurements
waiting on a key in T51.
