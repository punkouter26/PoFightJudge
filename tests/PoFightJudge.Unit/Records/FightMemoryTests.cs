using System.Text.Json.Nodes;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Records;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Unit.Records;

/// <summary>
/// Finding a fight by what it was about rather than by the words it happens to contain.
/// </summary>
/// <remarks>
/// History search is a substring match on the topic, because Table Storage has no contains and the partition is
/// read and filtered here. That finds "thermostat" only if somebody typed "thermostat"; the fight where they
/// argued about the heating bill for twenty minutes without once using the word is invisible to it.
///
/// Every fight already produces a summary, a topic and a ruling. Embedding that once, when the analysis is
/// written, makes the whole history searchable by meaning for a fraction of a cent per fight and no new package —
/// it is one more Gemini call and a float array on a row that is already being upserted.
/// </remarks>
public class FightMemoryTests
{
    [Fact]
    public void The_request_names_the_model_and_says_what_the_text_is_for()
    {
        var body = JsonNode.Parse(GeminiEmbeddingClient.BuildRequest("gemini-embedding-001", "the thermostat", forQuery: false))!.AsObject();

        body["model"]!.GetValue<string>().Should().Be("models/gemini-embedding-001");
        body["content"]!["parts"]!.AsArray()[0]!["text"]!.GetValue<string>().Should().Be("the thermostat");
        body["taskType"]!.GetValue<string>().Should().Be("RETRIEVAL_DOCUMENT", "a stored fight is the document half of the pair");
    }

    /// <summary>
    /// The two halves are embedded differently on purpose: a query and a document that mean the same thing sit
    /// closer together when each is told which it is.
    /// </summary>
    [Fact]
    public void A_search_is_embedded_as_a_query_rather_than_as_another_document()
    {
        var body = JsonNode.Parse(GeminiEmbeddingClient.BuildRequest("gemini-embedding-001", "who does the dishes", forQuery: true))!.AsObject();

        body["taskType"]!.GetValue<string>().Should().Be("RETRIEVAL_QUERY");
    }

    [Fact]
    public void The_vector_is_read_off_the_answer()
    {
        GeminiEmbeddingClient.ParseVector("""{"embedding":{"values":[0.5,-0.25,0.125]}}""")
            .Should().Equal([0.5f, -0.25f, 0.125f]);
    }

    [Fact]
    public void An_answer_with_no_vector_in_it_is_nothing_rather_than_a_throw()
    {
        GeminiEmbeddingClient.ParseVector("""{"error":{"code":429}}""").Should().BeEmpty();
        GeminiEmbeddingClient.ParseVector("not json").Should().BeEmpty();
        GeminiEmbeddingClient.ParseVector("{}").Should().BeEmpty();
    }

    /// <summary>
    /// A vector is a thousand floats and the row it rides on is a Table Storage entity. Base64 of the raw bytes is
    /// four bytes a number; the JSON array that would otherwise be stored is nearer twelve.
    /// </summary>
    [Fact]
    public void A_vector_survives_the_round_trip_through_storage()
    {
        float[] vector = [0.5f, -0.25f, 0.125f, 0f];

        var stored = FightVector.ToBase64(vector);
        var read = FightVector.FromBase64(stored);

        read.Should().Equal(vector);
        stored.Length.Should().BeLessThan(vector.Length * 12, "the point of the encoding is that it is small");
    }

    [Fact]
    public void Nothing_stored_reads_back_as_nothing()
    {
        FightVector.FromBase64(null).Should().BeEmpty();
        FightVector.FromBase64(string.Empty).Should().BeEmpty();
        FightVector.FromBase64("not base64 at all !!").Should().BeEmpty("a corrupted row is a fight that cannot be found by meaning, not an error");
    }

    [Fact]
    public void Closeness_is_one_for_the_same_direction_and_zero_for_a_right_angle()
    {
        FightVector.Similarity([1, 0], [1, 0]).Should().BeApproximately(1, 0.0001);
        FightVector.Similarity([1, 0], [0, 1]).Should().BeApproximately(0, 0.0001);
        FightVector.Similarity([1, 0], [-1, 0]).Should().BeApproximately(-1, 0.0001);

        // Direction, not size: a longer summary must not rank above a shorter one for being longer.
        FightVector.Similarity([2, 0], [1, 0]).Should().BeApproximately(1, 0.0001);
    }

    [Fact]
    public void Vectors_that_cannot_be_compared_are_simply_not_close()
    {
        FightVector.Similarity([1, 0], []).Should().Be(0);
        FightVector.Similarity([], []).Should().Be(0);
        FightVector.Similarity([1, 0, 0], [1, 0]).Should().Be(0, "a fight embedded by an older model is not a match, it is unreadable");
        FightVector.Similarity([0, 0], [1, 0]).Should().Be(0, "a zero vector has no direction to compare");
    }

    [Fact]
    public void The_closest_fights_come_back_first_and_the_unrelated_ones_not_at_all()
    {
        var thermostat = Row("the thermostat", [1f, 0f]);
        var heating = Row("the heating bill", [0.9f, 0.1f]);
        var dishes = Row("the dishes", [0f, 1f]);

        var found = FightSearch.Rank([dishes, thermostat, heating], [1f, 0f], take: 2, minimum: 0.5);

        found.Select(f => f.Topic).Should().Equal("the thermostat", "the heating bill");
    }

    [Fact]
    public void A_fight_that_was_never_embedded_is_skipped_rather_than_ranked_last()
    {
        var embedded = Row("the thermostat", [1f, 0f]);
        var old = Row("an older fight", []);

        FightSearch.Rank([old, embedded], [1f, 0f], take: 10, minimum: 0)
            .Select(f => f.Topic).Should().Equal("the thermostat");
    }

    [Fact]
    public void Nothing_close_enough_is_no_results_rather_than_the_least_bad_one()
    {
        var dishes = Row("the dishes", [0f, 1f]);

        FightSearch.Rank([dishes], [1f, 0f], take: 10, minimum: 0.5).Should().BeEmpty();
    }

    /// <summary>What is embedded is what a person would search for: the topic, the ruling and who argued it.</summary>
    [Fact]
    public void The_text_embedded_is_what_somebody_would_search_for()
    {
        var text = FightSearch.Describe(
            "the thermostat",
            "AL",
            "SM",
            "AL argued the bill; SM argued being cold. AL took it on evidence.");

        text.Should().Contain("the thermostat").And.Contain("AL").And.Contain("SM").And.Contain("being cold");
    }

    [Fact]
    public void A_fight_with_no_ruling_yet_is_still_worth_describing()
    {
        FightSearch.Describe("the thermostat", "AL", "SM", null).Should().Contain("the thermostat");
    }

    private static FightVectorRow Row(string topic, float[] vector) =>
        new(MatchId.New(), topic, DateTimeOffset.UnixEpoch, vector);
}
