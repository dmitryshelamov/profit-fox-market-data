namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     What Bybit says an instrument is: <see cref="Stock" /> a share, <see cref="Etf" /> a fund, <see cref="Commodity" />,
///     <see cref="Forex" /> a currency, <see cref="XStocks" /> a tokenized share, <see cref="Innovation" /> and <see cref="Adventure" /> the
///     zones of Bybit for new tokens. An instrument Bybit gives no type, or a type this catalog does not know, has none.
/// </summary>
public enum BybitSymbolType
{
    Innovation,
    Adventure,
    XStocks,
    Stock,
    Etf,
    Commodity,
    Forex
}