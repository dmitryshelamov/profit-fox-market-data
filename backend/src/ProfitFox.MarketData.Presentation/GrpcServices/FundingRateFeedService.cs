using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of the funding rates of coming payments, <c>Protos/funding_rate_feed.proto</c>: a caller subscribes once and gets each of them as it is saved, after
///     the stored ones it asked to catch up on.
/// </summary>
public sealed class FundingRateFeedService(ISender sender, ILiveFeed<InstrumentFundingRateInternal> feed, IOptionsMonitor<PointFeedSettings> settings, FeedCall call)
    : FundingRateFeed.FundingRateFeedBase
{
    public override Task Subscribe(SubscribeFundingRateRequest request, IServerStreamWriter<FundingRateUpdate> responseStream, ServerCallContext context)
    {
        return call.ServeAsync(
            request.Since?.ToDateTimeOffset(),
            settings.Get(PointFeedSettings.FundingRateFeed).MaxCatchUpHours,
            feed,
            (since, cancellationToken) => sender.CreateStream(new GetFundingRatesSinceQuery(since), cancellationToken),
            ToUpdate,
            responseStream,
            context);
    }

    // The value goes as text, exactly as stored: protobuf has no decimal type.
    private static FundingRateUpdate ToUpdate(InstrumentFundingRateInternal item)
    {
        return new FundingRateUpdate
        {
            InstrumentId = item.InstrumentId.ToString(),
            Time = Timestamp.FromDateTimeOffset(item.Time),
            FundingRate = item.FundingRate.ToString(CultureInfo.InvariantCulture)
        };
    }
}