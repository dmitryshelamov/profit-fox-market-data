using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Contracts.Candles.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoExchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of closed minute candles, <c>Protos/candle_feed.proto</c>: a caller subscribes once and gets every candle as it is
///     saved, after the stored ones it asked to catch up on.
/// </summary>
public sealed class CandleFeedService(ISender sender, IClosedCandleFeed feed, IOptionsMonitor<CandleFeedSettings> settings, TimeProvider time)
    : CandleFeed.CandleFeedBase
{
    // The exchanges whose candles are told as they close. T-Invest is not among them until it has a stream of its own: its candles
    // come by polling, a minute or two late, and catching up on them from storage would then stop without a word.
    private static readonly Exchange[] Streamed = [Exchange.Bybit];

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<CandleUpdate> responseStream, ServerCallContext context)
    {
        CancellationToken cancellationToken = context.CancellationToken;
        Exchange[] exchanges = Asked(request);
        var since = request.Since?.ToDateTimeOffset();
        int maxCatchUpHours = settings.CurrentValue.MaxCatchUpHours;

        if (since < time.GetUtcNow().AddHours(-maxCatchUpHours))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The feed catches up on at most {maxCatchUpHours} hours; since is {since:O}."));
        }

        // Subscribed before storage is read: a candle saved meanwhile waits in the subscription, so none is left out between the two.
        // It may come twice, from storage and live, which the caller takes as one: it replaces a candle by instrument and open time.
        using IClosedCandleSubscription live = feed.Subscribe();
        await context.WriteResponseHeadersAsync([]);

        if (since is not null)
        {
            await foreach (ClosedCandleInternal candle in sender.CreateStream(new GetClosedCandlesQuery(since.Value, exchanges), cancellationToken))
            {
                await responseStream.WriteAsync(ToUpdate(candle), cancellationToken);
            }
        }

        try
        {
            await foreach (ClosedCandle closed in live.ReadAllAsync(cancellationToken))
            {
                if (exchanges.Contains(closed.Exchange))
                {
                    await responseStream.WriteAsync(ToUpdate(closed.ToInternal()), cancellationToken);
                }
            }
        }
        catch (SubscriberFellBehindException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
    }

    // The streamed exchanges among the ones asked for; all of the streamed when none is named.
    private static Exchange[] Asked(SubscribeRequest request)
    {
        if (request.Exchanges.Count == 0)
        {
            return Streamed;
        }

        Exchange[] asked = [.. request.Exchanges.Select(ToDomain)];

        return [.. Streamed.Where(asked.Contains)];
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
            _ => ProtoExchange.Unspecified
        };
    }

    private static CandleUpdate ToUpdate(ClosedCandleInternal candle)
    {
        var update = new CandleUpdate
        {
            InstrumentId = candle.InstrumentId.ToString(),
            Exchange = ToProto(candle.Exchange),
            OpenTime = Timestamp.FromDateTimeOffset(candle.OpenTime),
            Open = Text(candle.Open),
            High = Text(candle.High),
            Low = Text(candle.Low),
            Close = Text(candle.Close),
            Volume = Text(candle.Volume),
            Closed = true
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