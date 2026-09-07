using System.Buffers.Binary;
using PoMarriedFight.Api.Features.Analysis;
using PoMarriedFight.Api.Features.Fight;
using PoMarriedFight.Api.Features.Fighters;
using PoMarriedFight.Api.Features.Storage;
using PoMarriedFight.Shared.Models;

namespace PoMarriedFight.Unit.Analysis;

/// <summary>
/// What one debate is worth remembering about a person. Deliberately shallow: the few things still true a month
/// later, kept with the result so a style profile is a read rather than a re-analysis.
/// </summary>
public class StyleSnapshotTests
{
    private static PlayerAssessmentDto Assessment() => new(
        "C1", "articulate", 1, ["a slip"], 8, 7, 8,
        [new FallacyDto("Straw man", "q"), new FallacyDto("Straw man", "q2"), new FallacyDto("Ad hominem", "q3"), new FallacyDto("Slippery slope", "q4"), new FallacyDto("False dilemma", "q5")],
        6, 7, 8, 75, [],
        new EmotionProfileDto(5, 45, 30, 20, 0, 0), [], "peak",
        ["clipped", "dry", "fast", "certain", "warm"],
        "quick", "high", "varied", 8, 5, 6, 4,
        new string('q', 200), "worst",
        ["one", "two", "three", "four"]);

    private static PlayerMetricsDto Metrics() => new(
        30, 0.5, 3, 10, 20, 90, 120, 2, 1, 3, 1, 0, 0.5, 1, 4, 2, 3, 5, 0.6, 4.5, 1.4, 12, 60, 8,
        ["point"], "considerable", ["that is not (×3)", "the point is (×2)", "as I said (×2)", "and another (×2)"], 0, 0.2, 0.4);

    [Fact]
    public void A_judged_fight_keeps_the_handful_of_things_worth_remembering()
    {
        var snapshot = FightStyleSnapshotExtractor.FromFight(Assessment(), Metrics(), "Look, the thing is, you always do this and you know it, every single time");

        snapshot.Cefr.Should().Be("C1");
        snapshot.Tone.Should().Be("clipped, dry, fast");
        snapshot.Phrases.Should().HaveCount(FightStyleSnapshotExtractor.MaxPhrases);
        snapshot.Tips.Should().HaveCount(FightStyleSnapshotExtractor.MaxTips);
        snapshot.Opener.Split(' ').Should().HaveCount(FightStyleSnapshotExtractor.OpenerWords);
        snapshot.BestQuote.Should().HaveLength(FightStyleSnapshotExtractor.MaxQuoteLength + 1, "a quote is trimmed with an ellipsis rather than a table being broken");
    }

    [Fact]
    public void The_same_fallacy_twice_is_one_habit_not_two()
    {
        var snapshot = FightStyleSnapshotExtractor.FromFight(Assessment(), Metrics(), "opener");

        snapshot.Fallacies.Should().Equal(["Straw man", "Ad hominem", "Slippery slope"]);
    }

    [Fact]
    public void Only_the_emotions_that_actually_showed_are_kept_strongest_first()
    {
        var snapshot = FightStyleSnapshotExtractor.FromFight(Assessment(), Metrics(), "opener");

        snapshot.Emotions.Should().Equal(["confident", "frustrated", "angry"]);
        snapshot.Emotions.Should().NotContain("amused", "nobody was amused, and a zero is not a trait");
    }

    [Fact]
    public void A_watch_keeps_what_it_can_see_and_leaves_the_rest_empty()
    {
        var snapshot = FightStyleSnapshotExtractor.FromSpokenLines(
        [
            "That is not the point and you know it.",
            "That is not the point, it never was.",
            "I have said everything I am going to say about the freezer door being left open all night.",
        ]);

        snapshot.Opener.Should().StartWith("That is not the point");
        snapshot.Phrases.Should().Contain(p => p.Contains("that is not", StringComparison.OrdinalIgnoreCase));
        snapshot.BestQuote.Should().Contain("freezer");

        // Nothing judged a watch per side, so these stay empty rather than being invented.
        snapshot.Cefr.Should().BeEmpty();
        snapshot.Tone.Should().BeEmpty();
        snapshot.Fallacies.Should().BeEmpty();
        snapshot.Tips.Should().BeEmpty();
    }

    [Fact]
    public void Somebody_who_said_nothing_has_nothing_to_remember()
    {
        FightStyleSnapshotExtractor.FromSpokenLines([]).Should().Be(StyleSnapshot.Empty);
        FightStyleSnapshotExtractor.FromSpokenLines(["   ", string.Empty]).Should().Be(StyleSnapshot.Empty);
    }

    [Fact]
    public void An_opener_is_the_first_few_words_however_long_the_line_was()
    {
        FightStyleSnapshotExtractor.Opener("one two three").Should().Be("one two three");
        FightStyleSnapshotExtractor.Opener(string.Join(' ', Enumerable.Repeat("word", 40)))
            .Split(' ').Should().HaveCount(FightStyleSnapshotExtractor.OpenerWords);
        FightStyleSnapshotExtractor.Opener(null).Should().BeEmpty();
    }
}

/// <summary>Cutting a clip out of a recording, so a quoted moment can actually be heard.</summary>
public class WavSlicerTests
{
    private const int Rate = 16_000;

    private static byte[] Recording(int seconds)
    {
        var wav = WavWriterBytes(seconds);
        return wav;

        static byte[] WavWriterBytes(int seconds)
        {
            var pcm = new byte[Rate * 2 * seconds];
            for (var i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (byte)(i % 251);
            }

            return PoMarriedFight.Api.Features.Fight.WavWriter.Build(Rate, pcm);
        }
    }

    [Fact]
    public void A_clip_is_a_wav_in_its_own_right_covering_the_seconds_asked_for()
    {
        var clip = WavSlicer.Slice(Recording(10), 2, 4);

        clip.Should().NotBeNull();
        System.Text.Encoding.ASCII.GetString(clip!, 0, 4).Should().Be("RIFF");
        var data = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(clip.AsSpan(40));
        data.Should().Be(Rate * 2 * 2, "two seconds of 16-bit mono at 16 kHz");
        clip.Length.Should().Be(WavSlicer.HeaderBytes + data);
    }

    [Fact]
    public void A_range_running_past_the_end_is_clamped_rather_than_refused()
    {
        var clip = WavSlicer.Slice(Recording(3), 2, 30);

        clip.Should().NotBeNull("a highlight near the end is still worth hearing");
        clip!.Length.Should().BeLessThan(WavSlicer.HeaderBytes + (Rate * 2 * 30));
    }

    [Fact]
    public void A_range_with_nothing_in_it_is_no_clip()
    {
        WavSlicer.Slice(Recording(5), 3, 3).Should().BeNull();
        WavSlicer.Slice(Recording(5), 4, 2).Should().BeNull();
        WavSlicer.Slice(Recording(5), 10, 12).Should().BeNull("that is past the end of the recording");
    }

    [Fact]
    public void Something_that_is_not_a_recording_is_not_sliced()
    {
        WavSlicer.Slice([], 0, 1).Should().BeNull();
        WavSlicer.Slice(new byte[20], 0, 1).Should().BeNull();
    }

    /// <summary>
    /// Recordings are stored in Opus, which cannot be cut at an arbitrary offset — so a clip asked for out of one
    /// is decoded first, and what comes back is still a WAV, because that is what a browser can play.
    /// </summary>
    [Fact]
    public void A_clip_can_be_cut_out_of_an_opus_recording()
    {
        var pcm = new byte[16_000 * 2 * 4];
        for (var i = 0; i < pcm.Length / 2; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(Math.Sin(i / 12.0) * 12_000));
        }

        var opus = OpusAudio.Encode(pcm, 16_000);

        var clip = WavSlicer.SliceRecording(opus, "match/players.opus", 16_000, 1.0, 2.0);

        clip.Should().NotBeNull();
        BinaryPrimitives.ReadInt32LittleEndian(clip.AsSpan(24)).Should().Be(16_000);
        (clip!.Length - WavSlicer.HeaderBytes).Should().BeCloseTo(16_000 * 2, (uint)(16_000 / 4), "one second of it, give or take a frame");
    }

    [Fact]
    public void A_wav_recording_is_still_cut_without_decoding_anything()
    {
        var wav = WavWriter.Build(16_000, new byte[16_000 * 2 * 2]);

        var clip = WavSlicer.SliceRecording(wav, "match/players.wav", 16_000, 0.5, 1.0);

        clip.Should().NotBeNull();
        (clip!.Length - WavSlicer.HeaderBytes).Should().Be(16_000);
    }
}
