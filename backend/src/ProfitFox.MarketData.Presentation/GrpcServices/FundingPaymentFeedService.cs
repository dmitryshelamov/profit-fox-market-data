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
///     The gRPC feed of funding payments, <c>Protos/funding_payment_feed.proto</c>: a caller subscribes once and gets each of them as it is saved, after
///     the stored ones it asked to catch up on.
/// </summary>
public sealed class FundingPaymentFeedService(
    ISender sender,
    ILiveFeed<InstrumentFundingPaymentInternal> feed,
    IOptionsMonitor<PointFeedSettings> settings,
    FeedCall call) : FundingPaymentFeed.FundingPaymentFeedBase
{
    public override Task Subscribe(SubscribeFundingPaymentRequest request, IServerStreamWriter<FundingPaymentUpdate> responseStream, ServerCallContext context)
    {
        return call.ServeAsync(
            request.Since?.ToDateTimeOffset(),
            settings.Get(PointFeedSettings.FundingPaymentFeed).MaxCatchUpHours,
            feed,
            (since, cancellationToken) => sender.CreateStream(new GetFundingPaymentsSinceQuery(since), cancellationToken),
            ToUpdate,
            responseStream,
            context);
    }

    // The value goes as text, exactly as stored: protobuf has no decimal type.
    private static FundingPaymentUpdate ToUpdate(InstrumentFundingPaymentInternal item)
    {
        return new FundingPaymentUpdate
        {
            InstrumentId = item.InstrumentId.ToString(),
            Time = Timestamp.FromDateTimeOffset(item.Time),
            FundingRate = item.FundingRate.ToString(CultureInfo.InvariantCulture)
        };
    }
}