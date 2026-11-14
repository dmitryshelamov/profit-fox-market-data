using Dapper;

using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Loads the minutes of the window storage does not hold, up to <see cref="CandleReadSettings.MaxExchangeRequests" /> requests to the
///     exchange, each page a command of its own; a window that needs more, or an exchange that holds the requests, goes to
///     <see cref="CandleLoadQueue" /> and the answer is loading. Then it builds the candles of the interval from the stored minutes with
///     <c>time_bucket</c>, and gives only those whose minutes storage all holds.
/// </summary>
internal sealed class GetCandlesQueryHandler(
    IInstrumentRepository instruments,
    ICandleSeriesRepository series,
    ISender sender,
    CandleLoadQueue queue,
    IOptionsMonitor<CandleReadSettings> settings,
    NpgsqlConnection connection) : IRequestHandler<GetCandlesQuery, CandlesInternal?>
{
    // The turnover T-Invest does not tell is estimated minute by minute: the volume times the mean of the high, the low and the close.
    private const string CandlesSql = """
                                      SELECT time_bucket(@BucketSeconds * interval '1 second', open_time) AS "OpenTime",
                                             first(open, open_time) AS "Open",
                                             max(high) AS "High",
                                             min(low) AS "Low",
                                             last(close, open_time) AS "Close",
                                             sum(volume) AS "Volume",
                                             sum(coalesce(turnover, volume * (high + low + close) / 3)) AS "Turnover"
                                      FROM candles
                                      WHERE instrument_id = @InstrumentId AND open_time >= @From AND open_time < @Until
                                      GROUP BY 1
                                      ORDER BY 1
                                      """;

    public async Task<CandlesInternal?> Handle(GetCandlesQuery request, CancellationToken cancellationToken)
    {
        if (await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            return null;
        }

        var window = new MinuteRange(request.From, request.To);
        bool loaded = await LoadAsync(request.InstrumentId, window, request.RequestedAt, cancellationToken);
        CandleSeries? stored = await series.GetAsync(request.InstrumentId, cancellationToken);
        IReadOnlyList<MinuteRangeInternal> covered = CoveredIn(window, stored);

        if (!loaded || stored is null)
        {
            return new CandlesInternal(Loading: !loaded, [], covered);
        }

        TimeSpan length = request.Interval.Length();
        IEnumerable<CandleRow> candles = await connection.QueryAsync<CandleRow>(
            new CommandDefinition(
                CandlesSql,
                new { request.InstrumentId, BucketSeconds = (int)length.TotalSeconds, From = window.From.ToUniversalTime(), Until = window.Until.ToUniversalTime() },
                cancellationToken: cancellationToken));

        // A candle with minutes storage does not hold, such as the one of the interval still open, would tell less than its trades.
        return new CandlesInternal(
            Loading: false,
            [
                .. candles
                    .Where(candle => stored.Uncovered(new MinuteRange(candle.OpenTime, candle.OpenTime + length)).Count == 0)
                    .Select(candle => new CandleInternal(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.Turnover))
            ],
            covered);
    }

    // Whether all the closed minutes of the window are stored after the pages a read may wait for; else the window is queued.
    private async Task<bool> LoadAsync(Guid instrumentId, MinuteRange window, DateTimeOffset requestedAt, CancellationToken cancellationToken)
    {
        try
        {
            for (var page = 0; page < settings.CurrentValue.MaxExchangeRequests; page++)
            {
                if (!await sender.Send(new LoadCandlesCommand(instrumentId, window, requestedAt), cancellationToken))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is ExchangePausedException or TransientExchangeException)
        {
            // The exchange holds the requests or fails for a while: the background loads the window once it takes them again.
        }

        queue.Enqueue(new CandleLoadRequest(instrumentId, window));

        return false;
    }

    private static IReadOnlyList<MinuteRangeInternal> CoveredIn(MinuteRange window, CandleSeries? stored)
    {
        if (stored is null)
        {
            return [];
        }

        return
        [
            .. stored.Covered
                .Where(covered => covered.From < window.Until && covered.Until > window.From)
                .Select(covered => new MinuteRangeInternal(
                    covered.From > window.From ? covered.From : window.From,
                    covered.Until < window.Until ? covered.Until : window.Until))
        ];
    }

    // A candle of the interval as the query builds it.
    private sealed record CandleRow
    {
        public DateTimeOffset OpenTime { get; init; }

        public decimal Open { get; init; }

        public decimal High { get; init; }

        public decimal Low { get; init; }

        public decimal Close { get; init; }

        public decimal Volume { get; init; }

        public decimal Turnover { get; init; }
    }
}