using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using CandleInterval = ProfitFox.MarketData.Application.Queries.Candles.CandleInterval;
using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoInterval = ProfitFox.MarketData.Presentation.Protos.CandleInterval;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of minute candles, <c>Protos/candle_feed.proto</c>: a caller subscribes once and gets every candle as it is
///     saved, after the stored ones it asked to catch up on, and the forming candle of the minute that goes on when it asked for it.
///     A backtest reads the history of the candles of past days in one call.
/// </summary>
public sealed class CandleFeedService(
    ISender sender,
    ILiveCandleFeed feed,
    IOptionsMonitor<CandleFeedSettings> settings,
    FeedCall call,
    HistoryCall history)
    : CandleFeed.CandleFeedBase
{
    public override Task Subscribe(SubscribeRequest request, IServerStreamWriter<CandleUpdate> responseStream, ServerCallContext context)
    {
        Exchange[] exchanges = ExchangeCodes.Asked(request.Exchanges);

        // A candle is stored closed and comes live closed or forming, so each has a message of its own making.
        return call.ServeAsync<ClosedCandleInternal, LiveCandle, CandleUpdate>(
            request.Since?.ToDateTimeOffset(),
            settings.CurrentValue.MaxCatchUpHours,
            () => feed.Subscribe(exchanges, request.Forming),
            (since, cancellationToken) => sender.CreateStream(new GetClosedCandlesQuery(since, exchanges), cancellationToken),
            ToUpdate,
            ToUpdate,
            responseStream,
            context);
    }

    public override Task History(CandleHistoryRequest request, IServerStreamWriter<CandleHistoryItem> responseStream, ServerCallContext context)
    {
        (Guid[] ids, Exchange[] exchanges, CandleHistoryWindow window) = HistoryCall.Checked(() => (
            HistoryCall.Ids(request.InstrumentIds),
            ExchangeCodes.Asked(request.Exchanges),
            new CandleHistoryWindow(ToDomain(request.Interval), HistoryCall.Time(request.From, "from"), HistoryCall.Time(request.To, "to"), history.Now)));

        return history.ServeAsync<ClosedCandleInternal, CandleHistoryItem>(
            cancellationToken => sender.Send(new GetCandleHistoryCoverageQuery(ids, exchanges, window), cancellationToken),
            (coverage, cancellationToken) => sender.CreateStream(new GetCandleHistoryQuery(coverage, window.Interval), cancellationToken),
            coverage => new CandleHistoryItem { Coverage = HistoryCall.ToMessage(coverage) },
            candle => new CandleHistoryItem { Candle = ToUpdate(candle) },
            responseStream,
            context);
    }

    /// <exception cref="ArgumentException">The interval is CANDLE_INTERVAL_UNSPECIFIED or not one of the contract.</exception>
    private static CandleInterval ToDomain(ProtoInterval interval)
    {
        return interval switch
        {
            ProtoInterval.OneMinute => CandleInterval.OneMinute,
            ProtoInterval.FiveMinutes => CandleInterval.FiveMinutes,
            ProtoInterval.FifteenMinutes => CandleInterval.FifteenMinutes,
            ProtoInterval.OneHour => CandleInterval.OneHour,
            ProtoInterval.FourHours => CandleInterval.FourHours,
            ProtoInterval.OneDay => CandleInterval.OneDay,
            _ => throw new ArgumentException($"The interval {interval} is not known.")
        };
    }

    private static CandleUpdate ToUpdate(ClosedCandleInternal stored)
    {
        return ToUpdate(
            stored.Exchange,
            stored.InstrumentId,
            new Candle(stored.OpenTime, stored.Open, stored.High, stored.Low, stored.Close, stored.Volume, stored.Turnover, stored.BuyVolume),
            closed: true);
    }

    private static CandleUpdate ToUpdate(LiveCandle live)
    {
        return ToUpdate(live.Exchange, live.InstrumentId, live.Candle, live.Closed);
    }

    private static CandleUpdate ToUpdate(Exchange exchange, Guid instrumentId, Candle candle, bool closed)
    {
        var update = new CandleUpdate
        {
            InstrumentId = instrumentId.ToString(),
            Exchange = ExchangeCodes.ToProto(exchange),
            OpenTime = Timestamp.FromDateTimeOffset(candle.OpenTime),
            Open = Text(candle.Open),
            High = Text(candle.High),
            Low = Text(candle.Low),
            Close = Text(candle.Close),
            Volume = Text(candle.Volume),
            Closed = closed
        };

        if (candle.Turnover is { } turnover)
        {
            update.Turnover = Text(turnover);
        }

        if (candle.BuyVolume is { } buyVolume)
        {
            update.BuyVolume = Text(buyVolume);
        }

        return update;
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}