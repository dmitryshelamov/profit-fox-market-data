using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Builds the candles of the interval from the stored minutes with <c>time_bucket</c> and gives only those whose minutes storage all
///     holds: a candle with minutes not loaded yet, such as the one of the interval still open, would tell less than its trades.
/// </summary>
internal sealed class GetCandlesQueryHandler(ICandleSeriesRepository series, NpgsqlConnection connection)
    : IRequestHandler<GetCandlesQuery, CandlesInternal>
{
    public async Task<CandlesInternal> Handle(GetCandlesQuery request, CancellationToken cancellationToken)
    {
        CandleSeries? stored = await series.GetAsync(request.InstrumentId, cancellationToken);

        if (stored is null)
        {
            return new CandlesInternal(Loading: false, [], []);
        }

        MinuteRange range = request.Window.Range;
        TimeSpan length = request.Window.Interval.Length();
        IEnumerable<CandleBucketDb> buckets = await connection.QueryAsync<CandleBucketDb>(
            GetCandlesQueries.Buckets(request.InstrumentId, range, length, cancellationToken));

        return new CandlesInternal(
            Loading: false,
            [.. buckets.Where(bucket => IsCovered(stored, bucket.OpenTime, length)).Select(ToInternal)],
            CoveredIn(range, stored));
    }

    private static bool IsCovered(CandleSeries stored, DateTimeOffset openTime, TimeSpan length)
    {
        return stored.Uncovered(new MinuteRange(openTime, openTime + length)).Count == 0;
    }

    // The covered ranges of the series cut to the range.
    private static IReadOnlyList<MinuteRangeInternal> CoveredIn(MinuteRange range, CandleSeries stored)
    {
        return
        [
            .. stored.Covered
                .Where(covered => covered.From < range.Until && covered.Until > range.From)
                .Select(covered => new MinuteRangeInternal(
                    covered.From > range.From ? covered.From : range.From,
                    covered.Until < range.Until ? covered.Until : range.Until))
        ];
    }

    private static CandleInternal ToInternal(CandleBucketDb bucket)
    {
        return new CandleInternal(bucket.OpenTime, bucket.Open, bucket.High, bucket.Low, bucket.Close, bucket.Volume, bucket.Turnover, bucket.BuyVolume);
    }
}