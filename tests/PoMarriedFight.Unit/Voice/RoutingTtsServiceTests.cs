using Microsoft.Extensions.Logging.Abstractions;
using PoMarriedFight.Api.Features.Profiles;
using PoMarriedFight.Api.Features.Voice;

namespace PoMarriedFight.Unit.Voice;

public class RoutingTtsServiceTests
{
    /// <summary>A provider that answers, says nothing, or throws — enough to drive every branch of the chain.</summary>
    private sealed class StubProvider(string name, string? base64 = "QUJD", Exception? throws = null) : ITtsProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public bool IsFake => true;

        public Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default)
        {
            Calls++;
            return throws is not null
                ? Task.FromException<TtsAudio>(throws)
                : Task.FromResult(base64 is null ? TtsAudio.None : new TtsAudio(base64, TtsAudioFormats.Mp3));
        }
    }

    private static RoutingTtsService Sut(IEnumerable<ITtsProvider> providers, bool preferFast = true, ITtsCache? cache = null) =>
        new(providers, new TtsRoutingOptions(preferFast, TtsAudioFormats.Mp3, CacheEnabled: cache is not null), cache ?? new NullTtsCache(), NullLogger<RoutingTtsService>.Instance);

    private static FishAudioService Fish(bool enabled, string? defaultReference = null) =>
        new(new StubHttpClientFactory(), new FishAudioOptions(enabled, defaultReference), Routing, new Api.Features.Diagnostics.AiLatencyTracker(), NullLogger<FishAudioService>.Instance);

    private static AzureSpeechService Azure(bool enabled) =>
        new(new StubHttpClientFactory(), new AzureSpeechOptions(enabled, "k", "eastus"), Routing, new Api.Features.Diagnostics.AiLatencyTracker(), NullLogger<AzureSpeechService>.Instance);

    private static readonly TtsRoutingOptions Routing = new(true, TtsAudioFormats.Mp3, false);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The chain tests never reach the wire.");
    }

    private static TtsSettings Persona(string? fishReference = null) => new(1.0, 1.0, "Kore", fishReference);

    [Fact]
    public void Fast_first_puts_a_persona_voice_ahead_of_azure_but_a_shared_default_behind_it()
    {
        var fish = Fish(enabled: true, defaultReference: "shared");
        var azure = Azure(enabled: true);
        var gemini = new StubProvider("gemini");
        var sut = Sut([fish, azure, gemini]);

        sut.ProviderChain(Persona("own-voice")).Select(p => p.Name).Should().Equal("fish", "azure", "gemini");
        sut.ProviderChain(Persona()).Select(p => p.Name).Should().Equal("azure", "fish", "gemini");
    }

    [Fact]
    public void Without_fast_first_any_fish_voice_leads_and_unusable_providers_never_appear()
    {
        var withDefault = Sut([Fish(enabled: true, defaultReference: "shared"), Azure(enabled: true), new StubProvider("gemini")], preferFast: false);
        withDefault.ProviderChain(Persona()).Select(p => p.Name).Should().Equal("fish", "azure", "gemini");

        var noFishKey = Sut([Fish(enabled: false, defaultReference: "shared"), Azure(enabled: true), new StubProvider("gemini")]);
        noFishKey.ProviderChain(Persona("own-voice")).Select(p => p.Name).Should().Equal("azure", "gemini");

        var noReference = Sut([Fish(enabled: true), Azure(enabled: false), new StubProvider("gemini")]);
        noReference.ProviderChain(Persona()).Select(p => p.Name).Should().ContainSingle("a persona with no reference id and no shared default cannot use Fish").Which.Should().Be("gemini");

        var fakeOnly = Sut([new StubProvider("fake")]);
        fakeOnly.ProviderChain(Persona("own-voice")).Select(p => p.Name).Should().Equal("fake");
    }

    [Fact]
    public async Task A_failing_or_silent_provider_falls_through_and_the_audio_keeps_its_own_format()
    {
        var broken = new StubProvider("fish", throws: new HttpRequestException("502"));
        var silent = new StubProvider("azure", base64: null);
        var terminal = new StubProvider("gemini", base64: "R0VN");
        var sut = Sut([broken, silent, terminal], preferFast: false);

        var audio = await sut.GenerateTtsAsync("Say something.", Persona("own-voice"));

        audio.Base64.Should().Be("R0VN");
        broken.Calls.Should().Be(1);
        silent.Calls.Should().Be(1);
        terminal.Calls.Should().Be(1);

        var allDown = Sut([new StubProvider("gemini", throws: new HttpRequestException("down"))]);
        (await allDown.GenerateTtsAsync("Say something.", Persona())).Should().Be(TtsAudio.None, "a voice failure is silence, never an exception into the round");
        (await sut.GenerateTtsAsync("   ", Persona())).Should().Be(TtsAudio.None);
        terminal.Calls.Should().Be(1, "blank text never reaches a provider");
    }

    [Fact]
    public async Task Streaming_yields_chunks_in_order_marks_the_last_and_survives_one_bad_clause()
    {
        var line = "You left the freezer open all night and every single thing in it is ruined. "
            + "That is the third time this month, and I am the one who noticed it. "
            + "Do not stand there and tell me I am overreacting about this. (seething)";
        var calls = 0;
        var flaky = new FlakyProvider(() => ++calls == 2 ? throw new HttpRequestException("chunk 2 died") : "T0s=");
        var sut = Sut([flaky]);

        var chunks = new List<TtsChunk>();
        await foreach (var chunk in sut.GenerateTtsStreamAsync(line, Persona()))
        {
            chunks.Add(chunk);
        }

        chunks.Select(c => c.Index).Should().BeInAscendingOrder().And.Equal([.. Enumerable.Range(0, chunks.Count)]);
        chunks.Should().HaveCountGreaterThan(1);
        chunks[^1].IsLast.Should().BeTrue();
        chunks.Take(chunks.Count - 1).Should().OnlyContain(c => !c.IsLast);
        chunks.Should().Contain(c => c.Audio.IsEmpty, "the clause that failed is silent");
        chunks.Should().Contain(c => !c.Audio.IsEmpty, "the rest of the line still plays");

        var empty = new List<TtsChunk>();
        await foreach (var chunk in sut.GenerateTtsStreamAsync("   ", Persona()))
        {
            empty.Add(chunk);
        }

        empty.Should().ContainSingle().Which.IsLast.Should().BeTrue();
    }

    [Fact]
    public async Task A_cache_hit_skips_the_provider_and_the_key_separates_providers_and_voices()
    {
        var cache = new RecordingCache();
        var provider = new StubProvider("gemini", base64: "TkVX");
        var sut = Sut([provider], cache: cache);

        var first = await sut.GenerateTtsAsync("Hello.", Persona());
        var second = await sut.GenerateTtsAsync("Hello.", Persona());

        first.Base64.Should().Be("TkVX");
        second.Base64.Should().Be("TkVX");
        provider.Calls.Should().Be(1, "the second call was served from the cache");
        cache.Entries.Should().ContainSingle();

        var key = RoutingTtsService.CacheKey("Hello.", Persona(), "gemini", TtsAudioFormats.Mp3);
        key.Should().NotBe(RoutingTtsService.CacheKey("Hello.", Persona(), "fish", TtsAudioFormats.Mp3));
        key.Should().NotBe(RoutingTtsService.CacheKey("Hello.", new TtsSettings(1.0, 1.0, "Charon"), "gemini", TtsAudioFormats.Mp3));
        key.Should().NotBe(RoutingTtsService.CacheKey("Hello.", Persona(), "gemini", TtsAudioFormats.Pcm));
        key.Should().NotBe(RoutingTtsService.CacheKey("Goodbye.", Persona(), "gemini", TtsAudioFormats.Mp3));
    }

    private sealed class FlakyProvider(Func<string> next) : ITtsProvider
    {
        public string Name => "gemini";

        public bool IsFake => true;

        public Task<TtsAudio> SynthesizeAsync(string text, TtsSettings settings, CancellationToken ct = default)
        {
            try
            {
                return Task.FromResult(TtsAudio.Pcm(next()));
            }
            catch (HttpRequestException ex)
            {
                return Task.FromException<TtsAudio>(ex);
            }
        }
    }

    private sealed class RecordingCache : ITtsCache
    {
        public Dictionary<string, TtsAudio> Entries { get; } = new(StringComparer.Ordinal);

        public bool IsEnabled => true;

        public Task<TtsAudio?> TryGetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<TtsAudio?>(Entries.TryGetValue(key, out var audio) ? audio : null);

        public Task SetAsync(string key, TtsAudio audio, CancellationToken ct = default)
        {
            Entries[key] = audio;
            return Task.CompletedTask;
        }
    }
}
