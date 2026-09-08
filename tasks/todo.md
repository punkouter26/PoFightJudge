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
- [x] T29 WATCH end-to-end hardening  ← CP4 (fakes green; the real-key timing run is carried to T51, see docs/CP4.md)

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
- [x] T55 Rate limits the client can see: 429 + `Retry-After` becomes a toast and a countdown
- [x] T56 Run it back: rematch from a finished match, and setup remembers the last pair
- [ ] T57 Share a verdict: an opt-in, revocable read-only link
- [ ] T58 Head to head: two combatants side by side, from the rivalry data already computed
- [ ] T59 Take the clip: download a highlight
- [ ] T60 Keyboard: a command palette and the shortcuts HotKeys2 was added for
- [ ] T61 History that scales: search, filters and server-side paging
- [ ] T62 An error that does not lose the page: ErrorBoundary and a degraded banner
- [ ] T63 Before the bell: a microphone check, and a reconnect the fighters can see
