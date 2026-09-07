# Verification — SPEC §12, criterion by criterion

Every row names what was run and what it produced. Where a number is quoted it was measured on this machine on the
date given, not estimated. Where something has not been done, the row says so plainly.

Machine: Windows 11, .NET SDK 10.0.400, Docker Desktop, Chromium via Playwright, `az` signed in as
punkouter26@outlook.com. Real-key legs ran on **2026-09-07**, after the Gemini and voice secrets were seeded from
kv-poshared with `SCRIPTS/seed-secrets.ps1`.

## The suite

| Tier | Tests | Result |
| --- | --- | --- |
| Unit | (see below) | pass |
| Integration | | pass |
| E2EAPI | | pass |
| E2EUI | | pass |

## The eighteen

| # | Criterion | Evidence | Verdict |
| --- | --- | --- | --- |
| 1 | Build clean, all tiers pass, coverage ≥ 80 % on Api + Shared | | |
| 2 | With no keys, WATCH and FIGHT complete on the fakes with the banner up | | |
| 3 | Real key: WATCH text ≤ 5 s, audio ≤ 8 s | | |
| 4 | Real key: the host greets both by tag within 15 s and knows a returning fighter's record | | |
| 5 | On the 60 s monologue fixture the host interrupts within 45 ± 5 s | | |
| 6 | The debate phase never exceeds 3:00 | | |
| 7 | The probe phase asks each fighter at least one question, one at a time | | |
| 8 | The spoken verdict names three winners, three reasons and one line of marital advice | | |
| 9 | Analysis ≤ 45 s, and every metric and assessment field reaches the page | | |
| 10 | Two unknown tags create two fighters; each gains a style snapshot; a second fight says "2 fights" | | |
| 11 | Both modes write result rows; both boards rank; deleting a match removes everything it produced | | |
| 12 | History lists both kinds with their badges; the fighters page lists both tags | | |
| 13 | Production auth: `/api/*` 401, `/login` 200, FakeAuth throws, guest needs an explicit flag | | |
| 14 | `/api/diag` never carries a secret value | | |
| 15 | Theme persists, 390 px works, every colour pair ≥ 4.5:1 in both themes | | |
| 16 | No raw form control in any `.razor` file | | |
| 17 | The manifest is served, installable, with the icons a launcher needs | | |
| 18 | `az bicep build` passes and the pipeline builds, tests and publishes | | |
