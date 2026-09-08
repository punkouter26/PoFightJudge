# CLAUDE.md

Read [AGENT.md](AGENT.md) first — it carries the rules, the environment matrix and the deviations table. This file
is the short version for a working session.

## Commands

```powershell
dotnet build PoFightJudge.slnx -c Release       # warnings are errors; this must be clean
dotnet test PoFightJudge.slnx                   # Unit, Integration (Docker), E2EAPI, E2EUI (Playwright)
dotnet test tests/PoFightJudge.Unit             # the fast tier, and what the pre-commit hook runs
dotnet format PoFightJudge.slnx                 # the hook verifies this, so run it before committing
./SCRIPTS/setup.ps1                               # clean machine → running app
./SCRIPTS/azurite.ps1                             # just the emulator (and -Down -Wipe to throw it away)
dotnet run --project src/PoFightJudge.Api       # https://localhost:5001
```

## Working rules

- One task, one commit, subject `T##: what it does`. Tick `tasks/todo.md` in the same commit.
- Tests first, then the smallest code that passes them. Never delete, skip or weaken a failing test — if it is
  wrong, say so and fix it deliberately; if it is right, the code is wrong.
- A task edits the files it lists. Going outside that is allowed when it is necessary, but it is said out loud in
  the commit message and in AGENT.md's deviations table.
- Report faithfully. A failing test is reported as failing, with the output.
- Never invent an API's shape. Check the documentation or ask.

## Ask before

Any `az` write (Key Vault, app registration, deployment, role assignment) · creating the GitHub repository or its
OIDC credential · **every** `git push` · deleting everybody's matches or profiles · moving off the F1 plan or
changing a model tier · adding a NuGet package that is not already in `Directory.Packages.props`.

## Things that will bite

- `Arg.Any<MatchId>()` and friends throw: an uninitialised Vogen value object is not a valid argument matcher.
  Match on the real id.
- bunit disposes its container synchronously, so a test whose component owns an `IAsyncDisposable` needs
  `IAsyncLifetime`. And `FakeTimeProvider.Advance` must go through `cut.InvokeAsync`, or it deadlocks.
- A click that opens a Radzen confirm does not return until the dialog is answered. Capture the task, answer the
  dialog, then await it.
- `RadzenDataGrid` is bound to a list instance: mutate it and nothing re-renders. Assign a new list.
- `RadzenTabs` renders only the selected tab unless `RenderMode="TabRenderMode.Client"`.
- `RadzenChart` measures the DOM and throws under bunit. Test the decision, not the drawing.
- The analyzers are strict (`AnalysisLevel=latest-all`, Meziantou, VS.Threading, banned APIs). CA1711, MA0046,
  CA1849, VSTHRD200, MA0002 and MA0016 all bite regularly; read the rule before working around it.
