using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments;

/// <summary>
///     An instrument Bybit lists: its symbol, the coins it trades, whether it trades and the rules of an order; <see cref="Category" /> and
///     <see cref="Symbol" /> identify it. A contract of linear or inverse tells its <see cref="ContractType" />, which must belong to its
///     category; spot and option have none.
/// </summary>
public sealed record BybitListedInstrument
{
    public BybitCategory Category { get; }

    public string Symbol { get; }

    /// <summary>
    ///     The asset being traded: BTC of BTCUSDT.
    /// </summary>
    public string BaseAsset { get; }

    /// <summary>
    ///     The asset the price is quoted in: USDT of BTCUSDT.
    /// </summary>
    public string QuoteAsset { get; }

    public InstrumentStatus Status { get; }

    public InstrumentRules Rules { get; }

    /// <summary>
    ///     Perpetual or futures, linear or inverse; none on spot and option.
    /// </summary>
    public BybitContractType? ContractType { get; }

    /// <summary>
    ///     Since when the instrument trades, if Bybit tells (not on spot).
    /// </summary>
    public DateTimeOffset? LaunchTime { get; }

    public BybitListedInstrument(
        BybitCategory category,
        string symbol,
        string baseCoin,
        string quoteCoin,
        InstrumentStatus status,
        InstrumentRules rules,
        BybitContractType? contractType = null,
        DateTimeOffset? launchTime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteCoin);

        EnsureContractTypeMatchesCategory(category, contractType);

        Category = category;
        Symbol = symbol;
        BaseAsset = baseCoin;
        QuoteAsset = quoteCoin;
        Status = status;
        Rules = rules;
        ContractType = contractType;
        LaunchTime = launchTime;
    }

    private static void EnsureContractTypeMatchesCategory(BybitCategory category, BybitContractType? contractType)
    {
        bool isContract = category is BybitCategory.Linear or BybitCategory.Inverse;

        if (contractType is null)
        {
            if (isContract)
            {
                throw new ArgumentException($"A {category} instrument needs a contract type.", nameof(contractType));
            }

            return;
        }

        if (contractType.Value.Category() != category)
        {
            throw new ArgumentException($"Contract type {contractType} does not belong to {category}.", nameof(contractType));
        }
    }
}
