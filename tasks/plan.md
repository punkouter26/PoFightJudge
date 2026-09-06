# PoMarriedFight — Implementation Plan (Phase 2)

On approval: this file is copied verbatim to `tasks/plan.md` and §7 to `tasks/todo.md` (the first commit).
Then, per your standing rules: **top-50 library list → your picks → top-10 usage examples → the picks are folded
into the affected tasks below** (todo.md updated) → `/design` (10 concepts) → you pick → component hierarchy →
build starts at T01.

## Context

SPEC.md (approved 2026-09-06, revised same day for separate `Profile`/`Fighter` kinds) merges two working apps into a
fresh solution: **PoMarriedLife** (AI husband-vs-wife argument simulator: profiles, Gemini rounds, TTS voice chain,
judge, `AdvancedStats`) and **PoArgueJudge** (two humans on one mic, Gemini Live host over SignalR, post-hoc
diarization + ~25 metrics + `gemini-3.7-flash` assessment). Both repos are cloned in the scratchpad
(`…/scratchpad/PoMarriedLife`, `…/scratchpad/PoArgueJudge`) and were read file-by-file; they agree on ~80 % of their
patterns (VSA, `ApiRoutes`/`ConfigKeys` single sources, FakeAuth + Guest, Key Vault secret manager, degraded mode,
Radzen + tokens, Testcontainers/Playwright tiers). The plan is therefore mostly a **deliberate port with two design
changes**: (1) one storage/records model spanning both modes, (2) auto-created `Fighter` profiles whose argument style
is derived from every analysed fight and fed to the host.

Nothing is written before you reply **approved**.

## 1. Architecture decisions

| # | Decision | Why / source |
|---|---|---|
| A1 | **Two engines, one host.** `Features/Watch` (rounds, judge, scorer) and `Features/Fight` + `Features/Live` + `Features/Analysis` (live host, pipeline) stay separate slices; they meet only in `Features/Records` (matches, results, history, leaderboards) and `Features/Fighters`. | Keeps each port faithful to its origin; the seams are the tables, not shared services. |
| A2 | **Two profile kinds.** `Profile` (WATCH persona, hand-authored, PoMarriedLife's aggregate verbatim minus stored W/L) and `Fighter` (tag-keyed, auto-created by `POST /api/fights`, only `DisplayName` editable). No FK between them in v1. | Your spec revision. |
| A3 | **Records are derived, never accumulated.** `WatchResults` (PK profile initials) and `FightResults` (PK fighter tag), RowKey = matchId. `ProfileStatsBuilder`/`FighterStatsBuilder`/`StyleProfileBuilder` compute everything on read. `FightResult` carries a `FightStyleSnapshot` so the style profile needs no report re-reads. Delete = remove rows; re-analysis = overwrite. | PoArgueJudge's `PlayerDebates` rule; makes SPEC §12 #10–11 testable. |
| A4 | **Unified `Matches`/`Turns`/`Analyses` tables** (PoArgueJudge's entities generalised with `Mode`, denormalised names, `IsFake`; `AnalysisEntity` keeps the 8×32 KB JSON chunking). WATCH rounds are `Turn`s of kind `Round` with mood + audio format; audio in blobs `audio/{matchId}/{index}.{mp3\|pcm}`. | One history query; one cascade delete. |
| A5 | **Storage is identity-only, everywhere.** `TableServiceClient`/`BlobServiceClient` bound to endpoint URIs + one shared `DefaultAzureCredential`; Azurite runs `--oauth basic` over HTTPS on 12000/12001/12002 with `AzuriteStorageScopedCredential` (Development only). Clients never throw at DI time (placeholder URI + degraded flag). Testcontainers.Azurite (shared key) is test-only. | PoMarriedLife's `DependencyInjection.cs`/`azurite.ps1`; SPEC §2. |
| A6 | **Fakes outside Production only.** `Features/Ai/Fakes/*` (`FakeGeminiText`, `FakeWatchAi`, `FakeTts` = tone PCM sized to text, `FakeTranscription`, `FakeLiveClient` = scripted show that reacts to mic frames, `FakeAnalysisClients`) register when `!IsProduction() && (no key \|\| Features:UseFakeAi)`; every fake reports `IsFake`; `/api/features.UseFakeAi` drives the banner; WATCH fake matches are exhibitions, FIGHT fake matches are persisted with `IsFake=true`. Test projects reuse the same fakes via `TestSupport` (thin wrappers + in-memory repos). | Your Phase 4 rule ("runs end-to-end with fakes"); PoArgueJudge's `ScriptedShow` is the model. |
| A7 | **Live path ported intact**: `ILiveSocket` + `LiveSocketConnector`/`LiveClientFactory` delegates, `GeminiLiveClient` (setup → `setupComplete` wait, pump, resumption handle), `DebateSession` pure state machine, `DebateOrchestrator` (RMS gate, silence gating, WAV tracks, caption transcript, tool calls, reconnect ≤3), `SessionRegistry` (1 s tick, one live session per user), `LiveHub` (MessagePack, `[Authorize]`), `HubClientSink`. Changes: `ShowSetup` gains `Fighter1/2Digest` strings; `HostPersona` gains `Referee` (default) with a marital-advice verdict tail; `set_players` still cannot rename. | PoArgueJudge `Features/Session`, `Features/Live`, `Hubs`. |
| A8 | **Analysis pipeline ported intact** (Files API → live-caption transcript preferred → `gemini-3.5-transcribe` fallback → `SpeakerMapper` → `SpeechMetrics` → two-call judge on flex → `HighlightFinder`), plus one new step: `FightStyleSnapshotExtractor` builds each fighter's snapshot from the report + turns and the pipeline writes two `FightResult` rows (replacing `SavePlayerRecordsAsync`). | PoArgueJudge `AnalysisPipeline`, `GeminiJudgeClient`, `AnalysisSchema`. |
| A9 | **WATCH engine ported intact**: `RoundPromptBuilder` (system/user split, husband-first stable ordering — `ArgumentPromptCacheOrderingTests` comes with it), `AttitudeSelector`, `Interjections` (SLAP), `JudgePromptBuilder`, `ArgueScoreCalculator`, client-driven round loop (`generate-round[-stream]`, `round-audio[-stream]` NDJSON, `verdict`, `transcribe`) behind the `ai-per-user` limiter, prefetch of round N+1, SELF turn. `IGeminiService` is split into `IGeminiText` (transport) + `IWatchAi` (prompts/parsing) so the fake sits at the WATCH seam. Model defaults: rounds `gemini-3.1-flash-lite`, judge/profile `gemini-3.7-flash`, TTS `gemini-3.1-flash-tts-preview`. | PoMarriedLife `GeminiService` (1066 lines → 3 files), `InteractiveGameEndpoints`, `Game.razor.cs`. |
| A10 | **Voice chain ported intact**: `RoutingTtsService` (profile Fish → Azure → default Fish → Gemini when `PreferFastVoice`), `TtsAudio(Base64, Format)`, `SentenceChunker` + `TtsChunk` stream, `BlobTtsCache` keyed on (text, provider, voice, prosody, format), per-operation resilience (`gemini-fast` 8 s/20 s, `gemini-tts` 45 s/100 s, Azure 10 s/22 s, Fish 25 s/55 s, unguarded `gemini-stream`). | PoMarriedLife `Features/Ai`. |
| A11 | **Auth exactly as both repos**: default scheme `FakeAuth` outside Production (needs `PoMarriedFight:Auth:AllowFakeAuth`), JWT bearer with dual `ValidAudiences` in Production; `GuestMiddleware` cookie; client `GuestAwareMsalAuthStateProvider` derives from MSAL's `RemoteAuthenticationService`; tenant-specific authority; hub token via `access_token` query; `FallbackPolicy = RequireAuthenticatedUser`. | PoMarriedLife `Client/Program.cs`, PoArgueJudge `AuthServiceExtensions`. |
| A12 | **UI system = PoMarriedLife's tokens + PoArgueJudge's guards.** `app.css` tokens (`--po-space/text/radius/shadow/dur`, `color-scheme` + `light-dark()`, `:root[data-theme]` pin, 3 breakpoints, `.po-card`, Radzen bridge), `PageShell` widths, `RadzenDataGrid`/`RadzenTabs`/`RadzenChart`/`RadzenNotification`/`RadzenDialog` everywhere; `PaletteContrastTests` parses the CSS; a Unit grep test forbids raw form controls. Dark default. The exact layout comes from `/design` (Phase 3) — the component *names* below are stable, their arrangement is not. | SPEC §5; NET_RULES §3. |
| A13 | **Tests**: 5 projects (`TestSupport`, `Unit`, `Integration`, `E2EAPI`, `E2EUI`). `ApiFactory` = `WebApplicationFactory<Program>` under env `Test` with in-memory repos + fakes; `AppFixture` = Kestrel on port 0 + `UseStaticWebAssets` + Chromium fake mic (`fixtures/debate-60s.wav` copied from PoArgueJudge); `AzuriteFixture` = compose-or-Testcontainers. Coverage via coverlet, target ≥ 80 % on Api + Shared excluding `Program.cs`, `*ServiceExtensions.cs`, `Fakes/`. | PoArgueJudge `tests/`. |
| A14 | **Infra = PoArgueJudge's Bicep renamed** (RG `PoMarriedFight`, `asp-pomarriedfight-f1` West US 2, `app-pomarriedfight`, `stpomarriedfight` East US 2 keys-off, roles, KV access-policy `add`, `appCommandLine: dotnet PoMarriedFight.Api.dll`) + `deploy.yml` (build → Unit → publish → OIDC → Bicep-if-changed → deploy → curl smoke → browser smoke). App settings carry `PoMarriedFight__TableStorageEndpoint/BlobStorageEndpoint`. Deploy, KV writes, Entra redirect URI, GitHub repo + OIDC app are **ask-first**. | SPEC §9. |
| A15 | **Git**: `git init` in T01, `master` only, local identity `punkouter26 <punkouter26@gmail.com>`, one commit per task (`T12: Profile aggregate + repository`), never push without asking. | PoArgueJudge CLAUDE.md rule; your global config is empty. |

## 2. Dependency graph

```
T01 skeleton ─ T02 shared ─ T03 host ─ T04 diag ─ T05 auth ─ T06 storage ─ T07 client boot ─ T08 tokens/shell ─ T09 client auth ─ T10 home/health ─ T11 test infra  [CP1]
T11 ─ T12 profile domain ─ T13 profile endpoints/seed ─ T14 profiles UI  [CP2]
T11 ─ T15 gemini http ─ T16 text client+fakes ─ T17 profile generate
T16 ─ T18 tts core ─ T19 providers+routing ─ T20 cache+preview ─ T21 transcription  [CP3]
T14,T17,T20 ─ T22 watch domain ─ T23 prompts+IWatchAi ─ T24 match storage ─ T25 watch endpoints ─ T26 watch setup UI ─ T27 watch play UI ─ T28 self turn ─ T29 watch E2E  [CP4]
T24 ─ T30 fighter domain ─ T31 live client ─ T32 state machine+persona ─ T33 orchestrator ─ T34 registry+hub+endpoints ─ T35 browser audio ─ T36 fight setup UI ─ T37 fight live UI ─ T38 live UI 2 + E2E  [CP5]
T34 ─ T39 analysis clients ─ T40 metrics ─ T41 pipeline+snapshot ─ T42 analysis endpoints+fakes ─ T43 verdict UI 1 ─ T44 verdict UI 2 + E2E  [CP6]
T44 ─ T45 stats builders ─ T46 records endpoints ─ T47 records UI ─ T48 fighters UI + host digest  [CP7]
T11 ─ T49 PWA ─ T50 infra ─ T51 scripts/docs/secrets ─ T52 verify+simplify  [CP8]
(T-LIB* tasks from the library selection are inserted where their capability lands.)
```

## 3. Risks

| Risk | L | Mitigation |
|---|---|---|
| Live API / TTS preview ids or message shapes have drifted since the source repos ran (Aug 2026) | M | `GeminiLiveSmokeTests` + a TTS smoke run at **CP5/CP3** with the real key before more UI is built; parser stays tolerant; ids in `appsettings` |
| `gemini-3.1-flash-tts-preview` request shape differs from the 2.5 TTS the port assumes | M | Verify against [speech-generation docs](https://ai.google.dev/gemini-api/docs/speech-generation) in T18 before coding; fallback chain means a TTS failure never breaks a round |
| Fake Live host must react to mic frames realistically enough for the E2EUI fight to reach the verdict | M | Port `ScriptedShow` (counts frames, emits tool calls) as the runtime `FakeLiveClient`; the same fake drives Dev and tests |
| Two engines' JS (`audio.js` 668 lines in PoMarriedLife vs 231 in PoArgueJudge) collide on one bus | M | One `PoAudio` facade: playback scheduler (WATCH) + capture worklet & host bus (FIGHT) in separate files loaded per page; Unit `AudioInteropTests` for the bridge |
| Style-profile aggregation reads as noise with 1–2 fights | L | `StyleProfileBuilder` degrades gracefully ("first fight", "2 fights — early read"); digest is ≤ 160 chars |
| `WebApplicationFactory.StartServer()` for E2EUI flaked once on this machine | L | Fixture binds `http://127.0.0.1:0` and reads `IServerAddressesFeature` (the fix PoArgueJudge landed); tests `Skip` with a visible message if the host cannot start |
| Azurite OAuth+HTTPS needs the dev cert exported/trusted | L | `azurite.ps1` handles it; `setup.ps1` checks `dotnet dev-certs https --check --trust` |
| Suite grows past useful size (52 tasks × tests) | M | Fold theory rows into single asserting loops (PoArgueJudge budget style); target ≈ Unit ≤ 220, Integration ≤ 30, E2EAPI ≤ 45, E2EUI ≤ 10 |
| F1 CPU quota (60 min/day) exhausted by long fights during verification | L | Verify locally; Azure smoke is one short fight |
| Entra redirect / KV secret copy needs your consent mid-build | — | Batched into T51 as one reviewed script; Dev needs neither |

## 4. Checkpoints

| CP | After | Proves |
|---|---|---|
| CP1 | T11 | Shell boots on https://localhost:5001 (guest login, dark default, toggle persists, 390 px), `/api/health` green against Azurite, `/api/diag` redacted, all 5 test projects run |
| CP2 | T14 | Profiles CRUD + seed + faces in the UI; Integration round-trip |
| CP3 | T21 | `/api/diag` lists AI providers; "Preview line" speaks (fake); **real-key TTS + text smoke** |
| CP4 | T29 | **WATCH end-to-end** with fakes (E2EUI green) and with a real key (evidence log) |
| CP5 | T38 | **FIGHT end-to-end** with fake host (E2EUI green) and **real-key Live smoke**; both fighters auto-created |
| CP6 | T44 | Verdict page full analysis ≤ 45 s (fake instantly; real logged) |
| CP7 | T48 | History (both modes), two leaderboards, Fighters + style profile, host digest in the roster; cascade delete |
| CP8 | T52 | Full suite + coverage report, `/code-review`, `/security-review`, `/simplify`, success-criteria evidence, README |

## 5. Task rules

- **Manifest = blast radius.** A task edits only the files it lists. `.razor` + `.razor.css` + `.razor.cs` of one component count as **one** file. Each task lists ≤ 5 implementation files; its **test files** and **one** `Shared/Models/*.cs` file are additional. T01 (skeleton) is exempt.
- TDD: tests listed under each task are written first (RED), then the minimum code (GREEN), full suite, build, commit.
- Ported code keeps its origin's structure and comments unless the SPEC says otherwise; namespaces become `PoMarriedFight.*`.
- Verification command is given per task; "suite" = `dotnet build PoMarriedFight.slnx && dotnet test PoMarriedFight.slnx`.

## 6. Tasks

### Phase A — Foundation (C0)

**T01 — Solution skeleton** *(skeleton; manifest exempt)*
- Files: `PoMarriedFight.slnx`, `global.json` (10.0.400, latestFeature), `Directory.Build.props` (from PoArgueJudge), `Directory.Packages.props` (PoArgueJudge's set + MessagePack, Radzen 11.3.2, Serilog sinks, OpenTelemetry/Azure Monitor, Http.Resilience, MinVer), `tests/Directory.Build.props`, `.editorconfig`, `.gitignore`, `docker-compose.yml` (OAuth+HTTPS Azurite on 12000–12002), `SCRIPTS/azurite.ps1`, 8 `.csproj` files (Shared, Api, Client, TestSupport, Unit, Integration, E2EAPI, E2EUI) with references, empty `Program.cs` stubs that compile, `NET_RULES.md` (copied), `README.md` (stub), `tasks/plan.md`, `tasks/todo.md`, `git init` + local identity.
- Tests: none (a `SolutionTests.Every_project_builds` placeholder in Unit).
- Accept: `dotnet build -c Release` 0 warnings; `dotnet test` green (1 test); `git log` shows the first commit.
- Verify: `dotnet build PoMarriedFight.slnx -c Release && dotnet test PoMarriedFight.slnx`

**T02 — Shared contracts**
- Files: `Shared/ApiRoutes.cs`, `Shared/Configuration/ConfigKeys.cs`, `Shared/Identifiers/ProfileId.cs`, `Shared/Identifiers/MatchId.cs`, `Shared/SelfPlayer.cs`; Shared model: `Models/FightModels.cs` (`Initials`, `FighterId` helpers, enums `SessionPhase/SessionStatus/Speaker/TurnKind/MatchMode`).
- Tests: `Unit/Shared/IdentifierTests` (upper-casing, `IParsable`, JSON as bare string), `InitialsTests` (normalize/pair), `ApiRoutesTests` (every `Url` starts with `/`, no duplicates).
- Deps: T01. Verify: suite.

**T03 — Api host bootstrap + secrets + degraded mode**
- Files: `Api/Program.cs`, `Api/Common/SecretManager.cs` (`PoMarriedFight--` → `:`), `Api/Common/Outcome.cs`, `Api/Features/Diagnostics/StartupHealthState.cs`, `Api/Features/Diagnostics/StartupSecretValidator.cs`; `Api/appsettings.json` + `appsettings.Development.json` (no secrets; KV uri; Azurite endpoints; model defaults; Debate/Analysis sections).
- Tests: `SecretManagerTests`, `StartupSecretValidatorTests` (degraded only in Production; lists missing keys), `OutcomeTests`.
- Deps: T02. Verify: suite; `dotnet run` answers `/api/nope` → 404 JSON.

**T04 — Diagnostics endpoints**
- Files: `Api/Features/Diagnostics/HealthEndpoints.cs`, `DiagEndpoints.cs`, `FeatureFlagEndpoints.cs`, `Api/Common/SecretMasker.cs`, `Api/Common/SecurityHeadersMiddleware.cs`; Shared model: `Models/HealthModels.cs` (`HealthDto`, `HealthReportDto`, `DiagDto`, `FeatureFlagsDto`).
- Tests: `SecretMaskerTests`, E2EAPI `HealthTests` (200; `/api/diag` 401 anon; no key-like values; CSP header present).
- Deps: T03. **CP0.5** (host answers).

**T05 — Auth (server)**
- Files: `Api/Features/Auth/FakeAuthHandler.cs`, `FakeAuthOptions.cs`, `GuestMiddleware.cs`, `ClaimsPrincipalExtensions.cs`, `AuthEndpoints.cs` (+ `AuthServiceExtensions` folded into `AuthEndpoints.cs` as `AddPoAuth`); Shared model: `Models/AuthModels.cs` (`AuthMeDto`).
- Tests: `FakeAuthHandlerTests` (throws in Production; needs AllowFakeAuth), `GuestMiddlewareTests`, E2EAPI `AuthTests` (protected route 401; `X-Fake-User` 200; `/auth/me`).
- Deps: T03.

**T06 — Storage clients + Azurite credential**
- Files: `Api/Common/DependencyInjection.cs` (`AddPoStorage`: identity-only clients, placeholder URIs), `Api/Common/AzuriteStorageScopedCredential.cs`, `Api/Common/StorageBootstrap.cs` (ensure tables/containers, never throws), `Api/Features/Storage/AudioBlobStore.cs` (+`IAudioBlobStore`, blob-name helpers), `Api/Features/Diagnostics/StorageHealthCheck.cs` (data-plane list, not properties).
- Tests: `AzuriteScopedCredentialTests`, Integration `AudioBlobStoreTests` (upload/read/delete-prefix) with `AzuriteFixture`.
- Deps: T04.

**T07 — Client bootstrap**
- Files: `Client/Program.cs` (Radzen, env-split auth providers, `FallbackPolicy`), `Client/App.razor`, `Client/_Imports.razor`, `Client/wwwroot/index.html` (theme.js first, Radzen css, MSAL `AuthenticationService.js`), `Client/wwwroot/appsettings.json` (AzureAd placeholders).
- Tests: `Unit/Client/ClientBootTests` (bunit: `App` renders `RadzenComponents`).
- Deps: T02.

**T08 — Design tokens, theme, shell**
- Files: `Client/wwwroot/css/app.css` (PoMarriedLife tokens + PoArgueJudge Radzen bridge; dark default), `Client/wwwroot/js/theme.js`, `Client/Layout/MainLayout` (header `[brand | actions | session]`, nav, `RadzenNotification`, `RadzenDialog`), `Client/Components/PageShell`, `Client/Components/ThemeToggle` (+ `Services/ThemeInterop.cs` counted with it).
- Tests: `PaletteContrastTests` (parses `app.css`, AA in both themes), `NoRawFormControlsTests` (grep `.razor` for `<button|<input|<select|<textarea` except `InputFile`), `NoPrefersColorSchemeTests`.
- Deps: T07.

**T09 — Client auth**
- Files: `Client/Services/ApiClient.cs` (typed HTTP, guest header, error → `Outcome`), `Client/Services/ApiAuthStateProvider.cs` (`GuestFlag…` + `GuestAwareMsal…`), `Client/Pages/Login`, `Client/Pages/Authentication`, `Client/Shared/RedirectToLogin.razor`.
- Tests: bunit `LoginPageTests` (guest button only outside Production), E2EUI `ThemeAndViewportTests` (unauth → `/login`; toggle persists; 390 px no horizontal scroll) — fixture comes in T11, test file lands here and skips until then.
- Deps: T08, T05.

**T10 — Home, Health page, NotFound, banner, empty state**
- Files: `Client/Pages/Home` (two mode cards + recent matches placeholder), `Client/Pages/Health` (renders `HealthReportDto` via `RadzenDataGrid`), `Client/Pages/NotFound`, `Client/Components/FakeAiBanner` (reads `/api/features`), `Client/Components/EmptyState`.
- Tests: bunit `HomePageTests`, `FakeAiBannerTests`.
- Deps: T09.

**T11 — Test infrastructure**
- Files (tests only): `TestSupport/{FakeHttpHandler.cs, InMemoryRepositories.cs (stubs grow later), TestSupport.csproj}`, `E2EAPI/ApiFactory.cs`, `Integration/Support/AzuriteFixture.cs`, `E2EUI/AppFixture.cs` (+ `fixtures/debate-60s.wav` copied), `Unit/Support/FakeTime.cs`.
- Accept: `HealthTests`, `AuthTests`, `AudioBlobStoreTests`, `ThemeAndViewportTests` all run (not skipped) with Docker + Chromium present.
- Deps: T10. **→ CP1**

### Phase B — Profiles (C1)

**T12 — Profile domain + repository**
- Files: `Api/Features/Profiles/Profile.cs` (PoMarriedLife aggregate; W/L removed), `ProfileTableEntity.cs`, `ProfileRepository.cs` (+`IProfileRepository`), `ProfileMapping.cs` (DTO ↔ domain, `TtsSettings.Normalize`); Shared model: `Models/ProfileModels.cs` (`ProfileDto`, `TtsSettingsDto`, `CreateProfileRequest`).
- Tests: `ProfileTests` (create/normalize/self), Integration `ProfileRepositoryTests` (CRUD round-trip).
- Deps: T11.

**T13 — Profile endpoints, faces, seeding**
- Files: `Api/Features/Profiles/ProfileEndpoints.cs` (list/get/create/update/delete/face), `ProfileImageService.cs` (resize/validate → `faces/` blob), `Api/Features/Profiles/Seeding/SeedEndpoints.cs`, `Seeding/SeedProfiles.cs` (the six defaults), `Api/Common/SeedAdminGate.cs`.
- Tests: `ProfileImageServiceTests`, `SeedAdminGateTests`, E2EAPI `ProfilesTests` (CRUD, 400s, face upload, seed).
- Deps: T12.

**T14 — Profiles UI**
- Files: `Client/Pages/Profiles` (`RadzenDataList`/cards, role filter, seed button), `Client/Components/ProfileCard`, `Client/Components/ProfileEditDialog` (`RadzenTemplateForm`, sliders, dropdowns, `InputFile` face), `Client/Components/LoadingRows`, `Client/Components/StatBar`; assets `wwwroot/images/profiles/*.png` (copied).
- Tests: bunit `ProfileEditDialogTests` (validation), E2EUI `ProfileFlowTests` (create → appears in list).
- Deps: T13. **→ CP2**

### Phase C — AI core + Voice (C2, C3)

**T15 — Gemini HTTP core**
- Files: `Api/Features/Ai/GeminiHttp.cs` (client names, `EnsureSuccessAsync`, truncate), `GeminiRetryHandler.cs` (jittered backoff, `Retry-After`, skip resumable-upload legs), `GeminiResilience.cs` (named clients `gemini-fast`/`gemini-tts`/`gemini-stream`/`gemini-analysis` with the A10 budgets), `GeminiModelOptions.cs` (SPEC §2 defaults, config-driven), `AiServiceExtensions.cs` (`AddPoAi`: real vs fake decision, `UseFakeAi` flag, logs the choice).
- Tests: `GeminiRetryHandlerTests`, `GeminiModelOptionsTests`, `AiRegistrationTests` (Production + no key → no fakes registered, degraded).
- Deps: T11.

**T16 — Gemini text client + fakes + latency tracker**
- Files: `Api/Features/Ai/IGeminiText.cs` (`GenerateJsonAsync`, `StreamTextAsync`, `GeminiPrompt(System, User)`), `GeminiTextClient.cs` (generateContent + SSE), `Fakes/FakeGeminiText.cs`, `Api/Features/Diagnostics/AiLatencyTracker.cs` (p50/p95, ttft, token ledger → `/api/diag`), `DiagEndpoints.cs` (render the two tables).
- Tests: `GeminiTextClientTests` (payload shape: `systemInstruction` split, `responseMimeType`), `SseParserTests`, `AiLatencyTrackerTests`.
- Deps: T15.

**T17 — Profile generation**
- Files: `Api/Features/Profiles/ProfileGenerator.cs` (schema’d `gemini-3.7-flash` call, fake returns a canned persona), `ProfileEndpoints.cs` (+`/generate?role=`), `Client/Components/ProfileEditDialog` (Generate button, `RadzenProgressBarCircular`).
- Tests: `ProfileGeneratorTests` (prompt includes role; parses/clamps sliders), E2EAPI `ProfilesTests` (+generate).
- Deps: T16, T13. **→ CP2.5**

**T18 — TTS core + Gemini TTS + fake**
- Files: `Api/Features/Voice/ITtsService.cs` (+`TtsChunk`), `TtsAudio.cs` (+`TtsAudioFormats`), `SentenceChunker.cs`, `GeminiTtsService.cs` (`gemini-3.1-flash-tts-preview`, `responseModalities:["AUDIO"]`; verified against the docs first), `Fakes/FakeTts.cs` (440 Hz PCM sized ~60 ms/word).
- Tests: `SentenceChunkerTests`, `TtsAudioFormatsTests`, `GeminiTtsServiceTests` (request shape, PCM passthrough), `FakeTtsTests` (duration ∝ words).
- Deps: T16.

**T19 — Voice providers + routing**
- Files: `Api/Features/Voice/FishAudioService.cs` (+`FishAudioOptions`, resilience), `AzureSpeechService.cs` (+`AzureSpeechOptions`, mp3 48 kbit), `RoutingTtsService.cs` (fast-first chain, stream), `VoiceServiceExtensions.cs` (`AddPoVoice`), `Api/Features/Voice/TtsRoutingOptions.cs`.
- Tests: `RoutingTtsServiceTests` + `RoutingTtsServiceFallbackTests` (full matrix from PoMarriedLife), `FishAudioServiceTests`, `AzureSpeechServiceTests`.
- Deps: T18.

**T20 — TTS cache + preview line + playback interop**
- Files: `Api/Features/Voice/TtsCache.cs` (`ITtsCache`, `TtsCacheKey`, `BlobTtsCache`, `NullTtsCache`), `ProfileEndpoints.cs` (+`/preview-line`), `Client/Services/AudioInterop.cs` (decode mp3/pcm, schedule on a rolling clock, NDJSON chunk playback), `Client/wwwroot/js/audio.js` (`PoAudio.play*`), `Client/Components/ProfileCard` (Preview button).
- Tests: `TtsCacheKeyTests`, Integration `BlobTtsCacheTests`, `AudioInteropTests` (bridge args), E2EAPI `ProfilesTests` (+preview returns `TtsAudio`).
- Deps: T19.

**T21 — Transcription**
- Files: `Api/Features/Voice/ITranscriptionService.cs`, `AzureTranscriptionService.cs` (fast transcription), `GeminiTranscriptionService.cs`, `Fakes/FakeTranscription.cs`, `Api/Features/Diagnostics/FeatureFlagEndpoints.cs` (+`SelfPlayerEnabled`, `BrowserSpeechRecognition`).
- Tests: `TranscriptionTests` (provider selection; WAV validation), E2EAPI `FeaturesTests`.
- Deps: T19. **→ CP3** (real-key TTS/text smoke: `[SkippableFact] GeminiSmokeTests`)

### Phase D — WATCH (C4)

**T22 — WATCH domain**
- Files: `Api/Features/Watch/WatchMatch.cs` (+`WatchRound`, `MaxRoundsPerGame=3`, `MaxLinesPerGame=7`), `AdvancedStats.cs`, `ArgueScoreCalculator.cs`, `AttitudeSelector.cs`, `Api/Features/Watch/WatchRules.cs` (human reply rules from `HumanReplyRulesTests`); Shared model: `Models/WatchModels.cs` (`Interjections`, `WatchRoundDto`, `GenerateRoundRequest/Response`, `VerdictRequest/Response`, `AdvancedStatsDto`, `TranscribeRequest/Response`).
- Tests: `GameDomainTests`, `ArgueScoreCalculatorTests`, `AttitudeSelectorTests`, `HumanReplyRulesTests` (all ported).
- Deps: T14, T17, T20.

**T23 — Prompts + `IWatchAi`**
- Files: `Api/Features/Watch/RoundPromptBuilder.cs`, `JudgePromptBuilder.cs`, `IWatchAi.cs` (`GenerateArgument`, `StreamArgument`, `Judge`; records `ArgumentResult`, `JudgeVerdictResult`, `GameRoundContext`), `WatchAi.cs` (over `IGeminiText`, JSON parsing/repair), `Fakes/FakeWatchAi.cs` (deterministic lines by round/attitude; judge by word count).
- Tests: `ArgumentPromptCacheOrderingTests` (ported), `JudgePromptTests`, `HumanOpponentPromptTests`, `WatchAiParsingTests`, `FakeWatchAiTests`.
- Deps: T22.

**T24 — Match storage (both modes)**
- Files: `Api/Features/Storage/StorageEntities.cs` (`MatchEntity`, `TurnEntity`, `AnalysisEntity` chunked, `WatchResultEntity`, `FightResultEntity`, `FighterEntity`), `Api/Features/Records/MatchRepository.cs` (+`IMatchRepository`: upsert/get/list/delete-cascade, turns, analysis), `Api/Features/Records/WatchResultRepository.cs`, `Api/Features/Watch/WatchAudioBlobService.cs` (round audio `{matchId}/{index}.{ext}`), `TestSupport/InMemoryRepositories.cs` (grow); Shared model: `Models/RecordModels.cs` (`MatchDto`, `TurnDto`, `AnalysisRecordDto`, `WatchResultDto`, `FightResultDto` + `FightStyleSnapshot`).
- Tests: `StorageEntityTests` (round-trips, chunking), Integration `MatchRepositoryTests` (+cascade), `WatchResultRepositoryTests`.
- Deps: T23.

**T25 — WATCH endpoints**
- Files: `Api/Features/Watch/WatchEndpoints.cs` (generate-round, stream, round-audio, stream NDJSON, verdict → persist match + turns + `WatchResults` + audio, transcribe; SELF/fake exhibition rules; ownership checks), `Api/Features/Watch/WatchServiceExtensions.cs` (rate limiter `ai-per-user`, `AddPoWatch`), `Api/Program.cs` (map + `UseRateLimiter`), `Api/Features/Records/AudioProxyEndpoints.cs` (`GET /api/audio/{matchId}/{index}`).
- Tests: E2EAPI `WatchTests` (round → 6 lines → verdict persists; 7-line slap accepted; 8 → 400; SELF not persisted; 429 after 20/min; foreign match 404).
- Deps: T24.

**T26 — WATCH setup UI**
- Files: `Client/Pages/Watch` (pick husband/wife/SELF, topic, random, Start), `Client/Components/ProfilePicker`, `Client/Components/SpeakerCard`, `Client/Services/ApiClient.cs` (watch calls), `Client/Services/SimulationState.cs` (chosen matchup carried to play page).
- Tests: bunit `WatchSetupTests` (same profile twice blocked; SELF hidden when disabled).
- Deps: T25.

**T27 — WATCH play UI**
- Files: `Client/Pages/WatchPlay` (`.razor` + `.razor.cs`), `Client/Pages/WatchPlay.Prefetch.cs`, `Client/Components/InterjectionBar` (SLAP, once), `Client/Components/TensionMeter` (+`TensionCalculator`), `Client/Services/FxInterop.cs` + `wwwroot/js/fx.js` (counted as one).
- Tests: `TensionCalculatorTests`, bunit `WatchPlayTests` (round rendering, verdict panel), E2EUI `WatchFlowTests` (fake AI: start → 6 lines → verdict → history) — lands here, may need T28 for SELF branch.
- Deps: T26.

**T28 — SELF turn**
- Files: `Client/Pages/WatchPlay.SelfTurn.cs`, `Client/wwwroot/js/mic.js`, `Client/wwwroot/js/speech.js`, `Client/Services/MicInterop.cs`, `Client/Services/SpeechRecognitionInterop.cs`.
- Tests: `SelfPlayerTests` (ported), `SpeechRecognitionInteropTests`, E2EUI `WatchFlowTests` (+SELF branch with fake mic).
- Deps: T27.

**T29 — WATCH end-to-end hardening**
- Files: only fixes surfaced by the E2E runs (each listed in the commit); `docs/screenshots/watch-*.png`.
- Accept: `WatchFlowTests` green; real-key run log with ttft/first-audio ≤ 5 s / ≤ 8 s.
- Deps: T28. **→ CP4**

### Phase E — FIGHT live (C5)

**T30 — Fighter domain + repositories**
- Files: `Api/Features/Fighters/Fighter.cs` (tag id, display name, created), `FighterRepository.cs` (+`IFighterRepository`: get/list/ensure/rename/delete), `Api/Features/Records/FightResultRepository.cs`, `Api/Features/Storage/StorageEntities.cs` (fighter/fight-result entities if not in T24), `TestSupport/InMemoryRepositories.cs`; Shared model: `Models/FightModels.cs` (+`FighterDto`, `CreateFightRequest`, `CreateFightResponse`).
- Tests: `FighterTests`, Integration `FighterRepositoryTests` (ensure is idempotent), `FightResultRepositoryTests` (replace on same RowKey).
- Deps: T24.

**T31 — Gemini Live client**
- Files: `Api/Features/Live/ILiveSocket.cs` (+`LiveSocketConnector`, `ClientWebSocketLiveSocket`), `LiveMessages.cs` (client messages, `LiveServerEvent` hierarchy, tolerant `LiveMessageParser`), `GeminiLiveClient.cs` (+`IGeminiLiveClient`, `LiveClientFactory`), `LiveSetupBuilder.cs` (+`LiveSessionConfig`), `GeminiOptions.cs`.
- Tests: `LiveMessageParserTests`, `LiveSetupBuilderTests`, `GeminiLiveClientTests` (setup wait/timeout, resumption handle, close), `GeminiLiveSmokeTests` (`[SkippableFact]`, real key).
- Deps: T15.

**T32 — Debate state machine + host personas**
- Files: `Api/Features/Fight/DebateSession.cs` (+`PlayerId`, `VerdictCall`, `DebateNudge`, `NudgeKind`), `DebateOptions.cs`, `ShowSetup.cs` (+`Fighter1Digest`, `Fighter2Digest`), `HostPersona.cs` (Referee default + four; roster with digests; verdict tail = marital advice), `Api/Features/Live/ToolDeclarations.cs`; Shared model: `Models/HostPersonaModels.cs` (`HostPersonaId` with `Referee`, catalogue, `DebateSnapshotDto`, `VerdictDto`).
- Tests: `DebateSessionTests` (ported: phases, nudges once each, probe alternation, caps), `HostPersonaTests` (Referee is default; digests in roster; advice instruction; “first fight” wording), `ToolDeclarationsTests`.
- Deps: T30, T31.

**T33 — Orchestrator**
- Files: `Api/Features/Fight/DebateOrchestrator.cs`, `CaptionTranscript.cs`, `WavWriter.cs`, `ILiveClientSink.cs`, `Api/Features/Ai/Fakes/FakeLiveClient.cs` (runtime scripted host: reacts to frame counts, emits tool calls with the seeded tags, silence PCM as “voice”).
- Tests: `DebateOrchestratorTests` (ported: nudge/interrupt, silence gating, persist on end, reconnect ≤3, no-client grace), `WavWriterTests`, `CaptionTranscriptTests`, `FakeLiveClientTests` (reaches verdict on N frames).
- Deps: T32.

**T34 — Registry, hub, fight endpoints**
- Files: `Api/Features/Fight/SessionRegistry.cs` (+`HubClientSink`), `Api/Hubs/LiveHub.cs`, `Api/Features/Fight/FightEndpoints.cs` (`POST /api/fights` validates tags, **ensures both Fighters**, builds `ShowSetup` with digests (digest builder arrives in T45 — until then “first fight”), get snapshot/stored, end), `FightServiceExtensions.cs` (`AddPoFight`: options, connector, factory, SignalR MessagePack), `Api/Program.cs` (map hub + endpoints); Shared model: `Models/LiveModels.cs` (`LiveHubContract`, `CaptionDto`).
- Tests: E2EAPI `FightTests` (create → two `Fighters` rows; same tag twice 400; unknown persona 400; get; end; foreign 404; hub join + AudioIn with fake), `SessionRegistryTests` (one live per user; reaper).
- Deps: T33.

**T35 — Browser audio (capture + host bus)**
- Files: `Client/wwwroot/js/pcm-worklet.js` (16 kHz Int16, 100 ms frames), `Client/wwwroot/js/audio.js` (+capture, host 24 kHz bus, level meters, half-duplex duck), `Client/Services/AudioInterop.cs` (+`StartCaptureAsync`, `AudioCaptureBridge`), `Client/Services/IAudioInterop.cs` (+`IAudioFrameSink`), `Client/Services/LiveConnection.cs` (+`ForeverRetryPolicy`, `LiveConnectionFactory`).
- Tests: `AudioInteropTests`, `ReconnectPolicyTests`.
- Deps: T34.

**T36 — Fight setup UI**
- Files: `Client/Pages/Fight` (tags with `Immediate` binding, topic, host picker, Start), `Client/Components/FighterTagInput` (`RadzenAutoComplete` over `/api/fighters`), `Client/Components/FighterCard` (record badge or “new fighter”), `Client/Components/HostPersonaPicker` (`RadzenRadioButtonList`/cards with arrow keys), `Client/Services/ApiClient.cs` (fight + fighters calls).
- Tests: bunit `FightSetupTests` (Start disabled until two distinct tags; Referee preselected).
- Deps: T35.

**T37 — Fight live UI**
- Files: `Client/Pages/FightLive` (stage + control bar, joins hub, starts mic on click, hands over to `/verdict/{id}` after `Ended`), `Client/Components/PhaseBanner`, `Client/Components/DebateTimer`, `Client/Components/CaptionFeed` (`role=log`), `Client/Components/HostIndicator`.
- Tests: bunit `FightLiveComponentTests` (phase/timer/caption rendering from snapshots).
- Deps: T36.

**T38 — Live UI 2 + FIGHT E2E**
- Files: `Client/Components/MicMeter`, `Client/Components/LevelBars`, `Client/Components/ScoreboardBanner` (tags + records + who has the floor), `docs/screenshots/fight-*.png`.
- Tests: E2EUI `FightFlowTests` (fake host + fake mic: setup → captions → phases → verdict URL), E2EAPI `FightTests` (+snapshot phases), `FightHudTests` (bunit).
- Accept: `FightFlowTests` green; **real-key Live smoke** log (greets by tag ≤ 15 s). Deps: T37. **→ CP5**

### Phase F — Analysis (C6)

**T39 — Analysis clients + schema**
- Files: `Api/Features/Analysis/GeminiFilesClient.cs` (+interface, resumable upload, `GeminiFile`), `GeminiTranscribeClient.cs` (+interface, Interactions API), `GeminiJudgeClient.cs` (+interface, `JudgeRequest`, two calls, flex, `Parse`), `AnalysisSchema.cs`, `JsonRepair.cs`; Shared model: `Models/AnalysisModels.cs` (all PoArgueJudge analysis DTOs + `AnalysisResponse`).
- Tests: `GeminiFilesClientTests`, `GeminiTranscribeClientTests`, `GeminiJudgeClientTests` (request shape, flex/thinking, two calls), `AnalysisSchemaTests` (reflection vs DTOs), `JsonRepairTests`.
- Deps: T34.

**T40 — Metrics, mapping, highlights**
- Files: `Api/Features/Analysis/SpeechMetrics.cs`, `Lexicon.cs`, `Readability.cs`, `SpeakerMapper.cs`, `HighlightFinder.cs`; Shared model: `Models/MetricsModels.cs` (`PlayerMetricsDto`, `TranscriptDto`, `TranscriptWord`, `MappedWord`, `MappedTranscript`, `HighlightDto`).
- Tests: `SpeechMetricsTests`, `SpeakerMapperTests` (label-agnostic, merges extras, flags), `HighlightFinderTests`, `ReadabilityTests`.
- Deps: T39.

**T41 — Pipeline + style snapshot**
- Files: `Api/Features/Analysis/AnalysisPipeline.cs` (+`IAnalysisQueue`; writes two `FightResult` rows incl. snapshots; marks match Ready/Failed), `AnalysisOptions.cs`, `PollBackoff.cs`, `WavSlicer.cs`, `Api/Features/Fighters/FightStyleSnapshotExtractor.cs` (tone, ≤3 phrases, fallacies, opener = first 12 words of first Talk turn, CEFR, emotions, best quote ≤120, ≤3 tips).
- Tests: `AnalysisPipelineTests` (ported: transcript source preference, too-short gate, failure marks Failed, **two FightResult rows with snapshots**, deleted match → no resurrected row), `FightStyleSnapshotExtractorTests`, `WavSlicerTests`, `PollBackoffTests`.
- Deps: T40.

**T42 — Analysis endpoints, fakes, DI**
- Files: `Api/Features/Analysis/AnalysisEndpoints.cs` (202/200, clips, transcript post, retry 409), `ClientTranscript.cs`, `Api/Features/Ai/Fakes/FakeAnalysisClients.cs` (files/transcribe/judge with the sample output), `AnalysisServiceExtensions.cs` (`AddPoAnalysis`, hosted service), `Api/Program.cs` (map).
- Tests: `ClientTranscriptTests`, E2EAPI `AnalysisTests` (202 → 200; retry rules; clip 404 bounds; fighters gain results).
- Deps: T41. **→ CP6-pre**

**T43 — Verdict UI 1**
- Files: `Client/Pages/Verdict` (tabs: Ruling / Evidence / How they talked / Full analysis; polls 202→200; retry), `Client/Components/PlayerCard`, `Client/Components/StatTile`, `Client/Components/StatCell` (+enums), `Client/Components/TraitScores`.
- Tests: `VerdictComponentTests` (every `PlayerMetricsDto` + `PlayerAssessmentDto` field rendered — reflection), `StatFormatTests`.
- Deps: T42.

**T44 — Verdict UI 2 + E2E**
- Files: `Client/Components/ClaimsTable` (`RadzenDataGrid`), `Client/Components/EmotionChart` (`RadzenChart` area + SVG fallback), `Client/Components/HighlightReel` (clips via data URL), `Client/Components/StatsPanel`, `Client/Stats/StatFormat.cs` (+`PlayerStatsViewModel`).
- Tests: bunit `EmotionChartTests` (fallback < 2 points), E2EUI `FightFlowTests` (+verdict tabs, claims rows, 8 trait rows), `docs/screenshots/verdict.png`.
- Deps: T43. **→ CP6**

### Phase G — Records + Fighter style (C8, C7)

**T45 — Stats + style builders**
- Files: `Api/Features/Records/ProfileStatsBuilder.cs` (Watch W/L, `AdvancedStats` means, rivalries), `Api/Features/Fighters/FighterStatsBuilder.cs` (PoArgueJudge `PlayerStatsBuilder`: records, badges, streak, consistency, rivalries, form), `Api/Features/Fighters/StyleProfileBuilder.cs` (aggregation + host digest ≤160 chars + empty/“early read” states), `Api/Features/Fight/FightEndpoints.cs` (digest wired into `ShowSetup`), `Api/Features/Fighters/FighterStyle.cs` (options: phrase threshold, top-N); Shared model: `Models/RecordModels.cs` (+`ProfileRecordDto`, `FighterStatsDto`, `FighterProfileDto`, `StyleProfileDto`, `Badges`, leaderboard rows).
- Tests: `ProfileStatsBuilderTests`, `FighterStatsBuilderTests` (ported), `StyleProfileBuilderTests` (frequency ranking, phrase ≥2 fights, opener repeat count, CEFR trend, digest text, 0/1/2 fights), `HostPersonaTests` (+digest appears).
- Deps: T44.

**T46 — Records endpoints**
- Files: `Api/Features/Records/HistoryEndpoints.cs` (list/get/delete: rows + blobs + results), `LeaderboardEndpoints.cs` (`/watch`, `/fight`, global), `Api/Features/Fighters/FighterEndpoints.cs` (list/get/rename/delete), `Api/Features/Profiles/ProfileEndpoints.cs` (+`/record`), `Api/Features/Records/MatchRepository.cs` (cascade incl. `WatchResults`/`FightResults`).
- Tests: E2EAPI `HistoryTests` (per-user; both modes; cascade), `LeaderboardTests`, `FightersTests` (rename only display name; delete removes results), Integration `CascadeDeleteTests`.
- Deps: T45.

**T47 — Records UI**
- Files: `Client/Pages/History` (`RadzenDataGrid`, mode filter, open/delete with confirm dialog), `Client/Pages/Leaderboard` (`RadzenTabs`: AI profiles / Fighters), `Client/Components/ModeBadge`, `Client/Components/FormSparklines` (`RadzenChart` line), `Client/Pages/ProfileRecord` (Watch record + stat means).
- Tests: bunit `HistoryPageTests`, E2EUI `HistoryTests` (a Watch and a Fight row; delete detaches).
- Deps: T46.

**T48 — Fighters UI + host digest end-to-end**
- Files: `Client/Pages/Fighters` (grid: tag, name, fights, W/L, last seen), `Client/Pages/FighterProfile` (record, badges, rivalries, form chart, style profile, rename, delete), `Client/Components/StyleProfilePanel`, `Client/Pages/Home` (recent matches + top fighters), `Client/Pages/FightLive` (records on the scoreboard).
- Tests: bunit `StyleProfilePanelTests` (empty vs populated), E2EUI `FightFlowTests` (+Fighters page lists both tags with 1 fight; second fight → digest “2 fights” asserted via `/api/fighters/{tag}`).
- Deps: T47. **→ CP7**

### Phase H — PWA, infra, docs, verification (C10, C9)

**T49 — PWA (install-only)**
- Files: `Client/wwwroot/manifest.webmanifest`, `Client/wwwroot/sw.js` (install/activate only; no fetch caching of `/api`), `Client/wwwroot/icons/*` (assets), `Client/wwwroot/index.html` (manifest link, theme-color, sw registration guarded).
- Tests: E2EUI `PwaTests` (manifest 200, `display: standalone`, icons ≥192/512, sw registered).
- Deps: T11.

**T50 — Infra as code + CI**
- Files: `infra/main.bicep`, `infra/resources.bicep`, `infra/storage-role.bicep`, `infra/keyvault-access.bicep`, `.github/workflows/deploy.yml`.
- Tests: `az bicep build` in a Unit `[SkippableFact]` when `az` exists; workflow YAML parsed by a test.
- Accept: bicep compiles; workflow validated. **No deploy** without your go. Deps: T49.

**T51 — Scripts, docs, secret seeding (ask-first execution)**
- Files: `SCRIPTS/setup.ps1`, `SCRIPTS/seed-secrets.ps1` (copies `PoMarriedLife--*` → `PoMarriedFight--*`, adds the Entra redirect URI; prints the plan and asks `-WhatIf` first), `README.md`, `CLAUDE.md`, `AGENT.md` (identity, env matrix, flags, deviations table).
- Accept: `setup.ps1` runs clean on this machine; you approve and I run `seed-secrets.ps1`; `/api/diag` then shows real providers. Deps: T50.

**T52 — Verify, review, simplify, evidence**
- Files: fixes only (each listed in its commit), `docs/VERIFICATION.md` (SPEC §12 table with evidence), `docs/screenshots/*`.
- Steps: full suite + coverage; `/code-review`; `/security-review`; fix Critical/Important; `/simplify`; re-run; walk all 18 criteria with command output/screenshots; recap. **→ CP8**

## 7. tasks/todo.md (checklist)

```
# PoMarriedFight — TODO (one commit per task; tick when merged to master)

## Pre-build
- [ ] Library selection (top-50 → picks → top-10 examples → tasks updated)
- [ ] /design: 10 concepts → pick → component hierarchy confirmed

## Phase A — Foundation
- [ ] T01 Solution skeleton
- [ ] T02 Shared contracts
- [ ] T03 Api host bootstrap + secrets + degraded mode
- [ ] T04 Diagnostics endpoints
- [ ] T05 Auth (server)
- [ ] T06 Storage clients + Azurite credential
- [ ] T07 Client bootstrap
- [ ] T08 Design tokens, theme, shell
- [ ] T09 Client auth
- [ ] T10 Home, Health, NotFound, banner, empty state
- [ ] T11 Test infrastructure  ← CP1

## Phase B — Profiles
- [ ] T12 Profile domain + repository
- [ ] T13 Profile endpoints, faces, seeding
- [ ] T14 Profiles UI  ← CP2

## Phase C — AI core + Voice
- [ ] T15 Gemini HTTP core
- [ ] T16 Gemini text client + fakes + latency tracker
- [ ] T17 Profile generation
- [ ] T18 TTS core + Gemini TTS + fake
- [ ] T19 Voice providers + routing
- [ ] T20 TTS cache + preview line + playback interop
- [ ] T21 Transcription  ← CP3 (real-key smoke)

## Phase D — WATCH
- [ ] T22 WATCH domain
- [ ] T23 Prompts + IWatchAi
- [ ] T24 Match storage (both modes)
- [ ] T25 WATCH endpoints
- [ ] T26 WATCH setup UI
- [ ] T27 WATCH play UI
- [ ] T28 SELF turn
- [ ] T29 WATCH end-to-end hardening  ← CP4

## Phase E — FIGHT live
- [ ] T30 Fighter domain + repositories
- [ ] T31 Gemini Live client
- [ ] T32 Debate state machine + host personas
- [ ] T33 Orchestrator
- [ ] T34 Registry, hub, fight endpoints
- [ ] T35 Browser audio
- [ ] T36 Fight setup UI
- [ ] T37 Fight live UI
- [ ] T38 Live UI 2 + FIGHT E2E  ← CP5 (real-key Live smoke)

## Phase F — Analysis
- [ ] T39 Analysis clients + schema
- [ ] T40 Metrics, mapping, highlights
- [ ] T41 Pipeline + style snapshot
- [ ] T42 Analysis endpoints, fakes, DI
- [ ] T43 Verdict UI 1
- [ ] T44 Verdict UI 2 + E2E  ← CP6

## Phase G — Records + Fighter style
- [ ] T45 Stats + style builders
- [ ] T46 Records endpoints
- [ ] T47 Records UI
- [ ] T48 Fighters UI + host digest  ← CP7

## Phase H — PWA, infra, docs, verification
- [ ] T49 PWA
- [ ] T50 Infra as code + CI
- [ ] T51 Scripts, docs, secret seeding (ask-first)
- [ ] T52 Verify, review, simplify, evidence  ← CP8
```

## 8. Verification (end-to-end)

1. `./SCRIPTS/setup.ps1` on a clean clone → Azurite up, build clean, all tiers run.
2. `dotnet test PoMarriedFight.slnx --collect:"XPlat Code Coverage"` → ≥ 80 % Api+Shared (exclusions per A13).
3. No keys: `dotnet run` → banner, WATCH J1 and FIGHT J2 complete (E2EUI `WatchFlowTests`, `FightFlowTests`).
4. Real key (`az login` + seeded KV): WATCH ttft/first-audio log; FIGHT greeting-by-tag ≤ 15 s, interrupt at 45 ± 5 s on the fixture, analysis ≤ 45 s; second fight → digest “2 fights”.
5. Playwright 390 px + theme persistence; `PaletteContrastTests`; raw-control grep test.
6. `az bicep build`; CI on push (only when you approve the push); post-deploy smoke.
7. `docs/VERIFICATION.md` maps all 18 SPEC §12 criteria to evidence.

## 9. Library integration map (selected 2026-09-06 — "use recommended")

Selected: Blazored.LocalStorage 4.5.0 · Toolbelt.Blazor.HotKeys2 6.2.1 · Humanizer.Core 3.0.10 · WASM AOT+trim (publish) ·
FluentValidation 12.1.1 (+DI ext) · Blazored.FluentValidation 2.2.0 · Vogen 8.0.7 · Riok.Mapperly 4.3.1 · Carter 10.0.0 ·
Microsoft.FeatureManagement.AspNetCore 4.7.0 · Microsoft.Extensions.Caching.Hybrid 10.9.0 · AspNetCore.HealthChecks.Uris 9.0.0 ·
Serilog.Sinks.Seq 9.1.0 · Concentus 2.2.2 (+Concentus.Oggfile — verify at T41b) · SixLabors.ImageSharp 4.1.1 ·
AwesomeAssertions 9.6.0 (replaces FluentAssertions) · Verify.Xunit 31.12.5 · Bogus 35.6.5 · ReportGenerator 5.5.11 (tool) ·
Microsoft.Extensions.Diagnostics.Testing 10.9.0 · Meziantou.Analyzer 3.0.217 · Microsoft.VisualStudio.Threading.Analyzers 18.7.23 ·
Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0 · NetAnalyzers `AnalysisLevel=latest-all` · Husky.Net 0.9.1.

| Task | Library work added to its manifest |
|---|---|
| T01 | All packages pinned in `Directory.Packages.props`; `Directory.Build.props`: `AnalysisLevel=latest-all`, Meziantou + VSTHRD + BannedApiAnalyzers referenced solution-wide, `BannedSymbols.txt` (DateTime.Now/UtcNow → TimeProvider, `new HttpClient()`, `.Result`/`.Wait()`, `TableServiceClient(string)`/`BlobServiceClient(string)` connection-string ctors); `tests/Directory.Build.props` implicit using `AwesomeAssertions`; `.husky/` (pre-commit: `dotnet format --verify-no-changes` + Unit tests; commit-msg: `^T\d\d[a-z]?: `) + `.config/dotnet-tools.json` (husky, reportgenerator); `docker-compose.yml` adds `seq` (port 5341); Client csproj: `RunAOTCompilation`/`PublishTrimmed` **only when** `-p:Aot=true` (used by deploy.yml, not local builds) |
| T02 | `ProfileId`, `MatchId`, `FighterId` become Vogen value objects (`[ValueObject<string>]`, `NormalizeInput` upper-cases, `Validate` 1–3 alphanumerics, STJ + `IParsable` generated); hand-written record structs are not ported |
| T03 | `AddCarter()`/`MapCarter()` — every `XEndpoints` is an `ICarterModule` (`AddRoutes(IEndpointRouteBuilder)`), so `Program.cs` never lists endpoints; `AddFeatureManagement()` over `Features:*`; `AddHybridCache()`; Serilog `WriteTo.Seq` from `Serilog:Seq:ServerUrl` (Development only) |
| T04 | `AddUrlGroup` checks (Gemini `generativelanguage.googleapis.com`, Fish, Azure Speech region) tagged `ai`, surfaced on `/api/health/details`; `IFeatureManager` replaces `GetValue<bool>` in `FeatureFlagEndpoints` |
| T07 | `AddBlazoredLocalStorage()`, `AddHotKeys2()` |
| T08 | `ThemeToggle` persists via `ILocalStorageService` (theme.js still stamps pre-paint from the same key) |
| T11 | `TestSupport/Fakers.cs` — Bogus fakers for profiles, fighters, turns, transcripts (seeded, deterministic) |
| T12 | `ProfileMapping` = `[Mapper] static partial class`; `Shared/Validators/CreateProfileRequestValidator` (FluentValidation, in Shared so both sides share it); endpoints validate via `IValidator<T>` filter |
| T13 | `ProfileImageService` uses ImageSharp (decode any → 512×512 cover crop → PNG) |
| T14 | `ProfileEditDialog` uses `<FluentValidationValidator>` inside `RadzenTemplateForm` |
| T16/T23 | Dev fakes draw persona/dialogue material from the Bogus fakers |
| T23, T31, T32, T39 | Verify snapshots: `RoundPromptBuilder` (system + user halves), `JudgePromptBuilder`, `LiveSetupBuilder` JSON, `HostPersona.SystemInstruction` per persona, `AnalysisSchema.Build()` — `*.verified.txt` committed |
| T26/T36 | Last WATCH matchup / last fighter pair remembered in LocalStorage |
| T27/T37 | HotKeys2: `Space` SLAP, `Esc` cancel, `E` end fight, `?` shortcut sheet (`RadzenDialog`) |
| T30 | `Shared/Validators/CreateFightRequestValidator` (distinct tags, persona known, topic ≤ 200) |
| T33 | `DebateOrchestratorTests` assert `[LoggerMessage]` events with `FakeLogger` (tool call ok/fail, nudge kinds, persist failure) |
| **T41b (new)** — Opus recordings | Files: `Api/Features/Storage/OpusAudio.cs` (Concentus encode PCM16 → Ogg/Opus, decode → PCM), `DebateOrchestrator.cs` (persist tracks as `.opus`, WAV kept when `Audio:StoreOpus=false`), `AnalysisPipeline.cs` (uploads `audio/ogg`), `WavSlicer.cs` (decode Opus before slicing), `AudioBlobStore.cs` (content types). Tests: `OpusAudioTests` (round-trip length ± 20 ms, size ≤ 15 % of WAV), pipeline + clip tests on Opus input. Deps: T42. Guarded by `PoMarriedFight:Audio:StoreOpus` (default true) |
| T45/T46 | `HybridCache` (30 s) around leaderboard + fighter profile reads; invalidated by tag on analysis completion / delete |
| T47/T48 | Humanizer for relative times, counts, ordinals |
| T50 | CI: `reportgenerator -reporttypes:Html;MarkdownSummary` + `-minimumCoverageThresholds` line ≥ 80 (fails the job); publish with `-p:Aot=true`; `dotnet tool restore` |
| T51 | `setup.ps1` runs `dotnet tool restore` + `dotnet husky install`; README documents Seq at http://localhost:5341 |

## 10. Build-time amendments

- **Program.cs registrations are exempt from the manifest rule.** Carter removes endpoint mapping from `Program.cs`, but
  middleware and service registration (`AddPoX()` / `UseX()`) still land there. Any task may add its one or two
  registration lines to `Program.cs`; the manifest lists the feature files only. (Applied from T04 on; T03/T05 also
  touched it.)
- **T05 ran before T04**: `/api/diag`'s `RequireAuthorization` needs the auth services, so auth precedes diagnostics.
- **`ApiFactory` (E2EAPI) was created in T05** with the auth settings; T11 extends it with the in-memory stores and fakes.
- **T13 manifest additions**: `Api/Common/ValidationFilter.cs` (the `IValidator<T>` endpoint filter every request body
  uses from here on — first consumer, so it lands here), `ConfigKeys.Seed.AdminEmails` (one key), and the six face PNGs
  under `Client/wwwroot/images/profiles/` moved from T14 to T13 because the seed endpoint stores them as face blobs.
  `SeedResultDto` joins `Shared/Models/ProfileModels.cs` (T13's one Shared model file).
- **Faces are blobs only** (SPEC §6 `FacePic (blob)`): `POST /api/profiles/{id}/face` takes the raw image body,
  `ProfileImageService` crops it to a 512 px PNG (ImageSharp) and stores `{initials}.png` in the faces container;
  `ProfileDto.HasFace` + the anonymous `GET .../face` replace PoMarriedLife's inline data-URI path.
- **T14 manifest additions**: `Client/Services/ApiClient.cs` gains the profile calls and an `ApiException` that carries
  the server's problem details (the plan listed `ApiClient.cs` under T26 but the Profiles UI is its first consumer);
  `Client/Program.cs` registers the shared validator for `<FluentValidationValidator>` (registration line, exempt).
  The edit dialog is hosted by Radzen `DialogService` (CSP allows it: no inline script) rather than PoMarriedLife's
  hand-rolled modal; delete confirms through `DialogService.Confirm`.
- **T19 manifest notes**: `Api/Features/Voice/TtsCache.cs` is created here holding only `ITtsCache` + `NullTtsCache`,
  because `RoutingTtsService` takes the cache seam as a constructor dependency; T20 fills the same file with
  `TtsCacheKey` + `BlobTtsCache` and swaps the registration, leaving the routing service untouched. `TtsRoutingOptions`
  lives in `RoutingTtsService.cs` (as in PoMarriedLife) rather than its own file, and `ConfigKeys.Ai.TtsWireFormat`
  is one new Shared key. Fish and Azure implement T18's `ITtsProvider` instead of getting an interface each.
- **T20 manifest notes**: the preview button went on `ProfileCard` as planned, and the endpoint takes the whole
  persona so an unsaved draft can be auditioned. `ApiClient.cs` gains `PreviewLineAsync`, `Client/Program.cs`
  registers `AudioInterop` and `index.html` loads `js/audio.js` (registration/include lines). The preview prompt is
  deliberately minimal and local to `ProfileEndpoints`; the real round prompts belong to T23. `TtsAudioDto` and
  `PreviewLineResponse` join `Shared/Models/ProfileModels.cs` (T20's one Shared model file).
- **The blob TTS cache degrades, never throws**: an outage arrives as an `AggregateException` from the retry policy,
  so both cache paths catch broadly. The E2EAPI/E2EUI hosts run with `TtsCacheEnabled=false` because they have no
  storage account and the retry budget otherwise dominated the run (81 s -> 1 s).

