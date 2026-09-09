# PoFightJudge — TODO (one commit per task; tick when merged to master)

## Pre-build
- [x] Library selection (top-50 → picks → top-10 examples → tasks updated)
- [x] /design: 10 concepts → pick (04 Broadcast) → component hierarchy confirmed

## Phase A — Foundation
- [x] T01 Solution skeleton
- [x] T02 Shared contracts
- [x] T03 Api host bootstrap + secrets + degraded mode
- [x] T04 Diagnostics endpoints
- [x] T05 Auth (server) — done before T04 (diag needs the auth services)
- [x] T06 Storage clients + Azurite credential
- [x] T07 Client bootstrap
- [x] T08 Design tokens, theme, shell
- [x] T09 Client auth (E2EUI AppFixture created here; T11 extends it)
- [x] T10 Home, Health, NotFound, banner, empty state (banner shipped in T08)
- [x] T11 Test infrastructure  ← CP1 reached 2026-09-06: shell + guest login + theme + 390px (E2EUI 3), /api/health Healthy vs real Azurite (OAuth+HTTPS), all 5 test projects run

## Phase B — Profiles
- [x] T12 Profile domain + repository
- [x] T13 Profile endpoints, faces, seeding
- [x] T14 Profiles UI  ← CP2 reached 2026-09-06: Profiles page (cast by role, RadzenDataList cards, DialogService editor with the shared validator, portrait upload, delete confirm, Load default cast); E2EUI create/delete + seed with 512px portraits

## Phase C — AI core + Voice
- [x] T15 Gemini HTTP core
- [x] T16 Gemini text client + fakes + latency tracker
- [x] T17 Profile generation  ← CP2.5
- [x] T18 TTS core + Gemini TTS + fake
- [x] T19 Voice providers + routing
- [x] T20 TTS cache + preview line + playback interop
- [x] T21 Transcription  ← CP3 (fakes verified; real-key smoke pending a key)

## Phase D — WATCH
- [x] T22 WATCH domain
- [x] T23 Prompts + IWatchAi
- [x] T24 Match storage (both modes)
- [x] T25 WATCH endpoints
- [x] T26 WATCH setup UI
- [x] T27 WATCH play UI
- [x] T28 The spoken human turn  (Radzen's RadzenSpeechToTextButton replaced the planned speech.js + SpeechRecognitionInterop)
- [x] T29 WATCH end-to-end hardening  ← CP4 (fakes green; the real-key timing run is carried to T51)

## Phase E — FIGHT live
- [x] T30 Fighter domain + repositories  (pulled ahead of T25: a person in WATCH needs a fighter result)
- [x] T31 Gemini Live client  (LiveOptions replaces the planned GeminiOptions; model ids already live in GeminiModelOptions)
- [x] T32 Debate state machine + host personas
- [x] T33 Orchestrator  (IAnalysisQueue is named IAnalysisIntake: the suffix is reserved for collections)
- [x] T34 Registry, hub, fight endpoints
- [x] T35 Browser audio  (PoLive lives in live-audio.js, apart from the watch playback in audio.js)
- [x] T36 Fight setup UI
- [x] T37 Fight live UI
- [x] T38 Live UI 2 + FIGHT E2E  ← CP5 (real-key Live smoke)

## Phase F — Analysis
- [x] T39 Analysis clients + schema  (PollBackoff lands here, since both clients poll)
- [x] T40 Metrics, mapping, highlights
- [x] T41 Pipeline + style snapshot  (the fakes and DI came with it, so T42 is only the endpoints)
- [x] T42 Analysis endpoints, fakes, DI
- [x] T41b Opus recordings (Concentus)
- [x] T43 Verdict UI 1  (StatFormat lands here, since the tiles need it)
- [x] T44 Verdict UI 2 + E2E  ← CP6

## Phase G — Records + Fighter style
- [x] T45 Stats + style builders
- [x] T46 Records endpoints
- [x] T47 Records UI
- [x] T48 Fighters UI + host digest  ← CP7

## Phase H — PWA, infra, docs, verification
- [x] T49 PWA
- [x] T50 Infra as code + CI
- [x] T51 Scripts, docs, secret seeding (ask-first)
- [x] T52 Verify, review, simplify, evidence  ← CP8

## Phase I — From manual testing
- [x] T53 Fighter personas: a 2P fighter becomes a CPU/1P persona, read from their fights (seat chosen at 2P setup)

## Phase J — From the feature review (2026-09-08)
- [x] T54 WATCH replay: `GET /api/matches/{id}/turns` and a replay page for stored round audio
- [x] T55 Dev env signs in with Microsoft or guest: BFF cookie scheme (OIDC code flow) registered in Development when `PoFightJudge:Auth:AllowDevEntra` is on and the dev Entra client id is in KV; Login page shows both buttons in Dev, MSAL only in Prod, FakeAuth only in Test. Dev Entra registration: `po-fightjudge-dev` (appId `af9ec924-980b-4bda-b5fe-afebcfc1b35a`, audience `AzureADandPersonalMicrosoftAccount`, redirect URIs `https://localhost:5001/signin-oidc` + 5000 + 127.0.0.1:5001 + 127.0.0.1:5000). Secrets: `PoFightJudge--AzureAd--ClientId-Dev` + `--TenantId-Dev` in `kv-poshared`.
- [x] T55 Rate limits the client can see: 429 + `Retry-After` becomes a toast and a countdown
- [x] T56 Run it back: rematch from a finished match, and setup remembers the last pair
- [x] T57 Share a verdict: an opt-in, revocable read-only link
- [x] T58 Head to head: two combatants side by side, from the rivalry data already computed
- [x] T59 Take the clip: download a highlight
- [x] T60 Keyboard: a command palette and the shortcuts HotKeys2 was added for
- [x] T61 History that scales: search, filters and server-side paging
- [x] T62 An error that does not lose the page: ErrorBoundary and a degraded banner
- [x] T63 Before the bell: a microphone check and a device to pick (the reconnect banner already existed)
- [x] T64 The round audio is archived: the verdict carries each line's clip, so a replay has something to play

## Phase K — Sound and light (2026-09-08)
- [x] T65 A sound bus that makes its own noise: synthesized SFX, one master switch, ducking over the voices
- [x] T66 The slap lands and the bell rings: WATCH gets its shockwave, its round chrome and a stage that hears the voice
- [x] T67 The live stage as a broadcast: a shader backdrop, glass over it, phase stingers and a mic that shows itself
- [x] T68 The reveal: particles, a counting scoreboard and a courtroom finish on the verdict

## Phase L — UI/UX review (2026-09-08)
- [x] T69 Tokens that resolve: eight dead `var(--po-*)` references fixed, and a guard so the next one fails a test
- [x] T70 The viewport a phone actually has: `dvh` and the safe-area insets
- [x] T71 Touch targets that are the size the stylesheet claims
- [x] T72 Headings in order: a card does not decide the document outline
- [x] T73 Nothing jumps after first paint: the banners hold their slot
- [x] T74 A grid a phone can read: cards below 40rem, columns above it
- [x] T75 A live fight that renders what changed, not the page
- [x] T76 Setup as three steps, so each one is a screen
- [x] T77 Radzen does the layout: a sidebar on mobile, stacks instead of hand-rolled grid
- [x] T78 Every route measured at both viewports, on every run

## Phase M — AI pipeline audit (2026-09-09)
- [x] T79 WATCH reads the streams it already serves: the line as it is written, the audio clause by clause
- [x] T80 The next line is given a voice while this one is still being spoken
- [x] T81 The judge caches its shared prefix by name and assesses both players at once
- [x] T82 What the judge spends is on the ledger, so the cached share can be read rather than assumed
- [x] T83 The recording goes to the model without being copied through memory four times
- [x] T84 An analysis the host died halfway through is picked back up on the next start
- [x] T85 The browser transcribes what it hears, so a fight with thin captions stops paying to diarize
- [x] T86 A local model writes the dialogue in development, behind the same seam Gemini sits behind
- [x] T87 A fight is read by Azure when it is configured, not by the model that answers empty and bills anyway
- [x] T88 Fights remember each other: every fight is embedded, and a history is searchable by meaning

## Phase N — A profile that grows (2026-09-09)
- [x] T89 A persona is written from everything they have ever said: every debate keeps the words, both engines
- [x] T90 A 1P debate rewrites the persona too: the judge's per-person read becomes optional, the write is queued
- [x] T91 A spoken turn that the browser cannot read back says which step failed, and stops throwing the clip away
- [x] T92 A watch turn is captured as PCM off the graph, the way a fight already is: no container, so nothing to decode
- [x] T93 A persona read from debates says so, rather than claiming a 2P fight it may never have had

## Phase O — Prune (2026-09-09)
- [x] T94 The browser recogniser we stopped using: `ListenInterop`, `listen.js` and `HeardTranscript` deleted
- [x] T95 A search nothing called: the fight-embedding chain and `/api/matches/search` deleted
- [x] T96 Keys nobody presses: the command palette, the shortcut sheet and `HotKeys2` deleted
- [x] T97 One voice, one transcriber, one text model: the provider fan-out collapsed onto Gemini and the fakes
- [x] T98 Three toggles, not nine flags: `Microsoft.FeatureManagement` gone, the rest answered where they are asked
- [x] T99 One door per environment: the dev Entra OIDC scheme deleted, so sign-in is MSAL or guest
- [x] T100 The standings live under the roster they rank: three stats pages deleted, nav down to seven
- [x] T101 The suites capped: 96 Unit, 26 Integration, 24 API, 24 UI — one test per decision, not per branch
- [x] T102 Six documents, not thirteen: the checkpoint reports, the build plan and an unbuilt design deleted

## Phase P — The spoken turn (2026-09-09)
- [x] T103 A turn you only have to talk into: the microphone opens, a two-second pause ends it, and the line sends itself
- [x] T104 The spoken turn gets its own transcriber: one model id served two Gemini surfaces, and the one the diarizer needs returns an empty turn on generateContent
- [x] T105 A profile that counts as well as reads: twelve career word stats computed from everything a person has ever said
- [x] T106 The persona editor halved: 28 fields become 14, and the nine lines of "50 (neutral)" stop being sent to the model
- [x] T107 Cloned voices come back: Fish Audio restored ahead of Gemini, and the editor asks for a voice id instead of a voice
