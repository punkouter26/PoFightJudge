namespace PoFightJudge.Shared.Configuration;

/// <summary>
/// Every configuration key the solution reads, in one place. Keys under <c>PoFightJudge:</c> map to Key Vault
/// secrets by replacing <c>:</c> with <c>--</c> (<c>PoFightJudge:GeminiApiKey</c> → <c>PoFightJudge--GeminiApiKey</c>).
/// A literal key string anywhere else is a defect.
/// </summary>
public static class ConfigKeys
{
    public const string Root = "PoFightJudge";

    /// <summary>Supplied by <c>appsettings.Development.json</c> locally and an App Service setting in Azure — never by source.</summary>
    public static class KeyVault
    {
        public const string Uri = "KeyVault:Uri";
    }

    public static class AzureAd
    {
        public const string Section = $"{Root}:AzureAd";
        public const string TenantId = $"{Section}:TenantId";
        public const string ClientId = $"{Section}:ClientId";
        // No Audience key: ValidAudiences is derived from ClientId ([clientId, api://clientId]) in the host.
    }

    public static class Auth
    {
        /// <summary>Explicit opt-in for FakeAuth + guest sign-in; honoured only outside Production.</summary>
        public const string AllowFakeAuth = $"{Root}:Auth:AllowFakeAuth";

        /// <summary>
        /// Explicit opt-in for the real Entra ID cookie scheme in Development. When <c>true</c> and the environment is
        /// Development, the API registers an OIDC code-flow handler alongside the existing guest cookie endpoint, and
        /// the WASM client shows a 'Sign in with Microsoft' button. Test and Production ignore this flag — Test keeps
        /// FakeAuth for deterministic impersonation; Production already runs the prod Entra registration through MSAL.
        /// </summary>
        public const string AllowDevEntra = $"{Root}:Auth:AllowDevEntra";

        /// <summary>
        /// Dev Entra config (separate from the prod keys so the prod app registration stays untouched). Keys are
        /// mapped 1:1 from Key Vault secrets <c>PoFightJudge--AzureAd--ClientId-Dev</c> and <c>--TenantId-Dev</c> by
        /// <see cref="Shared.PoFightJudgeSecretManager"/> (the trailing <c>-Dev</c> survives the prefix strip on
        /// purpose — the secret name and the config key match exactly).
        /// </summary>
        public const string DevClientId = $"{Root}:AzureAd:ClientId-Dev";
        public const string DevTenantId = $"{Root}:AzureAd:TenantId-Dev";
    }

    /// <summary>Who may run <c>POST /api/seed/profiles</c> with a real login: a list of emails (Admin role always qualifies).</summary>
    public static class Seed
    {
        public const string AdminEmails = $"{Root}:Seed:AdminEmails";
    }

    /// <summary>AI provider credentials and the model ids per operation (config-driven: the published ids move on their own).</summary>
    public static class Ai
    {
        public const string Section = $"{Root}:Ai";

        public const string GeminiApiKey = $"{Root}:GeminiApiKey";
        public const string GeminiApiKeyEnvVar = "GEMINI_API_KEY";
        public const string FishAudioApiKey = $"{Root}:FishAudioApiKey";
        public const string FishAudioApiKeyEnvVar = "FISH_API_KEY";
        public const string FishDefaultReferenceId = $"{Root}:Fish:DefaultReferenceId";
        public const string AzureSpeechKey = $"{Root}:AzureSpeechKey";
        public const string AzureSpeechRegion = $"{Root}:AzureSpeechRegion";

        public const string RoundModel = $"{Section}:RoundModel";
        public const string JudgeModel = $"{Section}:JudgeModel";
        public const string ProfileModel = $"{Section}:ProfileModel";
        public const string TtsModel = $"{Section}:TtsModel";
        public const string LiveModel = $"{Section}:LiveModel";
        public const string TranscribeModel = $"{Section}:TranscribeModel";

        /// <summary>What a fight's summary is embedded with, so a history can be searched by meaning.</summary>
        public const string EmbeddingModel = $"{Section}:EmbeddingModel";
        public const string Voice = $"{Section}:Voice";
        public const string JudgeThinkingLevel = $"{Section}:JudgeThinkingLevel";
        public const string JudgeServiceTier = $"{Section}:JudgeServiceTier";

        /// <summary>Wire format the voice chain asks providers for: <c>mp3</c> (default) or <c>pcm</c>.</summary>
        public const string TtsWireFormat = $"{Section}:TtsWireFormat";

        /// <summary>
        /// A local Ollama daemon serving the text seam instead of the fakes, outside Production. Bound as a section
        /// because it carries an endpoint and a model name rather than one value.
        /// </summary>
        public const string OllamaSection = $"{Section}:Ollama";
    }

    /// <summary>
    /// Storage endpoints and container names. Endpoints only — the solution authenticates exclusively with
    /// <c>DefaultAzureCredential</c>, so there is deliberately no connection-string key (and BannedSymbols.txt
    /// makes the connection-string constructors a build error).
    /// </summary>
    public static class Storage
    {
        public const string TableEndpoint = $"{Root}:TableStorageEndpoint";
        public const string BlobEndpoint = $"{Root}:BlobStorageEndpoint";
        public const string AudioContainer = $"{Root}:Storage:AudioContainer";
        public const string FacesContainer = $"{Root}:Storage:FacesContainer";
        public const string TtsCacheContainer = $"{Root}:Storage:TtsCacheContainer";
    }

    /// <summary>
    /// Local Azurite endpoints, applied over the Key Vault values in the host when <see cref="Flags.UseAzurite"/> is on
    /// (Development only). Still endpoints: Azurite runs <c>--oauth basic</c> over HTTPS.
    /// </summary>
    public static class Azurite
    {
        public const string Section = $"{Root}:Azurite";
        public const string TableEndpoint = $"{Section}:TableStorageEndpoint";
        public const string BlobEndpoint = $"{Section}:BlobStorageEndpoint";
    }

    public static class Telemetry
    {
        /// <summary>Env var App Service injects automatically; the only accepted source.</summary>
        public const string AppInsightsEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";
        public const string SamplingRatio = $"{Root}:Telemetry:SamplingRatio";
        public const string SeqServerUrl = "Serilog:Seq:ServerUrl";
    }

    public static class Debate
    {
        public const string Section = $"{Root}:Debate";
    }

    public static class Analysis
    {
        public const string Section = $"{Root}:Analysis";
    }

    public static class Audio
    {
        /// <summary><c>mp3</c> (default) or <c>pcm</c> for synthesized speech on the wire.</summary>
        public const string WireFormat = $"{Root}:Audio:WireFormat";

        /// <summary>Store fight recordings as Ogg/Opus (default true); false keeps WAV.</summary>
        public const string StoreOpus = $"{Root}:Audio:StoreOpus";
    }

    public static class Host
    {
        public const string AspNetCoreUrls = "ASPNETCORE_URLS";
        public const string Urls = "urls";
    }
}

/// <summary>
/// Feature flag names, read through <c>Microsoft.FeatureManagement</c> from the <c>FeatureManagement</c> section and
/// reported by <c>GET /api/features</c>. The client's <c>&lt;FeatureGate&gt;</c> uses the same names.
/// </summary>
public static class Flags
{
    public const string Section = "FeatureManagement";

    /// <summary>Force the deterministic fakes (Development/Test only; Production ignores it).</summary>
    public const string UseFakeAi = "UseFakeAi";

    /// <summary>Development-only guest sign-in.</summary>
    public const string DevGuestEnabled = "DevGuestEnabled";

    /// <summary>Offer the SELF (live human) player in WATCH.</summary>
    public const string HumanInWatch = "HumanInWatch";

    /// <summary>Try the browser's Web Speech API for a SELF turn before posting the clip to the server.</summary>
    public const string BrowserSpeechRecognition = "BrowserSpeechRecognition";

    /// <summary>Fastest configured voice provider first (Azure before default-Fish).</summary>
    public const string PreferFastVoice = "PreferFastVoice";

    /// <summary>Content-addressed blob cache of synthesized speech.</summary>
    public const string TtsCacheEnabled = "TtsCacheEnabled";

    /// <summary>Development-only: point storage at the local Azurite container.</summary>
    public const string UseAzurite = "UseAzurite";

    /// <summary>Require the admin role for /api/diag even in Development.</summary>
    public const string DiagRequiresAdminInDev = "DiagRequiresAdminInDev";

    /// <summary>
    /// True when the API is running the dev Entra OIDC scheme (Development AND <c>PoFightJudge:Auth:AllowDevEntra</c>
    /// AND a dev client id is configured). The client renders the 'Sign in with Microsoft' button when this is on,
    /// otherwise the guest button is the only door.
    /// </summary>
    public const string DevEntraEnabled = "DevEntraEnabled";

    public static readonly IReadOnlyList<string> All =
    [
        UseFakeAi, DevGuestEnabled, HumanInWatch, BrowserSpeechRecognition, PreferFastVoice, TtsCacheEnabled, UseAzurite, DiagRequiresAdminInDev, DevEntraEnabled,
    ];
}
