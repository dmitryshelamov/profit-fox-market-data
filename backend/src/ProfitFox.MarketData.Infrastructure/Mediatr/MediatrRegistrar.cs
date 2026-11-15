using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;

namespace ProfitFox.MarketData.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    /// <summary>
    ///     The handlers of the commands and queries, those of Application and the queries Infrastructure answers with SQL of its own. No
    ///     request runs in a transaction of the pipeline: a repository saves in one of its own.
    /// </summary>
    internal static void Configure(IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblies(
            typeof(SyncBybitInstrumentsCommand).Assembly,
            typeof(MediatrRegistrar).Assembly));
    }
}