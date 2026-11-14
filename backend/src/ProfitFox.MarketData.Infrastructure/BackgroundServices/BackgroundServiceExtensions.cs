using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     What every background service does the same way: it sends each request in a scope of its own, reads the catalog and reads its
///     settings again before each run.
/// </summary>
internal static class BackgroundServiceExtensions
{
    /// <summary>
    ///     Sends the request in a scope of its own, so it has a connection of its own and shares nothing with the other requests.
    /// </summary>
    public static async Task<TResponse> SendAsync<TResponse>(
        this IServiceScopeFactory scopes,
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }

    /// <inheritdoc cref="SendAsync{TResponse}" />
    public static async Task SendAsync(this IServiceScopeFactory scopes, IRequest request, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }

    /// <summary>
    ///     The instruments of the exchange in the catalog, listed or delisted; none when the catalog cannot be read, which is logged, so
    ///     the service goes on and reads it again in its next run.
    /// </summary>
    public static async Task<IReadOnlyCollection<InstrumentInternal>> InstrumentsOrNoneAsync(
        this IServiceScopeFactory scopes,
        Exchange exchange,
        ILogger logger,
        CancellationToken stoppingToken)
    {
        try
        {
            return await scopes.SendAsync(new GetInstrumentsQuery(exchange), stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading the instruments of {Exchange} failed.", exchange);

            return [];
        }
    }

    /// <summary>
    ///     The settings as they are now, so a saved change applies from the next run without a restart. A broken change is logged, and
    ///     <paramref name="lastValid" /> stay until it is fixed.
    /// </summary>
    public static TSettings CurrentOrLastValid<TSettings>(this IOptionsMonitor<TSettings> settings, TSettings lastValid, ILogger logger)
    {
        try
        {
            return settings.CurrentValue;
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
        {
            logger.LogError(exception, "The {Settings} are broken; the service goes on with the last valid ones.", typeof(TSettings).Name);

            return lastValid;
        }
    }
}