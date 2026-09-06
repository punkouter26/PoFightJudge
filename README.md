# PoMarriedFight

One app, two ways to have a married-couple fight and get judged: **WATCH** (AI husband vs AI wife, three rounds each,
Gemini judge) and **FIGHT** (two real people, one mic, a Gemini Live referee, then a full speech analysis).
A merge of [PoMarriedLife](https://github.com/punkouter26/PoMarriedLife) and
[PoArgueJudge](https://github.com/punkouter26/PoArgueJudge) into one fresh solution.

Governance: [SPEC.md](SPEC.md) · [CAPABILITY-MAP.md](CAPABILITY-MAP.md) · [tasks/plan.md](tasks/plan.md) ·
[tasks/todo.md](tasks/todo.md) · [NET_RULES.md](NET_RULES.md).

## Run (local)

```powershell
./SCRIPTS/azurite.ps1                       # Azurite (OAuth + HTTPS on 12000/12002) + Seq (5341)
az login                                    # DefaultAzureCredential reads kv-poshared (PoMarriedFight--*) and Storage
dotnet run --project src/PoMarriedFight.Api # https://localhost:5001
```

Without a Gemini key the app runs with deterministic fakes and shows a "USING FAKE AI" banner (Development only).

## Test

```powershell
dotnet test tests/PoMarriedFight.Unit          # no Docker
dotnet test tests/PoMarriedFight.Integration   # Testcontainers.Azurite (Docker)
dotnet test tests/PoMarriedFight.E2EAPI        # in-process API with fakes
dotnet test tests/PoMarriedFight.E2EUI         # Playwright Chromium + fake microphone
```

Full setup, secrets, deploy and infra: see SPEC.md §3 and (from T51) `SCRIPTS/setup.ps1`.
