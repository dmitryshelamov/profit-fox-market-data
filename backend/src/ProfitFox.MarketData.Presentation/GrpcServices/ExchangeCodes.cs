using Grpc.Core;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoExchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The exchanges of the catalog and their codes in the contracts of the feeds, both ways.
/// </summary>
public static class ExchangeCodes
{
    private static readonly Exchange[] All = Enum.GetValues<Exchange>();

    /// <summary>
    ///     The exchanges a request names; all of them when it names none.
    /// </summary>
    /// <exception cref="RpcException">INVALID_ARGUMENT for a code that is no exchange of the catalog, EXCHANGE_UNSPECIFIED among them.</exception>
    public static Exchange[] Asked(IReadOnlyCollection<ProtoExchange> exchanges)
    {
        return exchanges.Count == 0 ? All : [.. exchanges.Select(ToDomain).Distinct()];
    }

    public static ProtoExchange ToProto(Exchange exchange)
    {
        return exchange switch
        {
            Exchange.Bybit => ProtoExchange.Bybit,
            Exchange.TInvest => ProtoExchange.Tinvest,
            _ => throw new NotSupportedException($"The exchange {exchange} has no code in the contract of the feed.")
        };
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
}