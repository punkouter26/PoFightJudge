using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Api.Features.Ai.Fakes;

/// <summary>
/// Stand-ins for the three services a finished fight is read by, so the whole path — recording, transcript,
/// assessment, ruling, records — runs with no key and no network.
/// </summary>
/// <remarks>
/// Everything they produce says it is a rehearsal, and the ruling is derived from the transcript rather than
/// invented: whoever said more takes it. A fake that always picked player one would make the records meaningless
/// while looking like they worked.
/// </remarks>
public static class FakeAnalysisClients
{
    /// <summary>Accepts a recording and hands back a reference to it, without one ever leaving the process.</summary>
    public sealed class Files : IGeminiFilesClient
    {
        public Task<GeminiFile> UploadAsync(Stream content, long length, string mimeType, string displayName, CancellationToken ct) =>
            Task.FromResult(new GeminiFile($"files/fake-{displayName}", $"https://fake.invalid/files/{displayName}", "ACTIVE", mimeType));

        public Task<GeminiFile> GetAsync(string name, CancellationToken ct) =>
            Task.FromResult(new GeminiFile(name, $"https://fake.invalid/{name}", "ACTIVE", "audio/wav"));
    }

    /// <summary>
    /// A transcriber with no audio to work from. It says so, in enough words to clear the pipeline's own gate, and
    /// splits them between the two sides so the speaker mapping has something real to resolve.
    /// </summary>
    public sealed class Transcriber : IGeminiTranscribeClient
    {
        private static readonly string[] Rehearsal =
        [
            "this", "is", "a", "rehearsal", "transcript", "because", "there", "is", "no", "transcriber", "configured",
            "and", "so", "nobody", "actually", "heard", "what", "either", "of", "you", "said", "in", "this", "argument",
            "which", "is", "why", "the", "words", "below", "are", "not", "yours", "at", "all", "whatsoever", "here",
        ];

        public Task<TranscriptDto> TranscribeAsync(string fileUri, string mimeType, CancellationToken ct)
        {
            var words = new List<TranscriptWord>();
            for (var i = 0; i < Rehearsal.Length; i++)
            {
                // Half a second each, alternating sides every six words, so turns and overlap are both plausible.
                var start = i * 0.5;
                words.Add(new TranscriptWord(Rehearsal[i], i / 6 % 2 == 0 ? "spk_1" : "spk_2", start, start + 0.45));
            }

            return Task.FromResult(new TranscriptDto(string.Join(' ', Rehearsal), words));
        }
    }

    /// <summary>A judge that reads the transcript rather than inventing one, and says out loud that it is not real.</summary>
    public sealed class Judge : IGeminiJudgeClient
    {
        public Task<JudgeOutputDto> JudgeAsync(JudgeRequest request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);

            var first = request.Player1Metrics.Words;
            var second = request.Player2Metrics.Words;
            var winner = first >= second ? "player1" : "player2";
            var name = first >= second ? request.Player1Name : request.Player2Name;

            return Task.FromResult(new JudgeOutputDto(
                Assess(request.Player1Name, request.Transcript, Speaker.Player1),
                Assess(request.Player2Name, request.Transcript, Speaker.Player2),
                new JudgeOverallDto(
                    winner,
                    winner,
                    winner,
                    [
                        "(rehearsal ruling) There is no judge configured, so this is not a real reading of the argument.",
                        $"{name} said more of the words in the transcript, which is all this stand-in can measure.",
                        "Set a Gemini key and argue again to find out who actually won.",
                    ],
                    "(rehearsal ruling) Nobody read this argument. The numbers above were measured; the judgement was not.")));
        }

        private static PlayerAssessmentDto Assess(string name, MappedTranscript transcript, Speaker side)
        {
            var said = transcript.For(side).Select(w => w.Text).ToList();
            var quote = said.Count == 0 ? "(rehearsal) nothing was heard" : string.Join(' ', said.Take(8));

            return new PlayerAssessmentDto(
                Cefr: "B2",
                CefrJustification: "(rehearsal) nobody assessed this.",
                GrammarErrorCount: 0,
                GrammarExamples: [],
                VocabularySophistication: 5,
                Clarity: 5,
                Logic: 5,
                Fallacies: [],
                EvidenceUse: 5,
                RebuttalQuality: 5,
                Persuasiveness: 5,
                CorrectnessPercent: 50,
                Claims: [],
                Emotions: new EmotionProfileDto(40, 30, 15, 5, 5, 5),
                EmotionTimeline: [],
                PeakMomentQuote: quote,
                ToneDescriptors: ["rehearsed", "even", "unread"],
                PaceImpression: "(rehearsal) unmeasured",
                EnergyImpression: "(rehearsal) unmeasured",
                PitchVariationImpression: "(rehearsal) unmeasured",
                Confidence: 5,
                Politeness: 5,
                Aggression: 5,
                Listening: 5,
                BestMomentQuote: quote,
                WorstMomentQuote: quote,
                CoachingTips:
                [
                    $"(rehearsal) {name} was not actually assessed.",
                    "Set a Gemini key to have this read properly.",
                    "Everything measured above is real; everything judged is not.",
                ]);
        }
    }
}
