using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    /// <summary>
    ///     The handlers of the commands and queries, those of Application and the queries Infrastructure answers with SQL of its own. No
    ///     request runs in a transaction of the pipeline: a repository saves in one of its own. The handlers of history and the calls of
    ///     history read the HistoryRead section on every call: an app with a broken one stops at start, and a section broken later keeps
    ///     its last valid settings.
    /// </summary>
    internal static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddMediatR(mediatr => mediatr.RegisterServicesFromAssemblies(
            typeof(SyncBybitInstrumentsCommand).Assembly,
            typeof(MediatrRegistrar).Assembly));

        services.AddOptions<HistoryReadSettings>()
            .Bind(configuration.GetSection(HistoryReadSettings.Section))
            .Validate(read => read.MaxConcurrentReads > 0, "HistoryRead:MaxConcurrentReads must be above zero.")
            .Validate(read => read.StatementTimeoutSeconds > 0, "HistoryRead:StatementTimeoutSeconds must be above zero.")
            .ValidateOnStart();
        LastValidOptionsMonitor<HistoryReadSettings>.Register(services);
    }
}