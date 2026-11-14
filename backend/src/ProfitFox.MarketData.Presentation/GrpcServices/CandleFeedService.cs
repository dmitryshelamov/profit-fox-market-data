using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoExchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of minute candles, <c>Protos/candle_feed.proto</c>: a caller subscribes once and gets every candle as it is
///     saved, after the stored ones it asked to catch up on, and the forming candle of the minute that goes on when it asked for it.
/// </summary>
public sealed class CandleFeedService(
    ISender sender,
    ILiveCandleFeed feed,
    IOptionsMonitor<CandleFeedSettings> settings,
    IHostApplicationLifetime lifetime,
    TimeProvider time) : CandleFeed.CandleFeedBase
{
    private static readonly Exchange[] All = System.Enum.GetValues<Exchange>();

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<CandleUpdate> responseStream, ServerCallContext context)
    {
        Exchange[] exchanges = Asked(request);
        var since = request.Since?.ToDateTimeOffset();
        int maxCatchUpHours = settings.CurrentValue.MaxCatchUpHours;

        if (since is not null && since < time.GetUtcNow().AddHours(-maxCatchUpHours))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The feed catches up on at most {maxCatchUpHours} hours; since is {since:O}."));
        }

        // The call ends with the app as well as with its caller: the server waits for the open calls when it stops, and one only its
        // caller ends would hold the stop back until the server gives up.
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);

        try
        {
            await TellAsync(exchanges, since, request.Forming, responseStream, context, ended.Token);
        }
        catch (SubscriberFellBehindException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The service is stopping."));
        }
    }

    private async Task TellAsync(
        Exchange[] exchanges,
        DateTimeOffset? since,
        bool forming,
        IServerStreamWriter<CandleUpdate> responseStream,
        ServerCallContext context,
        CancellationToken cancellationToken)
    {
        // Subscribed before storage is read: a candle saved meanwhile waits in the subscription, so none is left out between the two.
        // It may come twice, from storage and live, which the caller takes as one: it replaces a candle by instrument and open time.
        using ILiveCandleSubscription live = feed.Subscribe(exchanges, forming);
        await context.WriteResponseHeadersAsync([]);

        if (since is not null)
        {
            await foreach (ClosedCandleInternal stored in sender.CreateStream(new GetClosedCandlesQuery(since.Value, exchanges), cancellationToken))
            {
                await responseStream.WriteAsync(ToUpdate(stored), cancellationToken);
            }
        }

        await foreach (LiveCandle candle in live.ReadAllAsync(cancellationToken))
        {
            await responseStream.WriteAsync(ToUpdate(candle), cancellationToken);
        }
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