# T56 — one cast, not two

> Status: **design**, awaiting approval. No code yet.

## The problem

Today there are two domain entities that both answer "who is on the card":

- **`Profile`** (`src/PoFightJudge.Api/Features/Profiles/Profile.cs`) — the AI persona. Has personality sliders, TTS settings, a portrait, a Fish voice clone, a philosophy, common arguments. Lives in the `profiles` Table. Read by the WATCH prompt builder.
- **`Fighter`** (`src/PoFightJudge.Api/Features/Fighters/Fighter.cs`) — a real person. Has tag, display name, role seat, created/last-seen. Lives in the `fighters` Table. Read by `/api/fighters`, `/api/fighters/roster`, and the leaderboard.

The split exists on purpose (see `Profile.cs:117-130` and the human-opponent directive in `RoundPromptBuilder.cs:33-39`): the AI spouse argues against whatever a human says into the mic, not against an invented personality. Today, the human's record carries nothing personality-shaped — a thin row that says "this tag argued at this time" and that's it.

The user has decided that the human's row should grow over time. Each match they play, the system reads their spoken lines, asks an LLM to infer a `CastMember` patch (likes, dislikes, sliders, philosophy), and merges the patch into their row. The next match they play, the AI spouse sees the inferred patch as additional context — what they've said this match still wins, but the AI is primed to expect the argument style the human has settled into across their last N fights.

The risk is real: a single entity invites future contributors to invent personalities for human rows by hand, or to let the inferred patch *replace* the spoken turn. The doc has to spell out the boundary so the code carries it.

## The shape

One C# record, one Table. The union of today's two property sets, with one boolean that gates which side of the union is in use:

```csharp
public sealed record CastMember
{
    public CastMemberId Id { get; init; }                  // the upper-cased initials (≤3 chars, normalized)

    // Identity (populated by both sides)
    public string? DisplayName { get; init; }              // fighter display name, optional for personas
    public ProfileRole Role { get; init; }                 // husband or wife seat
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public bool IsHuman { get; init; }                     // gates everything below

    // Authored-persona fields
    //   IsHuman == false: populated by the seed/editor.
    //   IsHuman == true:  populated by CastInferenceService over time, starting empty.
    //   The prompt builder reads them in BOTH cases; the difference is who fills them.
    public string? Name { get; init; }
    public int? Age { get; init; }
    public string? Occupation { get; init; }
    public string Likes { get; init; } = string.Empty;
    public string Dislikes { get; init; } = string.Empty;
    public bool IsIntrovert { get; init; }
    public bool IsStubborn { get; init; }
    public bool IsSpontaneous { get; init; }
    public bool IsSarcastic { get; init; }
    public bool IsWorkaholic { get; init; }
    public bool IsPackRat { get; init; }
    public LoveLanguage? LoveLanguage { get; init; }
    public AttachmentStyle? AttachmentStyle { get; init; }
    public StressResponse? StressResponse { get; init; }
    public int LogicVsEmotion { get; init; } = 50;
    public int Punctuality { get; init; } = 50;
    public int InLawAffinity { get; init; } = 50;
    public int ScreenTime { get; init; } = 50;
    public int Jealousy { get; init; } = 50;
    public int IsMessy { get; init; } = 50;
    public int SpendsMoneyFreely { get; init; } = 50;
    public int HoldsGrudges { get; init; } = 50;
    public int Patience { get; init; } = 50;
    public string CommonArguments { get; init; } = string.Empty;
    public string Philosophy { get; init; } = string.Empty;
    public string? FacePic { get; init; }
    public TtsSettings TtsSettings { get; init; } = TtsSettings.Default(ProfileRole.Husband);
    public bool FromFights { get; init; }                  // set by the pipeline after a 2P read

    // Computed-on-read (rolled up from result rows; not stored)
    //   Wins, Losses, Draws, Fights, Form, Style — same derivation as today.
}
```

The invariant — enforced in code, with a unit test:

> **`IsHuman == true` rows MUST NOT be written by humans or by the editor.** The seed/editor routes can only write `IsHuman == false`. The inference service is the only writer of `IsHuman == true` rows. The repository enforces this by splitting the surface: `ICastRepository.UpsertPersonaAsync` throws on `IsHuman == true`; `ICastRepository.ApplyInferencePatchAsync` throws on `IsHuman == false`. A unit test pins both halves.

Two reasons for the invariant:

1. The prompt builder's "argue against what was just said" behaviour stays intact because the inference is *derived from* the spoken lines, never replacing them. The patch is a layer on top, not a substitute.
2. The cast page (`/profiles`) keeps filtering to `IsHuman == false` server-side, so humans never surface as opponents in the picker even though they have populated rows.

## Inference

After every match a human participates in, `CastInferenceService` runs once per human side, **after** the result rows are written. It reads the human's own lines from the match transcript and asks the configured Gemini model for a patch shaped like a `CastMember`:

```json
{
  "likes":         "string — short list",
  "dislikes":      "string — short list",
  "commonArguments": "string — short paragraph",
  "philosophy":    "string — one sentence",
  "logicVsEmotion": 0,
  "isStubborn":    true
}
```

The patch is the **delta** — the LLM returns only the fields it has evidence to fill, not a whole row. The merge rules:

| Field type | Merge |
|---|---|
| List fields (`Likes`, `Dislikes`, `CommonArguments`) | Append new tokens, deduplicate, cap at the seed row's length. |
| Boolean traits (`IsIntrovert`, …) | New patch wins. LLM picks the most consistent value across the lines. |
| Sliders (`LogicVsEmotion`, `Patience`, …) | **Running average weighted by line count**: `new = (old × oldLines + new × newLines) / (oldLines + newLines)`. The first match moves the slider a lot; the tenth barely nudges it. |
| Free-text (`Philosophy`) | Replace. |
| `FacePic` | Never set by inference. |

The line-count weight is what makes "more matches = finer tune" actually happen — a single noisy fight can't rewrite a confident slider.

Two gating rules:

- **Minimum lines**: the inference runs only when the match has at least N spoken lines from the human (default N = 5). One-line matches are skipped; the signal is too thin. The threshold is a constant in `CastInferenceOptions`, tunable.
- **Schema version**: the patch carries a `schemaVersion` integer. An old inference result cannot clobber a newer one. The merge function reads `(tag, schemaVersion)` and refuses to apply a patch older than what's already on the row.

The inference runs **after** the result rows are written, not inline. A failure to infer doesn't fail the match — the match is over by then. The inference is a hosted background job that drains a queue (`TableName: inferencequeue`, keyed by `matchId|sideId`) with retries and idempotency: the patch key includes `(matchId, sideId, schemaVersion)`, so a retry applies the same patch to the same row state.

The inference also writes a `MatchInferenceDto` audit row to a separate Table (`cast_inference_log`, keyed by `matchId|sideId`) with: the prompt the LLM was given, the lines it saw, the patch it returned, the merge result. The audit log is what makes "why does this row say this?" debuggable — without it, the LLM is a black box writing to a row.

A unit test pins the merge:

> *Given a row with `Patience = 50` accumulated over 12 lines and a new patch saying `Patience = 80` over 8 lines, the merged row has `Patience ≈ 62` (not 80, not 50, not 65).*

## Prompt builder

`RoundPromptBuilder` is the surface that needs to change. The current logic:

- Persona side (`IsHuman == false`): inject the full authored CastMember into the prompt.
- Human side (`IsHuman == true`): inject the human-opponent directive; no authored fields.

The new logic:

- Persona side: unchanged.
- Human side: inject **two** things — the human-opponent directive (unchanged), and an *inferred-context block* built from the human's CastMember row:

  > *"Across N previous matches, this human has shown: Likes=…, Dislikes=…, Philosophy=…, IsStubborn=true, Patience=42. Use this to anticipate their argument style. Their actual spoken turn is what they mean; this is the shape they've tended to take when they mean it."*

The order in the prompt is: human-opponent directive first (so the AI is told "argue against what they said"), then inferred-context (so the AI has priors), then the spouse's authored profile, then the actual round transcript. The unit test that pinned the old directive's order is extended: it now asserts the inferred block follows the directive but precedes the transcript.

Two key tests pin the AI behavior:

1. **The patch never overrides the spoken turn.** A human with `Philosophy = "I always win"` whose most recent line is "I lost, you're right" still has the AI argue against the spoken line. The test pins this by checking that the round transcript is the last block before the speaker-attribution markers.
2. **Empty inference is silent.** A human with no inferred data (zero prior matches) produces a prompt that is byte-identical to today's. The test pins this with a snapshot.

## Storage

One Table, one partition key, one row per cast member:

```
TableName:    castmembers
PartitionKey: "cast"              // single partition, queryable as a list
RowKey:       upper-cased initials (≤3 chars)
```

Today:

- `profiles` Table: `PartitionKey = "profile"`, `RowKey = Id.Value`.
- `fighters` Table: `PartitionKey = "fighter"`, `RowKey = Id.Value`.

The migration is mechanical: read every row from both Tables, write to `castmembers`, delete the source rows. The mapping is one-to-one because both keys are the upper-cased initials.

Migration cost:

- **Dev Azurite**: drop the Azurite volume, re-seed (the `seed-secrets`/`azurite` flow already does this on a wipe).
- **Production**: one-shot Azure Data Tables copy. The deployment pipeline gets a new `MIGRATE_CAST=1` step that reads from `profiles` and `fighters`, writes to `castmembers`, deletes the source rows. Guarded behind a feature flag so a partial migration doesn't break reads.

## Repositories

Two repos merge into one: `ICastRepository` in `Features/Cast/`.

```csharp
public interface ICastRepository
{
    Task<CastMember?> GetAsync(CastMemberId id, CancellationToken ct);
    Task<IReadOnlyList<CastMember>> ListAsync(bool? humans, CancellationToken ct);   // null = both
    Task UpsertAsync(CastMember member, CancellationToken ct);                       // throws on invariant violation
    Task DeleteAsync(CastMemberId id, CancellationToken ct);
    // Personas-only convenience
    Task<IReadOnlyList<CastMember>> ListPersonasAsync(CancellationToken ct);
    // Humans-only convenience
    Task EnsureHumanAsync(CastMemberId id, DateTimeOffset now, ProfileRole role, string? displayName, CancellationToken ct);
}
```

`EnsureHumanAsync` is the equivalent of today's `IFighterRepository.EnsureAsync`: create-on-first-sight, stamp last-seen, never moves backwards. The display-name rename moves to a separate method `RenameAsync` (same shape as today).

## Endpoints

`/api/profiles/*` and `/api/fighters/*` both stay. Their implementations swap to `ICastRepository`. The route contracts don't change:

| Endpoint | Behaviour |
|---|---|
| `GET /api/profiles` | `ListPersonasAsync()` |
| `POST /api/profiles` | `UpsertAsync` (validator enforces non-human) |
| `GET /api/profiles/{id}` | `GetAsync` — null when the id is human |
| `PATCH /api/profiles/{id}` | `UpsertAsync` — rejects humans with 409 |
| `DELETE /api/profiles/{id}` | `DeleteAsync` — rejects humans |
| `POST /api/profiles/{id}/face` | unchanged |
| `GET /api/fighters/roster` | `ListAsync(humans: true)` |
| `GET /api/fighters/{tag}` | `GetAsync` — null when not human |
| `PATCH /api/fighters/{tag}/rename` | `RenameAsync` (humans only) |
| `DELETE /api/fighters/{tag}` | `DeleteAsync` (humans only) |

`/api/profiles/{id}` returning null for a human is the key behavioural change: today's "every id is a profile, fetch it" becomes "fetch it; it's null if the id is human." The frontend already handles null cleanly (the cast page is filtered to personas before fetch).

## Result rows

Today: `WatchResultDto` joins on persona initials; `FighterResultDto` joins on fighter tag. After T56 both join on `CastMemberId` (same value). The two result Tables stay separate (different partitions, different access patterns, different shapes):

- `watches` — persona results
- `fighter_results` — human results

The DTO row key changes from "tag string" to `CastMemberId`, but the wire shape stays the same.

## UI

- **`/profiles` page** — unchanged; filters to `IsHuman == false` server-side.
- **`/fighters` page** — unchanged; filters to `IsHuman == true` server-side. Today it shows record + style; tomorrow those columns derive from the same `CastMember` row.
- **Cast picker at setup** — unchanged; the existing role filter is server-side.

The only visible change: nothing. The two pages keep their identities; the data behind them is one row.

## Files that move

```
src/PoFightJudge.Api/Features/Profiles/
  Profile.cs                      ──┐
  ProfileRepository.cs            ──┤
  ProfileMapping.cs               ──┼──►  src/PoFightJudge.Api/Features/Cast/
  ProfileEndpoints.cs             ──┤     CastMember.cs
  ProfileGenerator.cs             ──┤     CastRepository.cs
  ProfileImageService.cs          ──┤     CastMapping.cs
  Seeding/SeedEndpoints.cs        ──┤     CastEndpoints.cs
  Seeding/SeedProfiles.cs         ──┘     Seeding/SeedCast.cs
src/PoFightJudge.Api/Features/Fighters/
  Fighter.cs                      ──┐
  FighterRepository.cs            ──┤──►  src/PoFightJudge.Api/Features/Cast/
  FighterPersonaWriter.cs         ──┤     CastMember.Fighter.cs (the human-side methods)
  FighterResultRepository.cs      ──┘     CastMember.Human.cs (the EnsureHumanAsync, RenameAsync)
src/PoFightJudge.Api/Features/Watch/
  WatchEndpoints.cs               ──► uses ICastRepository.GetAsync + IsHuman branch
src/PoFightJudge.Api/Features/Records/
  WatchResultRepository.cs        ──► unchanged
  FighterResultRepository.cs      ──► unchanged
src/PoFightJudge.Client/
  Pages/Profiles.razor            ──► unchanged (server filter is enough)
  Pages/Fighters.razor            ──► unchanged
  Services/ApiClient.cs           ──► unchanged (DTOs keep their shape)
```

The mapping is mechanical. The risk is in the **wiring**, not the moves.

## Decisions worth keeping

1. **Humans' persona fields are populated only by inference, never by hand.** The repository splits the surface (`UpsertPersonaAsync` rejects `IsHuman == true`; `ApplyInferencePatchAsync` rejects `IsHuman == false`) so a future contributor who tries to edit a human's row through the persona endpoint gets a 409.
2. **The patch is derived from spoken lines, never replaces them.** The prompt builder test pins the round transcript as the last block; the inference cannot write a `Philosophy` that the AI treats as authoritative.
3. **Two result Tables stay separate.** They have different shapes and different access patterns; merging them would be churn for no win.
4. **The cast page and the fighters page stay separate.** Same data row, different lists, different filters. The user still navigates to `/profiles` for the AI cast and `/fighters` for the people who've argued.
5. **`Profile.CreateHuman` becomes `CastMember.CreateHuman(...)`** that returns an empty-patch row (no AI fields populated, no portrait, default TTS settings). Inference fills it from there.
6. **Migration is reversible until the production deploy ships.** Dev Azurite is wiped and reseeded; the production migration is a guarded one-shot. If it fails, the old Tables still exist (they're not deleted by the new code, just abandoned).
7. **`Likes`, `Dislikes`, `CommonArguments` stay `string`, not `IReadOnlyList<string>`.** Today's seed format is comma-separated prose and the editor is a textarea. The merge rule "append + dedup" splits on comma, dedupes case-insensitively, and caps at 256 characters. The merge math (line-count-weighted average) is unchanged.

## Decisions made during this session

The two open decisions were resolved before this commit:

1. **`Likes` / `Dislikes` / `CommonArguments` stay strings, not lists.** The seed format and editor both work in prose; converting to lists is a separate UX change for another day. The merge treats them as delimited strings.
2. **Inference runs as a hosted background job, not inline.** The match completes; the patch is enqueued and the inference drains a queue. A failure to infer doesn't fail the match, and the prompt builder handles "no inferred data yet" the same as today's "no row exists" — silent fallback. The user-facing effect: after the first match, the inferred row may take a few seconds to populate, but the next match the user plays already has it. The first match itself has nothing inferred (no prior data).

## User-facing behavior

What changes for a player with a 3-letter initial (e.g. `KD`):

| Match # | Inferred row | AI behavior |
|---|---|---|
| 1 | Empty (no patch yet — the inference runs *after* the match ends) | Today: argues against spoken lines only. |
| 2 | Sliders moved toward match-1 evidence; lists contain the words match-1 surfaced | Today + a small "prior matches suggested this human X" hint to the AI. The AI still argues against the spoken line. |
| 5+ | Confident row — sliders stable, philosophy sentence settled, likes/dislikes carry the player's recurring topics | The AI is primed by the inferred row but the spoken turn still wins. The player feels the AI "knows them" without feeling like it's arguing with a fixed target. |

What changes for the player on the `/fighters` page:

- The card now shows the inferred **Likes**, **Dislikes**, **Philosophy**, and the **personality sliders** as small bars — the same UI as `/profiles` for personas, but populated by inference rather than by hand.
- The card includes a small "(inferred from N matches)" note so the player can tell the values came from their arguments, not from a form they filled out.
- A future task surfaces the audit log: "this slider moved because in match X you said Y." Out of scope for T56.

What does **not** change:

- The two result Tables and their pages (history, leaderboard).
- The picker, the cast editor, the result pages.
- The Fish Audio / Gemini TTS / Azure Speech chain.
- The T55 BFF cookie / Dev Entra path.

## Out of scope (this task)

- Changing the picker, the cast editor, the result pages, the leaderboard.
- Touching Fish Audio, Gemini TTS, Azure Speech.
- Touching the BFF cookie / MSAL / Dev Entra path (T55).
- The audit log Table (`cast_inference_log`) is wired but not user-facing. A future task surfaces "why does this row say this?" in the UI.

## Test plan

| | New | Updated |
|---|---|---|
| Unit | `CastMemberTests` (invariant + factory), `CastRepositoryTests`, `CastMappingTests`, **`CastInferenceMergeTests`** (the weighted-average merge with the `Patience ≈ 62` assertion), **`CastInferencePromptBuilderTests`** (patch follows directive, precedes transcript; empty inference is silent; patch never overrides spoken turn) | every test that referenced `Profile.CreateHuman`, `Profile.FromFights`, `Fighter.EnsureAsync`, `IFighterRepository.EnsureAsync`, `RoundPromptBuilder.BuildHumanOpponent` |
| Integration | **`CastInferenceServiceTests`** (Azurite queue + audit log round-trip; retry idempotency; schema-version refusal to clobber) | `FighterRepositoryTests`, `ProfileRepositoryTests` collapse into `CastRepositoryTests` |
| E2EAPI | none new | existing routes keep the same contract; one new test pins the 409 on `PATCH /api/profiles/{human-id}` |
| E2EUI | none new | the existing `ProfileFlowTests` and `WatchFlowTests` should run without modification |

Coverage target stays the same (90 % line minimum, per `docs/VERIFICATION.md`). Inference service must hit 95 % — it's the surface most likely to drift silently without tests.

## Risk register

- **Breaking the human-opponent directive**: mitigated by `CastInferencePromptBuilderTests.prompt_for_a_human_with_a_populated_patch_still_argues_against_the_spoken_turn`. **Highest-priority test.**
- **LLM flapping the sliders match-over-match**: mitigated by the weighted-average merge with line counts as the weight. A single noisy match nudges; ten matches converge. The unit test pins the math.
- **Inference LLM is down**: mitigated by the hosted background job. The match completes; the patch is enqueued and retried later. The user's next match may be against a stale or empty inferred patch; the prompt builder's "empty inference is silent" test ensures that doesn't break anything.
- **Migration loses rows in production**: mitigated by the read-then-write-then-delete order and the rollback path (the old Tables stay around until a separate cleanup task runs).
- **The `CastMemberId` type aliasing to the old `ProfileId`/`FighterId` types**: the Vogen-generated value objects are the same underlying string; aliasing them is a code-only change. Tests cover it.
- **`FromFights` semantics drift**: the pipeline today sets `MarkFromFights()` after a 2P read; the new code moves that to `CastMember.MarkFromFights()`. Behaviour preserved.
- **Schema-version race**: two matches played in quick succession might enqueue two patches. The `(matchId, sideId, schemaVersion)` key prevents clobbering; the audit log makes the order debuggable.

## TL;DR

One `CastMember` record, one `castmembers` Table, one repository. Personas populate by hand; humans are populated over time by an LLM that reads each match's spoken lines and writes a delta patch into the row, weighted by line count. The AI spouse sees the inferred patch as additional context but argues against what was just said. Two result Tables stay separate. The cast page and fighters page stay separate. The prompt builder's order is pinned by a new test. One PR.
