using Microsoft.Extensions.Options;

namespace ProfitFox.MarketData.Tests.Integration.Settings;

/// <summary>
///     Settings that never change, for a class under test that reads them through <see cref="IOptionsMonitor{TOptions}" />.
/// </summary>
internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name)
    {
        return value;
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        return null;
    }
}