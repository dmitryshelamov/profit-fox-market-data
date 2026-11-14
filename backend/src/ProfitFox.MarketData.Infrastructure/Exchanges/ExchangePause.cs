using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     The pause of the requests to one exchange after it limited them. It only ever ends later: a limit on a request sent before a longer
///     pause comes back after it and must not shorten it. It is kept in memory only.
/// </summary>
internal sealed class ExchangePause(string exchange, TimeProvider time)
{
    private readonly Lock _lock = new Lock();
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    /// <exception cref="ExchangePausedException">The pause has not ended.</exception>
    public void ThrowIfActive()
    {
        DateTimeOffset until;

        lock (_lock)
        {
            until = _until;
        }

        if (time.GetUtcNow() < until)
        {
            throw new ExchangePausedException($"{exchange} limited the requests; they wait until {until:O}.", until);
        }
    }

    /// <summary>
    ///     Holds the requests for <paramref name="pause" /> from now; gives when the pause ends.
    /// </summary>
    public DateTimeOffset Extend(TimeSpan pause)
    {
        DateTimeOffset until = time.GetUtcNow() + pause;

        lock (_lock)
        {
            _until = until > _until ? until : _until;

            return _until;
        }
    }
}