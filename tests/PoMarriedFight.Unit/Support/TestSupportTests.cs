using System.Net;
using PoMarriedFight.Shared.Models;
using PoMarriedFight.TestSupport;

namespace PoMarriedFight.Unit.Support;

public class TestSupportTests
{
    [Fact]
    public async Task FakeHttpHandler_answers_in_order_captures_requests_and_falls_back()
    {
        using var handler = new FakeHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"ok":true}""")
            .Enqueue((request, body) => FakeHttpHandler.Json(HttpStatusCode.Accepted, body ?? "null"));
        using var client = handler.CreateClient("https://api.example/");
        client.DefaultRequestHeaders.Add("x-goog-api-key", "k");

        var first = await client.GetAsync(new Uri("v1/one", UriKind.Relative));
        using var content = new StringContent("""{"echo":1}""");
        var second = await client.PostAsync(new Uri("v1/two", UriKind.Relative), content);
        var third = await client.GetAsync(new Uri("v1/three", UriKind.Relative));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync()).Should().Be("""{"echo":1}""", "the responder saw the captured body");
        third.StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing was scripted for it");

        handler.Requests.Should().HaveCount(3);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri!.AbsolutePath.Should().Be("/v1/two");
        handler.Requests[1].Body.Should().Be("""{"echo":1}""");
        handler.Requests[0].Headers["x-goog-api-key"].Should().Be("k");
    }

    [Fact]
    public void Fakers_are_deterministic_and_produce_valid_tags()
    {
        var a = Fakers.New();
        var b = Fakers.New();

        Fakers.Topic(a).Should().Be(Fakers.Topic(b));
        Fakers.Line(a).Should().Be(Fakers.Line(b));

        var (first, second) = Fakers.TagPair(a);
        Initials.ArePair(first, second).Should().BeTrue();
        first.Should().MatchRegex("^[A-Z0-9]{1,3}$");
    }
}
