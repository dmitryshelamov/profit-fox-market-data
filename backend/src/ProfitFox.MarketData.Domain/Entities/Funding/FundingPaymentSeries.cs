using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Domain.Entities.Funding;

/// <summary>
///     The funding payments of one instrument of the catalog as far as storage holds them: every payment the exchange made from
///     <see cref="CoveredFrom" /> up to <see cref="CoveredUntil" /> is stored, so a time without a payment there had none (the time
///     before the instrument was listed, the hours between two payments) and is never asked for again. Storage writes
///     <see cref="NewPayments" /> with the coverage in one transaction, so the coverage never claims a time storage does not hold.
///     The coverage is one stretch of time. A loaded range that touches it widens it; a newer range apart from it takes its place, and the
///     series is loaded back from there again, so no hole hides inside the coverage. That is unlike an
///     <see cref="OpenInterest.OpenInterestSeries" />: a page of payments is a number of them, the newest of a range, so after a long
///     downtime the time next to the coverage cannot be asked for first.
/// </summary>
public sealed class FundingPaymentSeries : AggregateRoot
{
    private readonly List<FundingPayment> _newPayments = [];

    /// <summary>
    ///     The id of the instrument in the catalog the payments are of; one series per instrument.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset CoveredFrom { get; private set; }

    /// <summary>
    ///     The end of the coverage, itself outside it.
    /// </summary>
    public DateTimeOffset CoveredUntil { get; private set; }

    /// <summary>
    ///     The payments of the ranges covered since the series was read from storage, which storage writes together with the coverage.
    /// </summary>
    public IReadOnlyCollection<FundingPayment> NewPayments => _newPayments.AsReadOnly();

    private FundingPaymentSeries(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        InstrumentId = instrumentId;
        CoveredFrom = coveredFrom;
        CoveredUntil = coveredUntil;
    }

    /// <summary>
    ///     The series of an instrument, started by the first range loaded for it: <c>[from, until)</c> with the payments the exchange made
    ///     in it, an empty list for a range without payments.
    /// </summary>
    /// <exception cref="ArgumentException">The range does not end after it starts, or a payment is outside it.</exception>
    public static FundingPaymentSeries Create(Guid instrumentId, DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<FundingPayment> payments)
    {
        EnsureInside(from, until, payments);

        var series = new FundingPaymentSeries(instrumentId, from, until);
        series._newPayments.AddRange(payments);

        return series;
    }

    /// <summary>
    ///     The series as storage holds it, with no new payments.
    /// </summary>
    public static FundingPaymentSeries Restore(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        return new FundingPaymentSeries(instrumentId, coveredFrom, coveredUntil);
    }

    /// <summary>
    ///     Takes a loaded range <c>[from, until)</c> with the payments the exchange made in it, an empty list for a range without
    ///     payments. A range that touches the coverage widens it. A newer range apart from it becomes the coverage: the payments stored
    ///     before stay, but the time between is not known to be whole, so it is loaded again from the new range back.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The range does not end after it starts, a payment is outside it, or it is older than the coverage and apart from it: the newer
    ///     stretch is the one a series keeps.
    /// </exception>
    public void Cover(DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<FundingPayment> payments)
    {
        EnsureInside(from, until, payments);

        if (until < CoveredFrom)
        {
            throw new ArgumentException(
                $"The range [{from:O}, {until:O}) is older than the coverage [{CoveredFrom:O}, {CoveredUntil:O}) and apart from it.",
                nameof(until));
        }

        bool touches = from <= CoveredUntil;

        CoveredFrom = touches && CoveredFrom < from ? CoveredFrom : from;
        CoveredUntil = touches && CoveredUntil > until ? CoveredUntil : until;
        _newPayments.AddRange(payments);
    }

    // A payment outside the range would be stored without the coverage over it, or would stand for a time the range was not asked for.
    private static void EnsureInside(DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<FundingPayment> payments)
    {
        if (until <= from)
        {
            throw new ArgumentException($"The range [{from:O}, {until:O}) does not end after it starts.", nameof(until));
        }

        foreach (FundingPayment payment in payments)
        {
            if (payment.Time < from || payment.Time >= until)
            {
                throw new ArgumentException($"A payment of {payment.Time:O} is outside the range [{from:O}, {until:O}).", nameof(payments));
            }
        }
    }
}