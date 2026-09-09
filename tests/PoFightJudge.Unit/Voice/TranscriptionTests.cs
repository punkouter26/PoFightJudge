using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.TestSupport;

namespace PoFightJudge.Unit.Voice;

public class TranscriptsTests
{
    [Theory]
    [InlineData("Fine, whatever.", "Fine, whatever.")]
    [InlineData("  Transcript: I did not say that.  ", "I did not say that.")]
    [InlineData("NO_SPEECH", "")]
    public void Clean_strips_wrappers_and_turns_narrated_silence_into_an_empty_transcript(string raw, string expected) =>
        Transcripts.Clean(raw).Should().Be(expected);
}
