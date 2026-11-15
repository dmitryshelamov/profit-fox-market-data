using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     The settings of the API of an exchange as they are now, which its gate and clients read on every request: a change that breaks
///     them is logged once and the last valid ones stay until it is fixed, so an edit of the settings file never stops the requests. Until
///     the settings were valid once there are none to keep, so a broken start still stops the app.
/// </summary>
internal sealed class LastValidOptionsMonitor<TSettings>(IOptionsMonitor<TSettings> settings, ILogger<LastValidOptionsMonitor<TSettings>> logger)
    : IOptionsMonitor<TSettings>
    where TSettings : class
{
    private readonly Lock _lock = new Lock();
    private TSettings? _lastValid;
    private bool _broken;

    public TSettings CurrentValue => Get(Options.DefaultName);

    public TSettings Get(string? name)
    {
        try
        {
            TSettings current = settings.Get(name);

            lock (_lock)
            {
                _lastValid = current;
                _broken = false;
            }

            return current;
        }
        catch (OptionsValidationException exception)
        {
            lock (_lock)
            {
                if (_lastValid is null)
                {
                    throw;
                }

                if (!_broken)
                {
                    logger.LogError(exception, "The {Settings} are broken; the requests go on with the last valid ones.", typeof(TSettings).Name);
                    _broken = true;
                }

                return _lastValid;
            }
        }
    }

    public IDisposable? OnChange(Action<TSettings, string?> listener)
    {
        return settings.OnChange(listener);
    }

    /// <summary>
    ///     Makes every reader of <typeparamref name="TSettings" /> through <see cref="IOptionsMonitor{TOptions}" /> get this monitor over the
    ///     one of the framework, the check at start included.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IOptionsMonitor<TSettings>>(provider => new LastValidOptionsMonitor<TSettings>(
            new OptionsMonitor<TSettings>(
                provider.GetRequiredService<IOptionsFactory<TSettings>>(),
                provider.GetServices<IOptionsChangeTokenSource<TSettings>>(),
                provider.GetRequiredService<IOptionsMonitorCache<TSettings>>()),
            provider.GetRequiredService<ILogger<LastValidOptionsMonitor<TSettings>>>()));
    }
}