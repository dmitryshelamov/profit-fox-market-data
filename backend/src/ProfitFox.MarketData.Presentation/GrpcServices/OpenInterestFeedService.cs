using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of open interest, <c>Protos/open_interest_feed.proto</c>: a caller subscribes once and gets each of them as it is saved, after
///     the stored ones it asked to catch up on.
/// </summary>
public sealed class OpenInterestFeedService(ISender sender, ILiveFeed<InstrumentOpenInterestInternal> feed, IOptionsMonitor<PointFeedSettings> settings, FeedCall call)
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