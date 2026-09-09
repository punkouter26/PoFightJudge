using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Analysis;
using PoFightJudge.Api.Features.Voice;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Analysis;

/// <summary>
/// Reading a whole fight with Azure's fast transcription instead of the transcribe model.
/// </summary>
/// <remarks>
/// The fallback that ran when the host's live captions came out too thin was gemini-3.5-transcribe, and
/// GeminiTranscriptionService's own remarks record what it does: accepts the request, bills the audio tokens,
/// answers 200 with finishReason STOP — and returns an empty part. AGENT.md has it as never having run against the
/// real service, so the one path that pays is also the one path nobody has watched work.
///
/// Azure fast transcription is already configured here (it transcribes WATCH turns), diarizes to a stated speaker
/// count, and returns word-level offsets — which is the whole contract the mapper needs. 500 MB and five hours are
/// its limits; a fifteen-minute fight is 28 MB.
/// </remarks>
public class AzureDiarizedTranscriberTests
{
    [Fact]
    public void Two_speakers_are_asked_for_by_name_because_a_fight_has_exactly_two()
    {
        var definition = JsonNode.Parse(AzureDiarizedTranscriber.BuildDefinition())!.AsObject();

        definition["locales"]!.AsArray().Select(l => l!.GetValue<string>()).Should().Equal("en-US");
        definition["diarization"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        definition["diarization"]!["maxSpeakers"]!.GetValue<int>().Should().Be(2, "a fight is two people, and saying so beats letting it guess");
        definition["profanityFilterMode"]!.GetValue<string>().Should().Be("None", "an argument is judged on what was said, not a cleaned-up version");
    }

    [Fact]
    public void Words_come_back_with_their_speaker_and_their_place_in_the_recording()
    {
        var transcript = AzureDiarizedTranscriber.ParseResponse("""
            {
              "durationMilliseconds": 4000,
              "combinedPhrases": [{"text": "you never listen rubbish"}],
              "phrases": [
                {"speaker": 1, "offsetMilliseconds": 1000, "durationMilliseconds": 900, "text": "you never listen",
                 "words": [
                   {"text": "you", "offsetMilliseconds": 1000, "durationMilliseconds": 200},
                   {"text": "never", "offsetMilliseconds": 1200, "durationMilliseconds": 300},
                   {"text": "listen", "offsetMilliseconds": 1500, "durationMilliseconds": 400}]},
                {"speaker": 2, "offsetMilliseconds": 2500, "durationMilliseconds": 500, "text": "rubbish",
                 "words": [{"text": "rubbish", "offsetMilliseconds": 2500, "durationMilliseconds": 500}]}
              ]
            }
            """);

        transcript.Words.Should().HaveCount(4);
        transcript.Words[0].Should().Be(new TranscriptWord("you", "spk_1", 1.0, 1.2));
        transcript.Words[2].Text.Should().Be("listen");
        transcript.Words[3].Label.Should().Be("spk_2", "the phrase carries the speaker; its words inherit it");
        transcript.Words[3].Start.Should().Be(2.5);
        transcript.Words[3].End.Should().Be(3.0);
    }

    /// <summary>The mapper needs offsets, so a phrase that arrived without its words still has to place them.</summary>
    [Fact]
    public void A_phrase_with_no_word_timings_is_spread_across_its_own_span()
    {
        var transcript = AzureDiarizedTranscriber.ParseResponse("""
            {"phrases": [{"speaker": 1, "offsetMilliseconds": 2000, "durationMilliseconds": 900, "text": "you never listen"}]}
            """);

        transcript.Words.Should().HaveCount(3);
        transcript.Words[0].Start.Should().Be(2.0);
        transcript.Words[^1].End.Should().BeApproximately(2.9, 0.001);
        transcript.Words.Should().OnlyContain(w => string.Equals(w.Label, "spk_1", StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_is_the_phrases_in_the_order_they_were_said()
    {
        var transcript = AzureDiarizedTranscriber.ParseResponse("""
            {"phrases": [
               {"speaker": 1, "offsetMilliseconds": 0, "durationMilliseconds": 100, "text": "you never listen"},
               {"speaker": 2, "offsetMilliseconds": 200, "durationMilliseconds": 100, "text": "rubbish"}]}
            """);

        transcript.Text.Should().Be("you never listen rubbish");
    }

    /// <summary>
    /// A shape this does not recognise is an empty transcript rather than an exception — the pipeline reads an
    /// empty one as "too little was said to judge", which is a ruling, where a throw loses the whole report.
    /// </summary>
    [Fact]
    public void An_answer_it_cannot_read_is_an_empty_transcript_rather_than_a_lost_report()
    {
        AzureDiarizedTranscriber.ParseResponse("not json").Words.Should().BeEmpty();
        AzureDiarizedTranscriber.ParseResponse("{}").Words.Should().BeEmpty();
        AzureDiarizedTranscriber.ParseResponse("""{"phrases":[]}""").Words.Should().BeEmpty();
    }

    [Fact]
    public void A_speaker_the_service_did_not_name_still_gets_a_label_the_mapper_can_use()
    {
        var transcript = AzureDiarizedTranscriber.ParseResponse("""
            {"phrases": [{"offsetMilliseconds": 0, "durationMilliseconds": 100, "text": "mumbling"}]}
            """);

        transcript.Words.Should().ContainSingle();
        transcript.Words[0].Label.Should().Be("spk_1");
    }
}

/// <summary>Which service reads a finished fight.</summary>
public class RecordingTranscriberChoiceTests
{
    [Fact]
    public void Azure_reads_the_fight_when_it_is_configured()
    {
        RecordingTranscribers.PrefersAzure(new AzureSpeechOptions(true, "k", "eastus")).Should().BeTrue(
            "it diarizes, it returns word offsets, and unlike the transcribe model it has been seen to work");
    }

    [Fact]
    public void Without_an_azure_key_the_transcribe_model_is_still_what_there_is()
    {
        RecordingTranscribers.PrefersAzure(new AzureSpeechOptions(false, string.Empty, string.Empty)).Should().BeFalse();
    }
}
