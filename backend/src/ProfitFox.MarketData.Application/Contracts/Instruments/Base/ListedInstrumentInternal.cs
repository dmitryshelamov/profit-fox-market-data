using System.Text.Json.Serialization;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Base;

/// <summary>
///     The listing of an instrument as the API shows it; <c>exchange</c> in the JSON tells which exchange it is of: <c>bybit</c> or
///     <c>tinvest</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "exchange")]
[JsonDerivedType(typeof(BybitListedInstrumentInternal), "bybit")]
[JsonDerivedType(typeof(TInvestListedShareInternal), "tinvest")]
public abstract record ListedInstrumentInternal;