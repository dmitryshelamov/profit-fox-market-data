namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

public static class BybitContractTypeExtensions
{
    /// <summary>
    ///     The category a contract is listed in: it follows from the contract type.
    /// </summary>
    public static BybitCategory Category(this BybitContractType type)
    {
        return type is BybitContractType.LinearPerpetual or BybitContractType.LinearFutures ? BybitCategory.Linear : BybitCategory.Inverse;
    }
}