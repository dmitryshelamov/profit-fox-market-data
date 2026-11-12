using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments;

/// <summary>
///     A share of the main board TQBR of MOEX as T-Invest lists it: <see cref="Uid" /> identifies it, <see cref="Ticker" /> names it, and
///     the rules step the price by its price increment and the quantity, in shares, by its lot.
/// </summary>
public sealed record TInvestListedShare : ListedInstrument
{
    /// <summary>
    ///     The id of the share in T-Invest; it stays when the ticker changes, and the candles of T-Invest are asked by it.
    /// </summary>
    public string Uid { get; }

    /// <summary>
    ///     The ticker of the share on TQBR: SBER.
    /// </summary>
    public string Ticker { get; }

    public InstrumentRules Rules { get; }

    public TInvestListedShare(string uid, string ticker, InstrumentRules rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        Uid = uid;
        Ticker = ticker;
        Rules = rules;
    }
}