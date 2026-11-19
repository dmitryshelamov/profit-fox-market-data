// Times the History calls of one instrument as the hypothesis bench makes them, and hashes what each tells, so a change of the
// service can be checked to be faster and to tell the same.
//
// Usage: dotnet run -c Release -- [address] [instrument id] [repeats]
// Each read runs once to warm up, then the given number of times; it prints the messages told, the median and the minimum time,
// and the SHA-256 of the messages in the order they came.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using ProfitFox.MarketData.Presentation.Protos;

string address = args.Length > 0 ? args[0] : "http://localhost:5237";
string instrumentId = args.Length > 1 ? args[1] : "00000000-0000-0000-0000-000000000001";
int repeats = args.Length > 2 ? int.Parse(args[2]) : 3;

using GrpcChannel channel = GrpcChannel.ForAddress(address);
var candles = new CandleFeed.CandleFeedClient(channel);
var funding = new FundingPaymentFeed.FundingPaymentFeedClient(channel);

(string Name, Func<Task<(int Messages, string Hash)>> Read)[] reads =
[
    ("4h 2023-01-01..2026-05-16", () => Candles(CandleInterval.FourHours, "2023-01-01", "2026-05-16")),
    ("4h 2025-11-01..2026-05-16", () => Candles(CandleInterval.FourHours, "2025-11-01", "2026-05-16")),
    ("funding 2023-01-01..2026-05-16", () => Funding("2023-01-01", "2026-05-16")),
    ("1m 2026-03-01..2026-03-08", () => Candles(CandleInterval.OneMinute, "2026-03-01", "2026-03-08"))
];

Console.WriteLine($"{address} {instrumentId}, {repeats} runs after a warm-up");

foreach ((string name, Func<Task<(int Messages, string Hash)>> read) in reads)
{
    await read();
    var times = new List<double>();
    (int Messages, string Hash) last = default;

    for (int run = 0; run < repeats; run++)
    {
        var watch = Stopwatch.StartNew();
        last = await read();
        times.Add(watch.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    Console.WriteLine($"{name,-32} messages={last.Messages,8} median={times[times.Count / 2],8:F0} ms min={times[0],8:F0} ms sha256={last.Hash}");
}

return;

Task<(int, string)> Candles(CandleInterval interval, string from, string to)
{
    var request = new CandleHistoryRequest { InstrumentIds = { instrumentId }, From = Day(from), To = Day(to), Interval = interval };

    return HashAsync(candles.History(request));
}

Task<(int, string)> Funding(string from, string to)
{
    var request = new FundingPaymentHistoryRequest { InstrumentIds = { instrumentId }, From = Day(from), To = Day(to) };

    return HashAsync(funding.History(request));
}

// The messages are hashed by their JSON form, which writes the fields in the order of the contract, not by their bytes.
static async Task<(int, string)> HashAsync<T>(AsyncServerStreamingCall<T> call)
    where T : IMessage
{
    using (call)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int messages = 0;

        await foreach (T message in call.ResponseStream.ReadAllAsync())
        {
            messages++;
            hash.AppendData(Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(message)));
        }

        return (messages, Convert.ToHexString(hash.GetHashAndReset()));
    }
}

static Timestamp Day(string day)
{
    return Timestamp.FromDateTimeOffset(DateTimeOffset.Parse(day + "T00:00:00Z"));
}
