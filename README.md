# PoFightJudge

One app, two ways to have a married-couple fight and get judged.

**WATCH** — pick a husband and a wife from your cast of personas, give them something to argue about, and they argue
it out three rounds each in their own synthesised voices while a judge scores it. You can take one of the two seats
yourself.

**FIGHT** — you two, one microphone. A Gemini Live host introduces you, interrupts, cross-examines and rules, and
afterwards the recording is transcribed, diarised, measured and judged: claims, fallacies, traits, thirty
measurements each, and the moments worth listening to again.

A merge of [PoMarriedLife](https://github.com/punkouter26/PoMarriedLife) and
[PoArgueJudge](https://github.com/punkouter26/PoArgueJudge) into one fresh solution.

Governance: [SPEC.md](SPEC.md) is what it must do · [AGENT.md](AGENT.md) is how it is worked on ·
[NET_RULES.md](NET_RULES.md) is the standard every `Po*` solution follows · [tasks/todo.md](tasks/todo.md) is the
task log.

## Run it

On a clean clone, one script does the lot — prerequisites, the dev certificate, Azurite, Seq, `az login`, build and
the whole suite:

```powershell
./SCRIPTS/setup.ps1          # add -Run to start the app when it finishes
```

Or by hand:

```powershell
./SCRIPTS/azurite.ps1                        # Azurite (OAuth + HTTPS on 12000/12002) + Seq (5341)
az login                                     # DefaultAzureCredential reads kv-poshared and Storage with this
dotnet run --project src/PoFightJudge.Api  # https://localhost:5001
```

**With no Gemini key it still works.** Every AI seam has a deterministic stand-in — the rounds, the voices, the live
host, the transcription and the judge — so a whole watch and a whole fight run end to end offline, and the app says
so with a banner. The fakes are refused outright in Production.

## Test

```powershell
dotnet test tests/PoFightJudge.Unit          # no Docker, no network
dotnet test tests/PoFightJudge.Integration   # Testcontainers.Azurite (Docker)
dotnet test tests/PoFightJudge.E2EAPI        # the real API in process, with the fakes
dotnet test tests/PoFightJudge.E2EUI         # Playwright Chromium with a fake microphone playing a real argument
```

`dotnet test PoFightJudge.slnx` runs all four. A pre-commit hook runs `dotnet format --verify-no-changes` and the
Unit tier.

## How it is put together

```
src/PoFightJudge.Shared   contracts, route table, config keys, value objects — the API and the client share one truth
src/PoFightJudge.Api      minimal API, vertical slices under Features/, SignalR hub for the live fight
src/PoFightJudge.Client   Blazor WebAssembly, Radzen, one set of design tokens
infra/                      Bicep: resource group, storage, F1 Linux plan, web app, role assignments
```

The slices: `Profiles` (the cast) · `Watch` (the round loop and its judge) · `Fight` + `Live` (the show, the state
machine and the Gemini socket) · `Analysis` (transcribe, measure, judge, highlight) · `Fighters` (people, and how
they argue) · `Records` (matches, results, history, leaderboards) · `Auth` · `Diagnostics` · `Storage` · `Voice`.

Two rules run through all of it:

- **Storage is identity-only.** Endpoints and `DefaultAzureCredential`, never a connection string — the
  constructors that take one are a build error, and the storage account has shared-key access switched off.
- **Records are computed on read.** Nothing is accumulated on a counter, so re-reading a fight corrects its record
  instead of counting it twice, and deleting one removes it everywhere.

## Secrets

Configuration keys are `PoFightJudge:*`; the same values live in Key Vault as `PoFightJudge--*`. Nothing is ever
committed.

```powershell
./SCRIPTS/seed-secrets.ps1          # prints what it would copy into kv-poshared; writes nothing
./SCRIPTS/seed-secrets.ps1 -Apply   # carries it out
dotnet user-secrets set "PoFightJudge:GeminiApiKey" <value> --project src/PoFightJudge.Api   # or just locally
```

## Deploy

`infra/main.bicep` creates everything; `.github/workflows/deploy.yml` builds, tests, publishes and deploys on a push
to `master`, signing in with a federated credential rather than a stored secret. The plan is free-tier: one F1 Linux
plan, which means one instance — and a live fight lives in the API process with an in-process hub, so a second
instance would not see it anyway.

Nothing deploys itself. Creating the repository, the OIDC registration and the first release are deliberate steps.
