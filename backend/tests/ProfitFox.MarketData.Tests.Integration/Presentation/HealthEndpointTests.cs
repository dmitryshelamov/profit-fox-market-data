using System.Net;

namespace ProfitFox.MarketData.Tests.Integration.Presentation;

[TestFixture]
public class HealthEndpointTests
{
    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _client = _app.CreateClient();
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;

    // The deploy waits for this answer.
    [Test]
    public async Task A_started_app_over_a_database_that_answers_is_healthy()
    {
        HttpResponseMessage health = await _client.GetAsync("health");

        Assert.Multiple(async () =>
        {
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await health.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
        });
    }
}