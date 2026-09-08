# AGENT.md — how this repository is worked on

Written for whoever (or whatever) picks the work up next. [README.md](README.md) says what the app is;
[SPEC.md](SPEC.md) says what it must do; [tasks/plan.md](tasks/plan.md) says how it was built and in what order.
This file is the operating detail that lives in neither.

## Identity and history

- One branch: `master`. Local git identity `punkouter26 <punkouter26@gmail.com>`.
- **One commit per task**, subject `T47: history, leaderboards and a persona's record`. A commit-msg hook enforces
  the prefix (`T\d\d[a-z]?`, `docs`, `chore` or `fix`), and a pre-commit hook runs `dotnet format
  --verify-no-changes` and the Unit tier.
- Nothing is pushed without being asked for. There is no remote configured.
- `tasks/todo.md` is ticked in the same commit as the work it describes.

## Environments

| | Development | Test | Production |
|---|---|---|---|
| Auth | FakeAuth (`X-Fake-User`) + guest cookie | the same | Entra ID, JWT bearer |
| Storage | Azurite over HTTPS with OAuth, ports 12000/12002 | in-memory repositories | Azure Storage, managed identity |
| Secrets | Key Vault via `az login`, or user-secrets | none needed | Key Vault via managed identity |
| AI | real when a key is present, deterministic fakes when not | fakes, unless `POFIGHTJUDGE_E2E_REAL=1` | **real only** — the fakes refuse to register |
| Logs | console + Seq (5341) | console | Serilog → Application Insights |

`Test` is its own environment name, used by `ApiFactory` and by the E2EUI host. Anything that must never happen in
Production is written as `!environment.IsProduction()`, never as `IsDevelopment()`.

## Configuration

Keys are `PoFightJudge:*` in configuration and `PoFightJudge--*` in Key Vault; `SecretManager` maps one to the
other. Every key has a constant in `Shared/Configuration/ConfigKeys.cs`, and that is where a new one goes — the
host, the tests and the Bicep app settings all read the same name from there.

Feature flags (`FeatureManagement` section): `UseFakeAi`, `DevGuestEnabled`, `HumanInWatch`,
`BrowserSpeechRecognition`, `PreferFastVoice`, `TtsCacheEnabled`, `UseAzurite`, `DiagRequiresAdminInDev`.
`/api/features` exposes the ones the client needs; the "using fake AI" banner reads it.

## Rules that are not negotiable

- **No connection strings.** Storage is `DefaultAzureCredential` and endpoints, everywhere, and the SDK
  constructors that take a connection string are in `BannedSymbols.txt`.
- **No fakes in Production.** Registration is guarded, and a test asserts the guard.
- **`TimeProvider`, never `DateTime.UtcNow`.** Also banned at build time.
- **Radzen for anything a person operates.** A test greps the `.razor` files for raw `<button>`, `<input>`,
  `<select>` and `<textarea>`.
- **No `prefers-color-scheme` block.** The palette is `light-dark()` over one set of tokens, with
  `:root[data-theme]` pinning the explicit choice; a test enforces both, and another checks contrast in both themes.
- **Never weaken a test to make it pass.** A failing test is reported as failing.
- **Ask first** for: any `az` write (Key Vault, app registration, deployment, role assignment), creating the GitHub
  repository or its OIDC credential, every `git push`, deleting everybody's matches or profiles, moving off the F1
  plan or changing a model tier, and adding a NuGet package that is not already in `Directory.Packages.props`.

## Where things live

`Features/{Name}/` holds everything about one thing — endpoints, domain, storage, options — and slices talk to each
other through `Shared` contracts and repositories, not through each other's services. The seams worth knowing:

- `IGeminiText` is the transport; `IWatchAi` is the prompting. The WATCH fake sits at the second seam, so the fake
  understands the game rather than pretending to be an HTTP response.
- `ILiveSocket` is the WebSocket; `IGeminiLiveClient` is the protocol; `DebateSession` is the rules with no I/O at
  all. The scripted host replaces the client, so the state machine under test is the real one.
- Records are computed on read by `FighterStatsBuilder`, `ProfileStatsBuilder` and `StyleProfileBuilder` from
  result rows. Nothing anywhere increments a counter.

## Deviations from the plan

Each was a deliberate decision made while building; the commit that made it says why.

| Task | Plan said | What was done | Why |
|---|---|---|---|
| T44, T47 | `FormSparklines` as a `RadzenChart` line | drawn by hand as an SVG polyline | eight numbers in a table cell; a chart that measures the DOM to lay itself out is the wrong tool, and it cannot render under bunit |
| T47 | five client files | plus `Components/Board.razor` and a describer in `Stats/StatFormat.cs` | the two leaderboards are one component rather than the same grid twice, and every measured field is described in one place |
| T48 | client files only | plus `GET /api/fighters/roster` (`FighterEndpoints`, `ApiRoutes`) | a page cannot show a record the API does not return, and one read beats one read per person |
| T49 | registration in `index.html` | `wwwroot/js/pwa.js` | the app's own CSP is `script-src 'self'`, so the inline script was blocked and nothing registered — caught by `PwaTests` |
| T50 | "workflow YAML parsed by a test" | `InfraTests` reads the workflow as text | a YAML parser is a new package, which is ask-first |
| T39 | one judge call assessing both players | one call per player | the live endpoint refuses a response schema carrying two full assessments, and `$ref` with it |
| T53 | one `Shared/Models` file per task | three (`LiveModels`, `RecordModels`, `ProfileModels`) | the seat travels from the 2P setup request, through the fighter row, to the persona the cast page shows; one contract per hop |

## Still outstanding

- **Nothing has been deployed.** The templates compile and the workflow is written; the repository, the OIDC
  registration and the first release are yours to approve.
- The **transcription fallback** has never run against the real service: both real fights had usable live
  captions, which the pipeline prefers.

The real-key measurements for CP4, CP5 and CP6 are done (2026-09-07) and written up in `docs/CP4.md`,
`docs/CP5.md`, `docs/CP6.md` and `docs/VERIFICATION.md`. They were worth running: nine defects came out of them,
seven of which no test on the stand-ins could have seen.

## Costs

The four test tiers spend nothing: every AI seam has a deterministic stand-in, and the tests that talk to Google
skip unless a variable says otherwise — `GEMINI_API_KEY` for the live smoke, `POFIGHTJUDGE_E2E_REAL=1` for a
browser fight against the real service, `POFIGHTJUDGE_SMOKE_URL` for the deployed-site check. Those are worth
running when the wire format or a schema changes, and not otherwise.
