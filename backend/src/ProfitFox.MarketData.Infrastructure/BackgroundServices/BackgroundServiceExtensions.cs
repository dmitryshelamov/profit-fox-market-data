using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     What every background service does the same way: it sends each request in a scope of its own and reads its settings again before
///     each run.
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