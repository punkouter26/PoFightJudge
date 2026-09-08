using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Ai.Fakes;
using PoFightJudge.Api.Features.Fight;

namespace PoFightJudge.Api.Features.Analysis;

public static class AnalysisServiceExtensions
{
    /// <summary>
    /// The post-fight pipeline and the three clients it drives. Registered after the fight, so the real intake
    /// replaces the placeholder that only logs: from here a finished fight is actually read.
    /// </summary>
    public static IServiceCollection AddPoAnalysis(this IServiceCollection services, IConfiguration configuration, AiMode ai)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ai);

        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.Section));

        if (ai.UseFakes)
        {
            services.AddSingleton<IGeminiFilesClient, FakeAnalysisClients.Files>();
            services.AddSingleton<IGeminiTranscribeClient, FakeAnalysisClients.Transcriber>();
            services.AddSingleton<IGeminiJudgeClient, FakeAnalysisClients.Judge>();
        }
        else
        {
            services.AddSingleton<IGeminiFilesClient, GeminiFilesClient>();
            services.AddSingleton<IGeminiTranscribeClient, GeminiTranscribeClient>();
            services.AddSingleton<IGeminiJudgeClient, GeminiJudgeClient>();
        }

        services.AddSingleton<AnalysisPipeline>();
        services.AddSingleton<IAnalysisIntake>(sp => sp.GetRequiredService<AnalysisPipeline>());
        services.AddHostedService(sp => sp.GetRequiredService<AnalysisPipeline>());
        return services;
    }
}
