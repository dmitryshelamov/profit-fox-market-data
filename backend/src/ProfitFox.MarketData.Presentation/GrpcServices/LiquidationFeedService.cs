using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of liquidations, <c>Protos/liquidation_feed.proto</c>: a caller subscribes once and gets each minute as a change
///     of it is saved, after the stored ones it asked to catch up on.
/// </summary>
public sealed class LiquidationFeedService(
    ISender sender,
    ILiveFeed<InstrumentLiquidationMinuteInternal> feed,
    IOptionsMonitor<PointFeedSettings> settings,
    FeedCall call) : LiquidationFeed.LiquidationFeedBase
{
    public override Task Subscribe(SubscribeLiquidationsRequest request, IServerStreamWriter<LiquidationUpdate> responseStream, ServerCallContext context)
    {
        return call.ServeAsync(
            request.Since?.ToDateTimeOffset(),
            settings.Get(PointFeedSettings.LiquidationFeed).MaxCatchUpHours,
            feed,
            (since, cancellationToken) => sender.CreateStream(new GetLiquidationsSinceQuery(since), cancellationToken),
            ToUpdate,
            responseStream,
            context);
    }

    // A volume and a turnover go as text, exactly as stored: protobuf has no decimal type.
    private static LiquidationUpdate ToUpdate(InstrumentLiquidationMinuteInternal item)
    {
        return new LiquidationUpdate
        {
            InstrumentId = item.InstrumentId.ToString(),
            Time = Timestamp.FromDateTimeOffset(item.Time),
            LongVolume = item.Longs.Volume.ToString(CultureInfo.InvariantCulture),
            LongTurnover = item.Longs.Turnover.ToString(CultureInfo.InvariantCulture),
            LongCount = item.Longs.Count,
            ShortVolume = item.Shorts.Volume.ToString(CultureInfo.InvariantCulture),
            ShortTurnover = item.Shorts.Turnover.ToString(CultureInfo.InvariantCulture),
            ShortCount = item.Shorts.Count,
            IsValid = item.IsValid
        };
    }
}