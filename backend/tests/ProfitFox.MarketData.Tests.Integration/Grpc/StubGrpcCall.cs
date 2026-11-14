using Grpc.Core;

namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     A call <see cref="StubGrpcInvoker" /> answered: the name of the method, the request and the headers it went with.
/// </summary>
internal sealed record StubGrpcCall(string Method, object Request, Metadata Headers);