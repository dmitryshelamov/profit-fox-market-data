using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Instruments.Sync;
using ProfitFox.MarketData.Infrastructure.Mediatr.Behaviors;

namespace ProfitFox.MarketData.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    /// <summary>
    ///     The handlers of the commands (Application) and of the queries (Infrastructure); every request goes through the transaction behavior.
    /// </summary>
    internal static void Configure(IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblies(
            typeof(SyncInstrumentsCommand).Assembly,
            typeof(MediatrRegistrar).Assembly));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PostgresTransactionBehavior<,>));
    }
}