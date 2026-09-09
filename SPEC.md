# PoFightJudge — Specification

Status: v1 (2026-09-06). What the app must do; interview answers and research are recorded here. What was
actually built, in order, is [tasks/todo.md](tasks/todo.md).

## 1. Objective

**PoFightJudge** is one Blazor WebAssembly app with two ways to have a married-couple fight and get judged:

- **WATCH** — pick a husband profile and a wife profile, give a topic, and watch the AI versions argue: three rounds
  each, spoken in each character's voice, a SLAP heckle available once per match, then a Gemini judge names the winner
  and the app scores both spouses on ten "argument analytics". (From PoMarriedLife.)
- **FIGHT** — two real people share one microphone. An AI host — by default **the Referee**, a wry marriage
  counselor-meets-boxing-referee — greets both by name, moderates turns, interrupts monologues, grills each with
  probing questions (Google Search when a fact matters), rules on logic and correctness aloud, and closes with one line
  of unsolicited marital advice. Afterwards a pipeline diarizes the recording, computes ~25 speech metrics and asks a
  judge model for a structured assessment shown on a courtroom-style Verdict page. (From PoArgueJudge.)

The two modes have **two kinds of profile**:

- **`Profile`** (WATCH) — a hand-authored AI persona: husband/wife, traits, sliders, voice, face. You write it; the AI
  plays it.
- **`Fighter`** (a real person, either mode) — identified by a 1–3-character tag typed before they speak. **Nobody
  writes a Fighter.** It is created automatically the first time that tag argues and its *argument-style profile* is
  built up over time from every debate they speak in: record, averages, badges, rivalries, tone, signature phrases,
  favourite fallacies, typical opener, CEFR trend, emotion profile. The host reads that profile before the bell.

**A person keeps one identity across both modes.** The same tag is used whether they argue an AI persona in WATCH or
another person in FIGHT, so one person has one profile that grows from every match they spoke in. There is no
anonymous play: a human side always types a tag and the match is always recorded. (This replaces the original apps'
`SELF` sentinel, which was an unrecorded exhibition and left the human with nothing that accumulated.)

The three matchups that fall out of this:

| Matchup | Mode | Sides | What is recorded |
|---|---|---|---|
| CPU vs CPU | WATCH | two personas | both personas' WATCH records |
| CPU vs human | WATCH | persona + tagged person | the persona's WATCH record, and the person's record + style snapshot |
| Human vs human | FIGHT | two tagged people | both people's records + style snapshots |

History is unified (one list, a Mode column); leaderboards are per mode (AI profiles vs real fighters).

### User journeys

**J1 — WATCH a fight (primary)**
1. Sign in (Microsoft in Production; *Continue as Guest* in Development). Home shows two big cards: WATCH and FIGHT.
2. WATCH → pick husband + wife from profile cards (or SELF for one side) → type a topic (or *Random*) → **Start**.
3. Round 1 line appears within 5 s and starts speaking on its first clause; each subsequent line prefetches while
   the current one plays. The spectator may hit **SLAP** once; the slapped spouse reacts furiously and the other's turn
   is skipped. Tension meter and moods update per line.
4. After 6 lines (7 with a slap) the judge speaks a verdict; the screen shows the winner, the verdict text, and both
   spouses' `AdvancedStats` bars. Wins/losses post to the leaderboard; a person on either side is recorded under their tag.
5. The match appears in History with replayable audio.

**J2 — FIGHT live (primary)**
1. FIGHT → type two fighter tags (1–3 letters/digits; autocomplete from existing Fighters, defaults: last pair used) →
   optionally type the topic → pick a host (Referee default) → **Start the fight**. Unknown tags become new Fighters
   on the spot. Browser asks for mic permission.
2. The host greets both by tag ("In the red corner, KDH — four fights, two wins, a weakness for the strawman…"),
   restates or asks for the topic, states the rules in one sentence, and gives the floor to fighter 1. On-screen:
   phase banner, 3:00 countdown, live captions, who has the floor, each fighter's record.
3. Host moderates turns; interrupts anyone who holds the floor 45 s; after 10 s of silence prompts by name, after 20 s
   moves on. Between 1:00 and 2:00 (or at 3:00) it ends the debate and probes each fighter, one at a time, 1–3 questions.
4. Host calls `deliver_verdict`, announces logic winner, correctness winner, overall winner + three reasons + one line
   of marital advice. Session ends; the page hands itself over to `/verdict/{id}`.
5. Verdict page shows the ruling immediately and the full analysis ≤ 45 s later: per-fighter metrics, assessment,
   claims table, emotion timeline, highlight clips, coaching tips. Both Fighters' style profiles update automatically
   ("KDH: 5 fights · Ad hominem ×3 · opens with 'Look, the thing is…'").

**J3 — Profiles (WATCH personas)**: list → create (manual or *Generate with AI*) → edit traits/sliders/voice → upload or
generate a face → preview a spoken line → view Watch record (W/L, `AdvancedStats` averages, rivalries).

**J3b — Fighters (real people)**: list (auto-populated) → open a Fighter → record, badges, streak, rivalries, form chart,
and the **argument-style profile** built from their fights (tone, phrases, fallacies, opener, CEFR trend, emotions,
latest coaching tips). Only allowed edits: display name and delete.

**J4 — History & leaderboards**: History lists every match (Mode, date, topic, participants, winner, status) → open →
replay (WATCH audio / FIGHT verdict + analysis) → delete (cascade). Leaderboard page has two tabs: **AI profiles**
(Watch) and **Fighters** (Fight), each ranked globally.

### Success
Both J1 and J2 run end-to-end with **fakes** (no keys) and with **a real Gemini key**, in one sitting, on a phone-width
viewport and a laptop, with every screen built from Radzen components.

## 2. Tech stack (verified 2026-09-06)

| Layer | Choice |
|---|---|
| Runtime | .NET 10 (SDK **10.0.400**, `rollForward: latestFeature`), C# 15, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, nullable, CPM with transitive pinning |
| API | ASP.NET Core Minimal API, vertical slices (`Features/{Name}`), SignalR (MessagePack), Serilog, OpenAPI + Scalar, rate limiter, OpenTelemetry → Azure Monitor |
| Client | Blazor WebAssembly hosted by the API (same origin, no CORS), **Radzen.Blazor 11.3.2**, scoped `.razor.css`, design tokens, AudioWorklet/Web Audio/Web Speech JS interop |
| Shared | `ApiRoutes`, `ConfigKeys`, `ProfileId`/`MatchId` (`readonly record struct`, `IParsable`), DTOs, `LiveHubContract`, `HostPersonaCatalogue`, JSON source-gen context |
| AI — live host | Gemini Live API, model **`gemini-3.1-flash-live-preview`** (AI Studio only; no Vertex), `wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent`; in 16 kHz/16-bit/mono PCM, out 24 kHz PCM; `responseModalities:["AUDIO"]`, `tools:[{googleSearch:{}},{functionDeclarations:[set_players,start_turn,end_debate,ask_probe,deliver_verdict]}]`, input+output transcription, automatic VAD, `sessionResumption`, `contextWindowCompression`; voice per persona (Referee = `Puck`). Limits: 15-min audio session, ~10-min connection (`GoAway` → resume) |
| AI — rounds | **`gemini-3.1-flash-lite`** (GA; the `-preview` id shut down 2026-05-25), `generateContent` + SSE `streamGenerateContent`, JSON output |
| AI — judge / analysis / profile gen | **`gemini-3.7-flash`** (`generateContent`, `responseSchema`, `thinkingConfig.thinkingLevel: low`, analysis on `service_tier: flex`) |
| AI — diarization | **`gemini-3.5-transcribe`** via `POST /v1beta/interactions` (`diarization_mode: speaker`, word timestamps), audio via Files API; used only when the live caption transcript is too thin (< 30 words) |
| AI — TTS | **`gemini-3.1-flash-tts-preview`** (`responseModalities:["AUDIO"]`, `speechConfig.voiceConfig.prebuiltVoiceConfig`), behind Fish Audio (`POST https://api.fish.audio/v1/tts`, `reference_id`, Bearer) and Azure Speech (REST TTS + fast transcription) |
| Gemini transport | Raw `HttpClient` / `ClientWebSocket` with `x-goog-api-key`; no Google SDK (it exposes neither Live nor Interactions) |
| Storage | Azure.Data.Tables 12.12.0, Azure.Storage.Blobs 12.29.2, Azure.Identity 1.21.0 — **endpoint + `DefaultAzureCredential` only, no connection strings anywhere**. Local: Azurite `--oauth basic` over HTTPS (dev cert) on **12000 blob / 12001 queue / 12002 table** |
| Auth | Development/Test: `FakeAuthHandler` (`X-Fake-User`) + `GuestMiddleware` (cookie); Production: Entra ID — `Microsoft.Identity.Web` 4.14.2 JWT bearer (`ValidAudiences = [clientId, api://clientId]`), `Microsoft.Authentication.WebAssembly.Msal` with tenant-specific authority. `FakeAuthHandler` throws in Production |
| Secrets | `kv-poshared` (RG `PoShared`), prefix **`PoFightJudge--`** → `PoFightJudge:*`; `KeyVault:Uri` from `appsettings.Development.json` / App Service setting. No `UserSecretsId` |
| Telemetry | Shared App Insights component in `PoShared` via `APPLICATIONINSIGHTS_CONNECTION_STRING`; 10 % trace sampling, health probes filtered |
| Tests | xunit 2.9.3, xunit.runner.visualstudio 3.1.5, NSubstitute 6.2.0, **AwesomeAssertions 9.6.0**, bunit 2.9.0, Xunit.SkippableFact 1.5.85, Microsoft.AspNetCore.Mvc.Testing 10.0.x, Microsoft.Extensions.TimeProvider.Testing 10.x, **Microsoft.Extensions.Diagnostics.Testing 10.9.0** (FakeLogger), **Verify.Xunit 31.12.5** (prompt snapshots), **Bogus 35.6.5**, Testcontainers.Azurite 4.14.0, Microsoft.Playwright 1.62.0, coverlet.collector 10.0.1 + **ReportGenerator 5.5.11** (80 % gate) |
| Libraries (selected 2026-09-06) | **Carter 10.0.0** (endpoint modules), **FluentValidation 12.1.1** + **Blazored.FluentValidation 2.2.0** (shared validators), **Vogen 8.0.7** (ids), **Riok.Mapperly 4.3.1** (mapping), **Microsoft.FeatureManagement.AspNetCore 4.7.0**, **Microsoft.Extensions.Caching.Hybrid 10.9.0**, **AspNetCore.HealthChecks.Uris 9.0.0**, **Serilog.Sinks.Seq 9.1.0** (dev), **Concentus 2.2.2** (Opus recordings), **SixLabors.ImageSharp 3.1.x** (faces; 4.x needs a paid licence key), **Blazored.LocalStorage 4.5.0**, **Toolbelt.Blazor.HotKeys2 6.2.1**, **Humanizer.Core 3.0.10**; analyzers **Meziantou 3.0.217**, **VS.Threading 18.7.23**, **BannedApiAnalyzers 5.6.0**, AnalysisLevel=latest-all; **Husky.Net 0.9.1** hooks; WASM AOT + trimming on publish |
| Infra | Bicep (`infra/`), GitHub Actions OIDC deploy, Azure App Service Linux F1 (West US 2), Storage (East US 2, shared-key disabled) |

## 3. Commands

```powershell
./SCRIPTS/setup.ps1                      # prereqs, Azurite (OAuth+HTTPS), az login + secret presence check, restore, build, test, Playwright
./SCRIPTS/azurite.ps1 [-Down] [-Wipe]    # local storage emulator on 12000/12001/12002
dotnet build PoFightJudge.slnx -c Release          # 0 warnings — TreatWarningsAsErrors
dotnet format PoFightJudge.slnx --verify-no-changes # lint (CI gate)
dotnet test tests/PoFightJudge.Unit                # fast, no Docker
dotnet test tests/PoFightJudge.Integration         # Testcontainers.Azurite (Docker)
dotnet test tests/PoFightJudge.E2EAPI              # WebApplicationFactory + fakes, no Docker
dotnet test tests/PoFightJudge.E2EUI               # Playwright Chromium, self-hosted Kestrel, fake mic WAV
dotnet test PoFightJudge.slnx --collect:"XPlat Code Coverage"
dotnet run --project src/PoFightJudge.Api          # https://localhost:5001 (serves the WASM client)
az bicep build --file infra/main.bicep               # infra lint
```

Opt-in real-key checks: `GEMINI_API_KEY` (or KV) enables `GeminiLiveSmokeTests`; `POFIGHTJUDGE_E2E_REAL=1` drives
the E2EUI fight against the real host; `POFIGHTJUDGE_SMOKE_URL` points `DeployedSiteTests` at a live site.

## 4. Project structure

```
PoFightJudge.slnx  Directory.Build.props  Directory.Packages.props  global.json  docker-compose.yml  .editorconfig
README.md  CLAUDE.md  AGENT.md  NET_RULES.md  SPEC.md  tasks/todo.md
SCRIPTS/setup.ps1  SCRIPTS/azurite.ps1  SCRIPTS/seed-secrets.ps1
infra/main.bicep  infra/resources.bicep  infra/storage-role.bicep  infra/keyvault-access.bicep
.github/workflows/deploy.yml

src/PoFightJudge.Shared/
  ApiRoutes.cs  Configuration/ConfigKeys.cs  Identifiers/{ProfileId,MatchId}.cs  SelfPlayer.cs
  Models/{ProfileModels,WatchModels,FightModels,LiveModels,AnalysisModels,MetricsModels,RecordModels,
          HostPersonaModels,AuthModels,HealthModels,DiagModels,FeatureFlagsDto}.cs
  Json/SharedJsonContext.cs
src/PoFightJudge.Api/
  Program.cs  Common/{DependencyInjection,SecretManager,SecretMasker,Outcome,ForwardedHeadersConfig,
                      GlobalExceptionHandler,SecurityHeadersMiddleware,AzuriteStorageScopedCredential,StorageBootstrap}.cs
  Features/Auth/          FakeAuthHandler, FakeAuthOptions, GuestMiddleware, ClaimsPrincipalExtensions, AuthEndpoints
  Features/Diagnostics/   HealthEndpoints, DiagEndpoints, FeatureFlagEndpoints, StartupSecretValidator,
                          StartupHealthState, StorageHealthCheck, AiLatencyTracker
  Features/Profiles/      Profile, ProfileTableEntity, ProfileRepository, ProfileImageService, ProfileGenerator,
                          ProfileEndpoints, Seeding/SeedEndpoints
  Features/Fighters/      Fighter, FighterTableEntity, FighterRepository, FightStyleSnapshot, StyleProfileBuilder,
                          FighterStatsBuilder, FighterEndpoints
  Features/Ai/            GeminiHttp, GeminiRetryHandler, GeminiResilience, GeminiModelOptions, IGeminiText,
                          GeminiTextClient, Fakes/{FakeGeminiText,FakeTts,FakeTranscription,FakeLiveClient…} (non-Production DI only)
  Features/Voice/         ITtsService, RoutingTtsService, TtsAudio, TtsAudioFormats, SentenceChunker,
                          FishAudioService, AzureSpeechService, GeminiTtsService, ITtsCache, BlobTtsCache, ITranscriptionService,
                          AzureTranscriptionService, GeminiTranscriptionService
  Features/Watch/         WatchMatch, WatchRound, ArgueScoreCalculator, AdvancedStats, AttitudeSelector, RoundPromptBuilder,
                          JudgePromptBuilder, WatchEndpoints (generate-round[-stream], round-audio[-stream], verdict, transcribe),
                          WatchAudioBlobService
  Features/Live/          GeminiLiveClient, LiveSetupBuilder, LiveMessages, ToolDeclarations, GeminiOptions, ILiveSocket
  Features/Fight/         DebateSession, DebateOptions, DebateOrchestrator, HostPersona, ShowSetup, SessionRegistry,
                          CaptionTranscript, WavWriter, WavSlicer, FightEndpoints
  Features/Analysis/      GeminiFilesClient, GeminiTranscribeClient, GeminiJudgeClient, AnalysisSchema, SpeakerMapper,
                          SpeechMetrics, Lexicon, Readability, HighlightFinder, JsonRepair, PollBackoff, AnalysisPipeline,
                          AnalysisOptions, AnalysisEndpoints
  Features/Records/       MatchRepository (Matches/Turns/Analyses), WatchResultRepository, FightResultRepository,
                          ProfileStatsBuilder, HistoryEndpoints, LeaderboardEndpoints
  Features/Storage/       StorageEntities, AudioBlobStore
  Hubs/LiveHub.cs
src/PoFightJudge.Client/
  Program.cs  App.razor  _Imports.razor  Layout/MainLayout.razor
  Pages/{Home,Login,Authentication,Health,Profiles,ProfileRecord,Fighters,FighterProfile,Watch,WatchPlay,Fight,FightLive,
         Verdict,History,Leaderboard,NotFound}.razor(+.css)
  Components/{PageShell,ThemeToggle,FakeAiBanner,EmptyState,LoadingRows,ProfileCard,ProfilePicker,ProfileEditDialog,
              FighterTagInput,FighterCard,StyleProfilePanel,SpeakerCard,StatBar,TensionMeter,InterjectionBar,PhaseBanner,
              DebateTimer,CaptionFeed,HostIndicator,MicMeter,LevelBars,ScoreboardBanner,PlayerCard,StatTile,StatCell,
              TraitScores,ClaimsTable,EmotionChart,HighlightReel,FormSparklines,ModeBadge}.razor(+.css)
  Services/{ApiClient,LiveConnection,AudioInterop,MicInterop,SpeechRecognitionInterop,ThemeInterop,TelemetryService}.cs
  wwwroot/index.html  wwwroot/manifest.webmanifest  wwwroot/sw.js  wwwroot/css/app.css  wwwroot/icons/*
  wwwroot/js/{audio,pcm-worklet,mic,speech,theme,frame}.js  wwwroot/images/profiles/*.png
tests/PoFightJudge.TestSupport/   FakeLiveSocket, ScriptedShow, FakeAnalysisClients, InMemoryRepositories, FakeHttpHandler
tests/PoFightJudge.Unit/          mirrors Features/* + Client/
tests/PoFightJudge.Integration/   AzuriteFixture (Testcontainers), repositories, blob store, TTS cache, cascade delete, fighter auto-create
tests/PoFightJudge.E2EAPI/        ApiFactory (fakes), auth/health/profiles/fighters/watch/fight/analysis/history/leaderboard
tests/PoFightJudge.E2EUI/         AppFixture (Kestrel + fakes), WatchFlow, FightFlow, ProfileFlow, ThemeAndViewport,
                                    DeployedSite; fixtures/debate-60s.wav
```

## 5. Code style + conventions

```csharp
namespace PoFightJudge.Api.Features.Fight;

/// <summary>Drives one fight through its phases. Pure state; no I/O, no timers.</summary>
public sealed class DebateSession(DebateOptions options, MatchId matchId, DateTimeOffset startedAt, ShowSetup setup)
{
    public SessionPhase Phase { get; private set; } = SessionPhase.Intro;
    public PlayerId? CurrentSpeaker { get; private set; }

    public Outcome StartTurn(PlayerId player, DateTimeOffset now)
    {
        if (Phase is SessionPhase.Setup) { Phase = SessionPhase.Debate; _debateStartedAt = now; }
        if (Phase is not SessionPhase.Debate) return Outcome.Fail($"Cannot start a turn during {Phase}.");
        OpenTurn(player, TurnKind.Talk, string.Empty, now);
        return Outcome.Ok;
    }
}
```

- Primary constructors, `sealed`, records for DTOs/value objects, `Outcome`/`Outcome<T>` for expected failures,
  `TimeProvider` for anything time-based, `[LoggerMessage]` source generators on hot paths, STJ source-gen context
  in Shared, `IParsable` ids that bind as route parameters.
- **Single sources of truth**: `ApiRoutes` (every path — server maps from it, client builds from it, tests assert it),
  `ConfigKeys` (every config key), `LiveHubContract` (hub method names), `HostPersonaCatalogue`, `Interjections`.
  A literal route/key/method string anywhere else is a defect.
- Endpoint modules are `static class XEndpoints { public static void Map(RouteGroupBuilder api) }` registered
  explicitly in `Program.cs`. One `Map` per feature.
- Fakes: `IsFake => true` on every stand-in; registered only when `!IsProduction()` and the real key is absent (or
  `Features:UseFakeAi=true`); `/api/features` reports `UseFakeAi` and the client shows the banner. A fake match in
  a fake match is fully recorded (it is the E2E path) but flagged `IsFake` on the row.
- UI: Radzen components only (`InputFile` is the one framework exception); `style` attribute only to pass CSS custom
  properties; presentation in `.razor.css`; every value in `app.css` resolves through a `--po-*` token; `color-scheme`
  + `light-dark()` theming, `data-theme` pinned by `theme.js` before first paint, **no `prefers-color-scheme` blocks**;
  three breakpoints (40/48/64 rem); pages open with `<PageShell>`; one card (`.po-card` + modifiers); AA-safe text
  tokens; Radzen theme variables bridged to the tokens; `RadzenNotification` for action outcomes, inline alerts for
  page state; `RadzenDataGrid` for every table, `EmptyState`/`LoadingRows` for every list.
- Files: one public type per file when there is one; `<Concept>Models.cs` for a cohesive cluster. Namespaces match
  folders. `dotnet format` clean.
- Git: `master` only; one commit per task (`T07: WATCH verdict endpoint + AdvancedStats`); commit freely, **ask before
  every push**; MinVer for versioning (never set `<Version>`).

## 6. Domain model

| Concept | Shape | Storage |
|---|---|---|
| `Profile` (WATCH persona) | `ProfileId` (= upper-cased initials, 1–3 alphanumerics), `Role` (Husband/Wife), name/age/occupation, likes/dislikes, 6 trait flags, 9 sliders (0–100), love language/attachment/stress response, common arguments, philosophy, `FacePic` (blob), `TtsSettings` (pitch, speed, Gemini voice, `FishReferenceId`). Record is derived, not stored | `Profiles` table: PK `"profile"`, RK initials — **global** |
| `Fighter` (real person) | `FighterId` (= upper-cased tag, 1–3 alphanumerics), `DisplayName` (defaults to the tag; the only editable field), `CreatedAt`, `CreatedByUserId`. **Auto-created** by `POST /api/fights` for any unknown tag. Everything else — record, averages, badges, streak, rivalries, form and the **style profile** — is derived on read from `FightResults` | `Fighters` table: PK `"fighter"`, RK tag — **global** |
| `Match` | `MatchId`, `UserId`, `Mode` (Watch/Fight), `Topic`, `HusbandId`/`WifeId` (Watch) or `Fighter1Id`/`Fighter2Id` (Fight), display names denormalised, `Persona`, `Status` (Live/Analyzing/Ready/Failed), `Winner`, `VerdictText`, `StartedAt/EndedAt`, audio blob names, `IsFake` | `Matches` table: PK userId, RK matchId |
| `Turn` | index, `Speaker` (Host/Player1/Player2 ↔ Husband/Wife), `Kind` (Talk/Probe/Interrupt/Verdict/Round), start/end seconds, text, mood, audio format | `Turns` table: PK matchId, RK index |
| `Analysis` | status, `ReportJson` (`AnalysisReportDto`), error | `Analyses` table: PK matchId, RK `"analysis"` |
| `WatchResult` | one row per profile per WATCH match: `Won`, `Opponent`, `Topic`, `At`, `UserId`, `AdvancedStats` (10 values) | `WatchResults` table: PK profile initials, RK matchId |
| `FightResult` | one row per fighter per FIGHT match: `Won`, `WonLogic`, `WonCorrect`, `Opponent`, `Topic`, `At`, `UserId`, scores (logic, clarity, persuasiveness, evidence, rebuttal, confidence, politeness, aggression, listening), metrics (wpm, fillers/100, talk share, interruptions made), fallacies count, and a **`FightStyleSnapshot`** (tone descriptors, ≤ 3 repeated phrases, fallacy names, opener = first 12 words of the fighter's first turn, CEFR, emotion profile, best-moment quote ≤ 120 chars, ≤ 3 coaching tips) | `FightResults` table: PK fighter tag, RK matchId — re-analysis replaces, delete removes |
| Blobs | `audio/{matchId}/players.wav`, `audio/{matchId}/host.wav`, `audio/{matchId}/live-transcript.json`, `audio/{matchId}/{roundIndex}.{mp3|pcm}`, `faces/{initials}.png`, `tts-cache/{sha256}.{mp3|pcm}` | containers `pofightjudge-audio`, `-faces`, `-ttscache` |

Records, leaderboards, rivalries, badges and form are **derived on read** (`ProfileStatsBuilder` over `WatchResults`,
`FighterStatsBuilder` over `FightResults`); nothing is accumulated, so delete and re-analysis stay correct.

### Fighter style profile (C7)
`StyleProfileBuilder.Build(IReadOnlyList<FightStyleSnapshot>)` is deterministic and unit-tested: tone descriptors
ranked by frequency (top 5), signature phrases (phrases seen in ≥ 2 fights, else the latest), favourite fallacies with
counts, most recent opener + "opens the same way N/M fights", CEFR trend (latest vs first), mean emotion profile,
latest coaching tips, one-line **host digest** ("KDH — 4 fights, 2–2, favours the strawman, opens with 'Look…'").
The digest is fed to the host's roster block; the full profile renders on the Fighter page. A fighter with no analysed
fights has an empty style profile and the host is told "first fight".

### Host personas
`Referee` (default) + `Puck`, `JudgeStern`, `Coach`, `Roastmaster`. All share the show format, tool contract and hard
rules; only the character block, voice and the verdict tail differ. The roster block carries each fighter's tag,
display name and host digest so the host can greet accurately and needle them on their record.

## 7. API surface (all under `ApiRoutes`)

| Route | Purpose |
|---|---|
| `GET /api/health`, `/healthz`, `/healthz/ready` | probes (storage + configuration); `/health` is the Blazor page |
| `GET /api/diag` | redacted diagnostics (auth required; admin-only outside Development) |
| `GET /api/features` | `UseFakeAi`, `DevGuestEnabled`, `HumanInWatch`, `BrowserSpeechRecognition` |
| `GET/POST /auth/me`, `/auth/guest`, `/auth/logout` | BFF-facing auth probes (root, not `/api`) |
| `GET/POST/PUT/DELETE /api/profiles[/{id}]`, `POST /api/profiles/{id}/face`, `POST /api/profiles/generate?role=`, `POST /api/profiles/preview-line`, `GET /api/profiles/{id}/record` | WATCH personas + record |
| `GET /api/fighters`, `GET /api/fighters/{tag}` (record + style profile), `PUT /api/fighters/{tag}` (display name only), `DELETE /api/fighters/{tag}` | real fighters (auto-created by `POST /api/fights`) |
| `POST /api/seed/profiles` | seed defaults (admin gate) |
| `POST /api/watch/generate-round/{h}/{w}`, `POST /api/watch/generate-round-stream/{h}/{w}`, `POST /api/watch/round-audio`, `POST /api/watch/round-audio-stream`, `POST /api/watch/verdict`, `POST /api/watch/transcribe` | WATCH interactive path (`ai-per-user` rate limit, 20/min) |
| `GET /api/audio/{matchId}/{index}` | archived round audio proxy |
| `POST /api/fights` → `{matchId}`, `GET /api/fights/{id}`, `POST /api/fights/{id}/transcript`, `GET /api/fights/{id}/analysis` (202/200), `GET /api/fights/{id}/clips/{n}` | FIGHT |
| `/hubs/live` | SignalR: `JoinSession`, `AudioIn`, `EndSession` ↑ · `AudioOut`, `AudioClear`, `Caption`, `Snapshot`, `Ended`, `Error` ↓ |
| `GET /api/matches`, `GET /api/matches/{id}`, `DELETE /api/matches/{id}` | unified history (per user) |
| `GET /api/leaderboard/watch`, `GET /api/leaderboard/fight` | global rankings per mode |

## 8. Testing strategy

| Level | Project | Tooling | Covers |
|---|---|---|---|
| Unit | `Unit` | xunit, NSubstitute, FluentAssertions, bunit, FakeTimeProvider | state machine, orchestrator, metrics, scorer, attitude, prompt builders (incl. cache ordering), style profile + host digest, stats builders, parsers, schema reflection, JSON repair, TTS routing matrix, chunker, secret manager, validator, palette contrast (parses `app.css`), components |
| Integration | `Integration` | Testcontainers.Azurite | repositories, blob store, TTS cache, cascade delete, fighter auto-create + re-analysis replace |
| E2E API | `E2EAPI` | `WebApplicationFactory` + TestSupport fakes | every route, auth gating (401), degraded 503, rate limit 429, watch round→verdict, fight create→hub→end→analysis 202→200, history, leaderboard |
| E2E UI | `E2EUI` | Playwright Chromium, `--use-fake-device-for-media-stream --use-file-for-fake-audio-capture=debate-60s.wav`, API on Kestrel with fakes | login → watch → verdict; login → fight → captions → verdict → history; create profile; theme persists; 390 px |
| Real-key smoke | `Unit` `[SkippableFact]` | KV / env key | one Live handshake, one round, one TTS, one judge call |

Coverage target **≥ 80 % line** on `Api` + `Shared` (excluding `Program.cs`, `Common/DependencyInjection.cs`, fakes).
Unit suite gates CI; the other tiers self-skip without their dependency and print a visible SKIPPED count.
**Never** skip/weaken/delete a failing test; never mock away validation.

## 9. Boundaries

- **Always**: TDD; one commit per task; secrets from Key Vault/env only; `TreatWarningsAsErrors`; Radzen + tokens;
  `ApiRoutes`/`ConfigKeys` single sources; fakes only outside Production; `FakeAuthHandler` throws in Production;
  update SPEC/todo when a decision changes; report failing tests as failing.
- **Ask first**: any `az` write (Key Vault secrets, Entra app registration changes, Bicep deploy, role assignments);
  creating the GitHub repo / OIDC app; **every `git push`**; deleting all matches/profiles; moving off F1 or changing a
  model tier; adding telemetry beyond App Insights; adding a NuGet package not in the approved list.
- **Never**: commit keys or connection strings; send audio anywhere except Gemini/Fish/Azure Speech and your own Blob;
  runtime fakes in Production; > 2 combatants; start the mic without a click; expose the Gemini key to the browser;
  a `prefers-color-scheme` block; a raw HTML form control.

## 10. Out of scope (v1)

Group fights / spectators / audience voting; one device per fighter; non-English; Ollama or any local LLM;
native/MAUI/app-store builds; offline PWA caching (install-only PWA is in); voice enrollment; hand-editing a
Fighter's style profile (it is automatic; delete a match to remove its contribution); **casting a Fighter into WATCH as
an AI version of themselves** (natural v1.1 — the style profile is already the prompt material); linking a Fighter to a
Profile; multi-tenant sharing of history; affective dialog model (emotion is post-hoc); SignalR backplane /
multi-instance (F1 is single instance by design).

## 11. Edge cases & error states

| Case | Behaviour |
|---|---|
| No Gemini key (Dev/Test) | Fakes register; banner "USING FAKE AI"; everything runs; WATCH results are exhibitions |
| No Gemini key (Production) | `StartupSecretValidator` → degraded: shell + `/diag` serve, `/api/*` → 503 with missing keys; `/api/health` unhealthy so the deploy fails |
| Fish / Azure Speech key absent or provider fails | Chain falls through; a voice problem never fails a round; `/diag` shows provider state |
| Mic denied / no mic | FIGHT page blocking error with retry; nothing saved. WATCH hides the human-side option |
| Live socket drops / `GoAway` | Resume with `sessionResumption` ≤ 3×; on failure end the session and analyse what was recorded |
| Browser closes mid-fight | Session ends and persists after 30 s without a client; a new fight by the same user ends the old one |
| Fighter talks > 45 s | Host interrupts by name and hands over; counted as `HostInterrupt` |
| Silence > 10 s / > 20 s | Host prompts by name / moves on |
| Both talk > 5 s | Host calls order; counted as overlap |
| 3:00 reached | Host ends debate and probes regardless |
| Runaway session | Hard cap 15 min wall clock; verdict phase ends 45 s after `deliver_verdict` at the latest |
| Diarization ≠ 2 speakers | `SpeakerMapper` maps by overlap with the turn timeline, merges extras, flags the report |
| Caption transcript < 30 words | Falls back to `gemini-3.5-transcribe` |
| Audio > 20 MB | Files API upload path |
| Analysis fails | Verdict page still shows the spoken ruling; "analysis failed — retry" button; no `FightResult` rows written, so the fighters' profiles are unchanged |
| Unknown fighter tag at setup | `Fighter` row created on `POST /api/fights`; the host is told "first fight" for that tag |
| Fighter deleted | `Fighters` row + all their `FightResults` removed; matches stay (tags denormalised on the row); leaderboard drops them |
| WATCH round generation fails | Retry once via resilience; then inline error with *Retry round*; match not persisted |
| WATCH verdict with 7 lines (slap) | Accepted (`MaxLinesPerGame = 7`) |
| A person takes a side in WATCH | Recorded like any other debate: the persona gets a WATCH result, the person gets a fighter result and a style snapshot from what they said |
| Same profile / same tag picked twice | Setup blocks with a message (`Initials.ArePair`) |
| Profile deleted with matches | Matches stay (names denormalised on the row); `WatchResults` for that profile removed; leaderboard drops them |
| Azurite down locally | `/api/health` degraded; Start buttons disabled with reason |
| Unauthenticated in Production | API 401; client redirects to `/login`; hub token via `access_token` query |
| Rate limit | 429 with `Retry-After`; client shows a toast and re-enables after the window |

## 12. Success criteria (measurable)

1. `dotnet build` (0 warnings) and `dotnet test` on all 5 test projects pass on a clean clone with Docker running; coverlet reports ≥ 80 % line on Api + Shared (exclusions in §8).
2. With **no keys** in Development, both J1 and J2 complete end-to-end with fakes and the banner visible (E2EUI `WatchFlow` + `FightFlow`).
3. With a **real key**: WATCH round 1 text appears ≤ 5 s and audio starts ≤ 8 s after Start (logged `ttft` and first-audio timestamps).
4. With a real key: the host greets both fighters **by their tags** within 15 s of joining, without asking for names, and mentions the record of any fighter with prior fights (turn log: `set_players` carries the seeded tags; persona test asserts the digest is in the roster block).
5. On the 60-s monologue fixture the host interrupts within 45 ± 5 s (orchestrator test + real-run log).
6. Debate phase never exceeds 3:00 (state-machine test + snapshot log).
7. Probe phase asks each fighter ≥ 1 question, one at a time (alternating `Probe(P1)`/`Probe(P2)` turns).
8. Spoken verdict names logic winner, correctness winner, overall winner, three reasons **and one line of marital advice** (persona test asserts the instruction; real-run transcript shows it).
9. Analysis available ≤ 45 s after the fight ends (logged `AnalysisSeconds`); Verdict page renders every field of `PlayerMetricsDto` and `PlayerAssessmentDto` for both fighters (reflection test + screenshot).
10. Starting a fight with two unknown tags creates two `Fighter` rows; after the analysis each has one `FightResult` with a populated `FightStyleSnapshot`; `GET /api/fighters/{tag}` returns a style profile whose fallacy counts, phrases and opener match the report; after a second fight the host digest reports "2 fights" (integration + E2EAPI tests).
11. WATCH verdict writes two `WatchResult` rows; FIGHT analysis writes two `FightResult` rows; `/api/leaderboard/watch` ranks profiles and `/api/leaderboard/fight` ranks fighters; deleting a match removes its rows and blobs and the fighter's profile no longer reflects it (integration test).
12. History lists a WATCH match and a FIGHT match with correct Mode badges; opening each shows replay / verdict; the Fighters page lists both tags with records (E2EUI).
13. Production configuration: unauthenticated `/api/*` → 401; `/login` 200; `FakeAuth` registration throws (unit test); guest requires an explicit `PoFightJudge:Auth:AllowFakeAuth`.
14. `/api/diag` never contains a secret value (regex test against key/connection-string patterns).
15. Dark/light toggle persists across reload; every page usable at 390 px; every text colour pair ≥ 4.5:1 in both themes (`PaletteContrastTests`).
16. No raw `<button>`, `<input>`, `<select>`, `<textarea>` in any `.razor` file except `InputFile` (grep test in Unit).
17. `manifest.webmanifest` served with icons and `display: standalone`; Lighthouse "installable" passes (E2EUI asserts the manifest link + fields).
18. `az bicep build` passes; `deploy.yml` runs build + Unit + publish on push to `master`; a deploy (when you approve it) ends with `/api/health` 200 and the browser smoke test green.

## 13. Open questions

None blocking. Resolved during the interview (2026-09-06): both modes equal; fresh solution; Entra + dev guest;
Bicep + new RG; **separate profile kinds — hand-authored `Profile` for WATCH, auto-created `Fighter` for FIGHT whose
style profile is derived from every analysed fight** (revised from "one shared Profile" at spec review); Referee host
default; copy secrets from PoMarriedLife; keep everything until deleted; PoArgueJudge's test bar; hobby budget, no
deadline; dark default, 390 px, AA; install-only PWA.

## 14. Design decision (2026-09-06)

Ten layout concepts were drafted on one canvas; **04 Broadcast** was chosen and built out hi-fi (Home, live Fight,
Verdict at 1440 and 390): https://claude.ai/code/artifact/c0965d66-9a6c-4015-a110-0ea93b13c6bf

- **Language**: sports-broadcast grammar — LIVE badge, lower-third captions, results ticker on Home, a stage with two
  fighter "cameras" and the Referee mark between them, a post-game analysis desk for the verdict.
- **Type**: Barlow Condensed (display, uppercase, tight) + Barlow (body); Google Fonts with `Arial Narrow`/system fallbacks.
- **Tokens** (dark default; light re-derived for AA): `--po-bg #0b0d12`, `--po-bg-elev #10131a`, `--po-surface #151923`,
  `--po-surface-2 #1c2230`, `--po-surface-3 #232a3a`, `--po-fg #eef0f4`, `--po-muted #98a1b3`, `--po-border #2a3242`,
  `--po-accent #ffcc33` (on-accent `#0b0d12`), `--po-p1 #3fa9f5`, `--po-p2 #ff4f8b`, `--po-danger #ff5a5a`,
  `--po-success #4ade80`; radii 4/6 px; spacing 4/8/12/16/24/32/48; three breakpoints 40/48/64 rem.
- **Component hierarchy** (confirmed): `MainLayout` [FakeAiBanner · TopBar (RadzenMenu · RadzenProfileMenu · ThemeToggle) ·
  ResultsTicker (Home) · RadzenNotification · RadzenDialog · PageShell] → Home [ChannelCard ×2 · ReplaysGrid
  (RadzenDataGrid) · TopFightersList] · FightLive [PhaseRail · Stage (LiveBadge · DebateTimer · FighterRing ×2 ·
  RefereeMark · LowerThird ×2) · FighterRecordCard ×2 · ControlBar (MicMeter · Mute host · End fight) · CaptionFeed ·
  TalkShareBar] · Verdict [RulingBand · RadzenTabs (Ruling / Evidence / How they talked / Full analysis) · ScoreTile ·
  FighterStatsCard (StatTile) · ClaimsTable (RadzenDataGrid) · HighlightReel · MoodGraph (RadzenChart) ·
  StyleProfilePanel] · shared [ModeBadge · TagMark · StatTile · EmptyState · LoadingRows · FighterTagInput
  (RadzenAutoComplete) · ProfilePicker · HostPersonaPicker].
- WATCH setup/play screens follow the same language (ChannelCard → ProfilePicker; play stage reuses Stage with
  SpeakerCards instead of rings, plus InterjectionBar and TensionMeter).
