using System.Globalization;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Presentation.Settings;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The gRPC feed of funding payments, <c>Protos/funding_payment_feed.proto</c>: a caller subscribes once and gets each of them as it is saved, after
///     the stored ones it asked to catch up on. A backtest reads the history of past days in one call.
/// </summary>
public sealed class FundingPaymentFeedService(
    ISender sender,
    ILiveFeed<InstrumentFundingPaymentInternal> feed,
    IOptionsMonitor<PointFeedSettings> settings,
    FeedCall call,
    HistoryCall history) : FundingPaymentFeed.FundingPaymentFeedBase
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

    public override Task History(FundingPaymentHistoryRequest request, IServerStreamWriter<FundingPaymentHistoryItem> responseStream, ServerCallContext context)
    {
        (Guid[] ids, Exchange[] exchanges, HistoryWindow window) = HistoryCall.Checked(() => (
            HistoryCall.Ids(request.InstrumentIds),
            ExchangeCodes.Asked(request.Exchanges),
            new HistoryWindow(HistoryCall.Time(request.From, "from"), HistoryCall.Time(request.To, "to"), history.Now)));

        return history.ServeAsync<InstrumentFundingPaymentInternal, FundingPaymentHistoryItem>(
            cancellationToken => sender.Send(new GetFundingPaymentHistoryCoverageQuery(ids, exchanges, window), cancellationToken),
            (coverage, cancellationToken) => sender.CreateStream(new GetFundingPaymentHistoryQuery(coverage), cancellationToken),
            coverage => new FundingPaymentHistoryItem { Coverage = HistoryCall.ToMessage(coverage) },
            item => new FundingPaymentHistoryItem { Payment = ToUpdate(item) },
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