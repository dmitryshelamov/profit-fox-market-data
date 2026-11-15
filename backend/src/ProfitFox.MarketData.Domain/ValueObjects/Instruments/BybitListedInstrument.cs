using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments;

/// <summary>
///     An instrument Bybit lists: its symbol, the assets it trades, whether it trades and the rules of an order; <see cref="Category" /> and
///     <see cref="Symbol" /> identify it. A linear contract tells its <see cref="ContractType" /> and a spot pair has none.
/// </summary>
public sealed record BybitListedInstrument : ListedInstrument
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

    public BybitInstrumentStatus Status { get; }

    public InstrumentRules Rules { get; }

    /// <summary>
    ///     Perpetual or futures on linear; none on spot.
    /// </summary>
    public BybitContractType? ContractType { get; }

    /// <summary>
    ///     Since when the instrument trades, if Bybit tells (not on spot).
    /// </summary>
    public DateTimeOffset? LaunchTime { get; }

    public override Exchange Exchange => Exchange.Bybit;

    /// <exception cref="ArgumentException">A name is blank, a linear contract has no contract type, or a spot pair has one.</exception>
    public BybitListedInstrument(
        BybitCategory category,
        string symbol,
        string baseAsset,
        string quoteAsset,
        BybitInstrumentStatus status,
        InstrumentRules rules,
        BybitContractType? contractType = null,
        DateTimeOffset? launchTime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAsset);
        ArgumentException.ThrowIfNullOrWhiteSpace(quoteAsset);

        if (category == BybitCategory.Linear != contractType is not null)
        {
            throw new ArgumentException($"A linear contract tells its contract type and a spot pair has none; {category} {symbol} does not.", nameof(contractType));
        }

        Category = category;
        Symbol = symbol;
        BaseAsset = baseAsset;
        QuoteAsset = quoteAsset;
        Status = status;
        Rules = rules;
        ContractType = contractType;
        LaunchTime = launchTime;
    }
}