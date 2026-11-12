using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Contracts.Instruments;

[TestFixture]
public class InstrumentInternalTests
{
    private const string Uid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Test]
    public void A_listing_of_bybit_is_shown_as_bybit()
    {
        Instrument instrument = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

        JsonNode listing = JsonSerializer.SerializeToNode(instrument.ToInternal(), Json)!["listing"]!;

        Assert.Multiple(() =>
        {
            Assert.That(listing["exchange"]!.GetValue<string>(), Is.EqualTo("bybit"));
            Assert.That(listing["symbol"]!.GetValue<string>(), Is.EqualTo("BTCUSDT"));
        });
    }

    [Test]
    public void A_share_of_tinvest_is_shown_with_its_uid_ticker_and_rules()
    {
        Instrument instrument = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate("SBER", tickSize: 0.01m, Uid));

        JsonNode listing = JsonSerializer.SerializeToNode(instrument.ToInternal(), Json)!["listing"]!;

        Assert.Multiple(() =>
        {
            Assert.That(listing["exchange"]!.GetValue<string>(), Is.EqualTo("tinvest"));
            Assert.That(listing["uid"]!.GetValue<string>(), Is.EqualTo(Uid));
            Assert.That(listing["ticker"]!.GetValue<string>(), Is.EqualTo("SBER"));
            Assert.That(listing["rules"]!["tickSize"]!.GetValue<decimal>(), Is.EqualTo(0.01m));
            Assert.That(listing["rules"]!["quantityStep"]!.GetValue<decimal>(), Is.EqualTo(10m));
        });
    }

    [Test]
    public void The_exchange_of_a_listing_brings_back_its_type()
    {
        InstrumentInternal shown = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate(uid: Uid)).ToInternal();

        InstrumentInternal? read = JsonSerializer.Deserialize<InstrumentInternal>(JsonSerializer.Serialize(shown, Json), Json);

        Assert.That(read!.Listing, Is.EqualTo(shown.Listing));
    }
}