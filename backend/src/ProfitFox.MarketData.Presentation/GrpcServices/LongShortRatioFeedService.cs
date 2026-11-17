using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of the long/short ratio, <c>Protos/long_short_ratio_feed.proto</c>: a caller subscribes once and gets each new
///     point as it is saved, after the stored ones it asked to catch up on. A backtest reads the history of past days in one call.
/// </summary>
public sealed class LongShortRatioFeedService(
    ISender sender,
    ILiveFeed<InstrumentLongShortRatioInternal> feed,
    IOptionsMonitor<PointFeedSettings> settings,
    FeedCall call,
    HistoryCall history) : LongShortRatioFeed.LongShortRatioFeedBase
{
    public override Task Subscribe(SubscribeLongShortRatioRequest request, IServerStreamWriter<LongShortRatioUpdate> responseStream, ServerCallContext context)
    {
        return call.ServeAsync(
            request.Since?.ToDateTimeOffset(),
            settings.Get(PointFeedSettings.LongShortRatioFeed).MaxCatchUpHours,
            feed,
            (since, cancellationToken) => sender.CreateStream(new GetLongShortRatioSinceQuery(since), cancellationToken),
            ToUpdate,
            responseStream,
            context);
    }

    public override Task History(LongShortRatioHistoryRequest request, IServerStreamWriter<LongShortRatioHistoryItem> responseStream, ServerCallContext context)
    {
        (Guid[] ids, Exchange[] exchanges, HistoryWindow window) = HistoryCall.Checked(() => (
            HistoryCall.Ids(request.InstrumentIds),
            ExchangeCodes.Asked(request.Exchanges),
            new HistoryWindow(HistoryCall.Time(request.From, "from"), HistoryCall.Time(request.To, "to"), history.Now)));

        return history.ServeAsync<InstrumentLongShortRatioInternal, LongShortRatioHistoryItem>(
            cancellationToken => sender.Send(new GetLongShortRatioHistoryCoverageQuery(ids, exchanges, window), cancellationToken),
            (coverage, cancellationToken) => sender.CreateStream(new GetLongShortRatioHistoryQuery(coverage), cancellationToken),
            coverage => new LongShortRatioHistoryItem { Coverage = HistoryCall.ToMessage(coverage) },
            item => new LongShortRatioHistoryItem { Point = ToUpdate(item) },
            responseStream,
            context);
    }

    // A share goes as text, exactly as stored: protobuf has no decimal type.
    private static LongShortRatioUpdate ToUpdate(InstrumentLongShortRatioInternal item)
    {
        return new LongShortRatioUpdate
        {
            InstrumentId = item.InstrumentId.ToString(),
            Time = Timestamp.FromDateTimeOffset(item.Time),
            Long = item.Long.ToString(CultureInfo.InvariantCulture),
            Short = item.Short.ToString(CultureInfo.InvariantCulture)
        };
    }
}