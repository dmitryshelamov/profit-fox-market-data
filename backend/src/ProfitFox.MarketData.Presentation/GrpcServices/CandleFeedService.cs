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

using Enum = System.Enum;
using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoExchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of minute candles, <c>Protos/candle_feed.proto</c>: a caller subscribes once and gets every candle as it is
///     saved, after the stored ones it asked to catch up on, and the forming candle of the minute that goes on when it asked for it.
/// </summary>
public sealed class CandleFeedService(ISender sender, ILiveCandleFeed feed, IOptionsMonitor<CandleFeedSettings> settings, FeedCall call)
    : CandleFeed.CandleFeedBase
{
    private static readonly Exchange[] All = Enum.GetValues<Exchange>();

    public override Task Subscribe(SubscribeRequest request, IServerStreamWriter<CandleUpdate> responseStream, ServerCallContext context)
    {
        Exchange[] exchanges = Asked(request);

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

    // The exchanges asked for; all of them when none is named.
    private static Exchange[] Asked(SubscribeRequest request)
    {
        return request.Exchanges.Count == 0 ? All : [.. request.Exchanges.Select(ToDomain).Distinct()];
    }

    private static Exchange ToDomain(ProtoExchange exchange)
    {
        return exchange switch
        {
            ProtoExchange.Bybit => Exchange.Bybit,
            ProtoExchange.Tinvest => Exchange.TInvest,
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"The exchange {exchange} is not known."))
        };
    }

    private static ProtoExchange ToProto(Exchange exchange)
    {
        return exchange switch
        {
            Exchange.Bybit => ProtoExchange.Bybit,
            Exchange.TInvest => ProtoExchange.Tinvest,
            _ => throw new NotSupportedException($"The exchange {exchange} has no code in the contract of the feed.")
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
            Exchange = ToProto(exchange),
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