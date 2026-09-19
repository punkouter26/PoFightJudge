using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Unit.Voice;

public class TranscriptsTests
{
    [Fact]
    public void Clean_strips_wrappers_and_turns_narrated_silence_into_an_empty_transcript()
    {
        (string Raw, string Expected)[] cases =
        [
            ("Fine, whatever.", "Fine, whatever."),
            ("  Transcript: I did not say that.  ", "I did not say that."),
            ("NO_SPEECH", ""),
        ];

        foreach (var (raw, expected) in cases)
        {
            Transcripts.Clean(raw).Should().Be(expected);
        }
    }
}
