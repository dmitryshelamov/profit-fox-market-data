using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of open interest, <c>Protos/open_interest_feed.proto</c>: a caller subscribes once and gets each of them as it is saved, after
///     the stored ones it asked to catch up on. A backtest reads the history of past days in one call.
/// </summary>
public sealed class OpenInterestFeedService(
    ISender sender,
    ILiveFeed<InstrumentOpenInterestInternal> feed,
    IOptionsMonitor<PointFeedSettings> settings,
    FeedCall call,
    HistoryCall history)
    : OpenInterestFeed.OpenInterestFeedBase
{
    public override Task Subscribe(SubscribeOpenInterestRequest request, IServerStreamWriter<OpenInterestUpdate> responseStream, ServerCallContext context)
    {
        return call.ServeAsync(
            request.Since?.ToDateTimeOffset(),
            settings.Get(PointFeedSettings.OpenInterestFeed).MaxCatchUpHours,
            feed,
            (since, cancellationToken) => sender.CreateStream(new GetOpenInterestSinceQuery(since), cancellationToken),
            ToUpdate,
            responseStream,
            context);
    }

    public override Task History(OpenInterestHistoryRequest request, IServerStreamWriter<OpenInterestHistoryItem> responseStream, ServerCallContext context)
    {
        (Guid[] ids, Exchange[] exchanges, HistoryWindow window) = HistoryCall.Checked(() => (
            HistoryCall.Ids(request.InstrumentIds),
            ExchangeCodes.Asked(request.Exchanges),
            new HistoryWindow(HistoryCall.Time(request.From, "from"), HistoryCall.Time(request.To, "to"), history.Now)));

        return history.ServeAsync<InstrumentOpenInterestInternal, OpenInterestHistoryItem>(
            cancellationToken => sender.Send(new GetOpenInterestHistoryCoverageQuery(ids, exchanges, window), cancellationToken),
            (coverage, cancellationToken) => sender.CreateStream(new GetOpenInterestHistoryQuery(coverage), cancellationToken),
            coverage => new OpenInterestHistoryItem { Coverage = HistoryCall.ToMessage(coverage) },
            item => new OpenInterestHistoryItem { Point = ToUpdate(item) },
            responseStream,
            context);
    }

    // The value goes as text, exactly as stored: protobuf has no decimal type.
    private static OpenInterestUpdate ToUpdate(InstrumentOpenInterestInternal item)
    {
        return new OpenInterestUpdate
        {
            InstrumentId = item.InstrumentId.ToString(),
            Time = Timestamp.FromDateTimeOffset(item.Time),
            OpenInterest = item.OpenInterest.ToString(CultureInfo.InvariantCulture)
        };
    }
}