using Google.Protobuf.WellKnownTypes;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

using Tinkoff.InvestApi.V1;

// The SDK has a Candle of its own, the one of its stream.
using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Reads the minute candles of the exchange from the Invest API of T-Invest with the token of the settings, read before every call, one
///     call a request. The candles of the dealer of T-Invest, who trades on weekends, are not asked for.
/// </summary>
internal sealed class TInvestCandlesClient(
    MarketDataService.MarketDataServiceClient marketData,
    InstrumentsService.InstrumentsServiceClient instruments,
    IOptionsMonitor<TInvestApiSettings> settings,
    TimeProvider time) : ITInvestCandlesClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <exception cref="NotSupportedException">TInvestApi:Token is not set; no call goes.</exception>
    public async Task<IReadOnlyList<Candle>> GetAsync(TInvestListedShare share, MinuteRange range, CancellationToken cancellationToken)
    {
        var request = new GetCandlesRequest
        {
            InstrumentId = share.Uid,
            From = Timestamp.FromDateTimeOffset(range.From),
            To = Timestamp.FromDateTimeOffset(range.Until),
            Interval = CandleInterval._1Min,
            CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange
        };

        GetCandlesResponse response = await marketData.GetCandlesAsync(
            request,
            TInvestMetadata.Of(settings.CurrentValue, $"No candles of {share.Ticker} are read"),
            Deadline(),
            cancellationToken);

        // A candle of a minute still open is not complete; one outside the range is left out too.
        return
        [
            .. response.Candles
                .Where(candle => candle.IsComplete)
                .Select(candle => TInvestCandleConverter.ToDomain(candle, share.Rules.QuantityStep))
                .Where(candle => candle.OpenTime >= range.From && candle.OpenTime < range.Until)
                .OrderBy(candle => candle.OpenTime)
        ];
    }

    // T-Invest tells when the first minute candle of a share is, whatever the time asked from.
    /// <exception cref="NotSupportedException">TInvestApi:Token is not set; no call goes.</exception>
    public async Task<DateTimeOffset?> GetTradingStartAsync(TInvestListedShare share, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ShareResponse response = await instruments.ShareByAsync(
            new InstrumentRequest { IdType = InstrumentIdType.Uid, Id = share.Uid },
            TInvestMetadata.Of(settings.CurrentValue, $"The trading start of {share.Ticker} is not read"),
            Deadline(),
            cancellationToken);

        return response.Instrument.First1MinCandleDate?.ToDateTimeOffset();
    }

    private DateTime Deadline()
    {
        return time.GetUtcNow().UtcDateTime + Timeout;
    }
}