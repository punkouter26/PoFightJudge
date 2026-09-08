# PoFightJudge — Capability Map

Status: DRAFT for approval (2026-09-06). Companion to [SPEC.md](SPEC.md).

PoFightJudge merges two working apps — **PoMarriedLife** (AI couple argument simulator) and **PoArgueJudge**
(live voice debate host + speech analysis) — into one fresh solution. Each capability below is independently
buildable and testable; the build order respects the dependency graph so every checkpoint is a running app.

## Capabilities

| # | Capability | What it delivers | Ported from | Tested by |
|---|---|---|---|---|
| C0 | **Foundation** | `PoFightJudge.slnx`, `Shared` (ApiRoutes, ConfigKeys, ids, DTOs), Api host (Key Vault, Serilog, env-split auth, degraded mode, `/api/health`, `/api/diag`, `/api/features`), Client shell (tokens, `PageShell`, theme, nav, Login, Home, Health), Table/Blob clients (identity-only), Azurite-over-OAuth script, 5 test projects, CI skeleton | Both (PoMarriedLife's token/CSS + Azurite model; PoArgueJudge's Program structure + security headers) | Unit (secret manager, validator, palette contrast), E2EAPI (health/auth/401), E2EUI (shell boots, theme persists, 390 px) |
| C1 | **Profiles** | `Profile` aggregate (WATCH persona: initials-as-id, role, traits, sliders, voice settings, face pic), repository, CRUD + face upload + AI generate endpoints, seed endpoint, Profiles page + edit dialog | PoMarriedLife | Unit (aggregate, normalisation), Integration (repo round-trip), E2EAPI (CRUD), E2EUI (create profile) |
| C2 | **AI core** | Gemini REST clients per latency class (`fast`, `tts`, `stream`) with resilience, retry handler, `GeminiModelOptions`, `AiLatencyTracker`, **fakes** (Dev/Test only) + "USING FAKE AI" banner | Both | Unit (retry predicate, model option resolution, fake determinism) |
| C3 | **Voice (TTS)** | Fish Audio, Azure Speech, Gemini TTS providers; `RoutingTtsService` (fast-first chain), `TtsAudio` wire format (mp3/pcm), sentence chunking + NDJSON stream, content-addressed blob `TtsCache` | PoMarriedLife | Unit (routing/fallback matrix, chunker, cache key), Integration (blob cache) |
| C4 | **WATCH engine** | Round prompt builder (cache-friendly ordering), `AttitudeSelector`, SLAP interjection, judge prompt, `ArgueScoreCalculator` → `AdvancedStats`, `WatchMatch` aggregate, interactive endpoints (`generate-round`, `round-audio[-stream]`, `verdict`, `transcribe`) behind per-user rate limit, SELF turn (Web Speech → Azure/Gemini transcription), Watch setup + play pages, Web Audio playback interop | PoMarriedLife | Unit (prompt ordering, attitude, scorer, human reply rules), E2EAPI (round → verdict with fakes), E2EUI (full watch with fake AI) |
| C5 | **Live host (FIGHT core)** | `Fighter` (auto-created by tag on `POST /api/fights`), `GeminiLiveClient` (WebSocket, setup message, tool declarations, tolerant parser), `HostPersona` catalogue (Referee default + Puck/Stern/Coach/Roastmaster) with fighter host-digest in the roster, `DebateSession` pure state machine, `DebateOrchestrator` (audio fan-out, tool calls, 1 s tick nudges, silence gating, `WavWriter`, `CaptionTranscript`), `SessionRegistry`, `LiveHub` (SignalR + MessagePack), mic AudioWorklet + host playback JS, Fight setup (tag inputs with autocomplete) + live pages | PoArgueJudge (+ Referee persona, Fighter auto-create) | Unit (state machine, orchestrator, setup builder, parser, persona), Integration (fighter auto-create), E2EAPI (create/join/end with fake Live), E2EUI (fake mic WAV → captions → verdict) |
| C6 | **Analysis pipeline** | Files API client, `gemini-3.5-transcribe` client, `SpeakerMapper`, `SpeechMetrics` (~25 local metrics), two-call judge (`gemini-3.7-flash`, flex tier), `AnalysisSchema` (reflected from DTOs), `JsonRepair`, `HighlightFinder` + `WavSlicer` clips, `AnalysisPipeline` BackgroundService (bounded concurrency), analysis + clip endpoints, Verdict page (tabbed) | PoArgueJudge | Unit (metrics, mapper, schema, repair, highlights, slicer), E2EAPI (202 → 200 with fakes) |
| C7 | **Fighter style profile** | `FightStyleSnapshot` extracted per fighter per analysed fight (tone, phrases, fallacies, opener, CEFR, emotions, best quote, tips) and stored on the `FightResult` row; `StyleProfileBuilder` (deterministic aggregation + one-line host digest); Fighters list + Fighter page (`StyleProfilePanel`, record, form chart); rename/delete | New | Unit (snapshot extraction, aggregation, digest, empty state), Integration (re-analysis replaces), E2EAPI (`/api/fighters`), E2EUI (Fighters page after a fight) |
| C8 | **Records** | `WatchResult` + `FightResult` rows (one per participant per match), unified history (list/get/delete-cascade), per-mode leaderboards (`ProfileStatsBuilder`, `FighterStatsBuilder`: W/L, badges, streaks, rivalries, form), profile record page with Radzen charts | Both | Unit (stats builders), Integration (cascade delete), E2EAPI (history/leaderboards), E2EUI (history after a match) |
| C9 | **Infra & deploy** | `infra/*.bicep` (RG, F1 plan, App Service + MI, storage keys-off + roles, KV policy), `deploy.yml` (build → Unit → publish → OIDC deploy → smoke), `SCRIPTS/setup.ps1`, `SCRIPTS/azurite.ps1`, `SCRIPTS/seed-secrets.ps1`, README/CLAUDE/AGENT docs | PoArgueJudge (Bicep/CI) + PoMarriedLife (Azurite script) | `az bicep build` lint, workflow dry-run, smoke test post-deploy (ask-first) |
| C10 | **PWA** | `manifest.webmanifest`, icons, minimal service worker (install prompt only, no offline caching of API) | New | E2EUI (manifest served, installable metadata present) |

## Dependency graph

```
C0 Foundation
├── C1 Profiles
├── C2 AI core
│   └── C3 Voice (TTS)
├── C9 Infra & deploy
└── C10 PWA

C4 WATCH engine   ← C1, C2, C3
C5 Live host      ← C2 (Fighter lives here)
C6 Analysis       ← C5, C2
C8 Records        ← C4, C6, C1, C5
C7 Fighter style  ← C6, C8
```

## Build order and checkpoints

| Order | Capability | Checkpoint (the app runs end-to-end at each) |
|---|---|---|
| 1 | C0 | Shell boots at https://localhost:5001, guest login, `/api/health` green against Azurite, dark/light toggle, all 5 test projects run (mostly empty) |
| 2 | C1 | Create/edit/seed profiles with faces; Profiles page complete |
| 3 | C2 → C3 | `/api/diag` shows AI providers; "Preview line" on a profile speaks (fake or real) |
| 4 | C4 | **WATCH plays end-to-end** with fakes and with a real key; verdict shown |
| 5 | C5 | **FIGHT runs end-to-end** with fake host (E2EUI fixture) and with a real key; spoken verdict |
| 6 | C6 | Verdict page shows the full analysis ≤ 45 s after a fight |
| 7 | C8 | History lists both modes; two leaderboards (profiles / fighters); delete cascades |
| 8 | C7 | After a fight, both Fighters show an argument-style profile; the host's next greeting quotes their record |
| 9 | C10 | Installable from the browser |
| 10 | C9 | Bicep validated; CI green; **deploy is ask-first** |

Cross-cutting from task 1: Radzen everywhere, design tokens, TDD, `TreatWarningsAsErrors`, no connection strings, no
secrets in source, one commit per task.
