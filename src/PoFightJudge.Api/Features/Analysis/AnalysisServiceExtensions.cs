using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Fight;
using PoFightJudge.Api.Features.Voice;

namespace PoFightJudge.Api.Features.Analysis;

public static class AnalysisServiceExtensions
{
    /// <summary>
    /// The post-fight pipeline and the three clients it drives. Registered after the fight, so the real intake
    /// replaces the placeholder that only logs: from here a finished fight is actually read.
    /// </summary>
    public static IServiceCollection AddPoAnalysis(this IServiceCollection services, IConfiguration configuration, AiMode ai, AzureSpeechOptions azure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(azure);

        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.Section));

        if (ai.UseFakes)
        {
            services.AddSingleton<IGeminiFilesClient, FakeAnalysisClients.Files>();
            services.AddSingleton<IGeminiTranscribeClient, FakeAnalysisClients.Transcriber>();
            services.AddSingleton<IGeminiJudgeClient, FakeAnalysisClients.Judge>();
            services.AddSingleton<IRecordingTranscriber, GeminiRecordingTranscriber>();
        }
        else
        {
            services.AddSingleton<IGeminiFilesClient, GeminiFilesClient>();
            services.AddSingleton<IGeminiTranscribeClient, GeminiTranscribeClient>();
            services.AddSingleton<IGeminiJudgeClient, GeminiJudgeClient>();

            // One is chosen up front, best first — the same shape AddPoVoice uses for turn transcription, and for
            // the same reason: this is already the fallback, and a fallback with its own fallback pays twice.
            if (RecordingTranscribers.PrefersAzure(azure))
            {
                services.AddSingleton<IRecordingTranscriber, AzureDiarizedTranscriber>();
            }
            else
            {
                services.AddSingleton<IRecordingTranscriber, GeminiRecordingTranscriber>();
            }
        }

        services.AddSingleton<AnalysisPipeline>();
        services.AddSingleton<IAnalysisIntake>(sp => sp.GetRequiredService<AnalysisPipeline>());
        services.AddHostedService(sp => sp.GetRequiredService<AnalysisPipeline>());

        // The repository is scoped, so the resumer is too; the hosted service opens a scope for its one pass.
        services.AddScoped<AnalysisResumer>();
        services.AddHostedService<AnalysisResumeService>();
        return services;
    }
}
