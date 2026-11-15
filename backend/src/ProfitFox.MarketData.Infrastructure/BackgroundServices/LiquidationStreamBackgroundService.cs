using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;
using ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Ports.Liquidations;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     Sums the liquidations of every listed linear contract of Bybit by the minute while <see cref="LiquidationStreamSettings" />
///     enable Bybit, and saves what was heard every <see cref="LiquidationStreamSettings.SaveSeconds" />: the sums are added to the
///     stored ones of their minutes. It subscribes to the listed contracts of the catalog and reads the catalog again every
///     <see cref="LiquidationStreamSettings.RefreshSeconds" /> for the ones listed since. A delisted contract stays subscribed until
///     the app restarts.
///     Bybit keeps no history of liquidations, so a minute that was not heard in full is marked not valid, with a row even when
///     nothing was heard of it. A contract is not heard from the latest minute storage held when the listening started, or from when
///     the catalog first saw the contract if that is later, until Bybit takes its subscription, and from the minute before its
///     connection was found lost until the connection is back. While a contract is not heard its minutes are marked at every save, so a
///     stop of the app leaves no minute unmarked. A save that failed has its minutes marked too: whether storage took the sums is
///     not known.
/// </summary>
internal sealed class LiquidationStreamBackgroundService(
    IServiceScopeFactory scopes,
    IBybitLiquidationStreamClient bybit,
    IOptionsMonitor<LiquidationStreamSettings> settings,
    TimeProvider time,
    ILogger<LiquidationStreamBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    // The listeners of the stream run on its threads, the saving on the one of the service.
    private readonly Lock _lock = new Lock();

    // The sums heard since the last save.
    private readonly Dictionary<(Guid InstrumentId, DateTimeOffset Time), LiquidationMinute> _heard = [];

    // The contracts not heard now, each with the minute its minutes are not marked from yet.
    private readonly Dictionary<Guid, DateTimeOffset> _unheard = [];

    // The contracts whose connection is lost: the end of a subscribing does not make them heard.
    private readonly HashSet<Guid> _lost = [];

    // The minutes to mark of contracts that are heard again, and of saves that failed.
    private readonly List<Gap> _gaps = [];

    // The symbols subscribed to, and their subscriptions.
    private readonly HashSet<string> _symbols = [];

    private readonly List<IAsyncDisposable> _subscriptions = [];

    // The minute nobody listened from when the listening started; null while it has not.
    private DateTimeOffset? _since;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(SubscribeAsync(stoppingToken), SaveAsync(stoppingToken));
    }

    // Subscribes to what the catalog lists and is not subscribed to yet, again and again; the subscriptions end with the service.
    private async Task SubscribeAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        LiquidationStreamSettings stream = settings.CurrentValue;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (stream.Bybit.Enabled)
                {
                    await SubscribeToBybitAsync(stoppingToken);
                }
                else
                {
                    await UnsubscribeAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(stream.RefreshSeconds), time, stoppingToken);

                stream = settings.CurrentOrLastValid(stream, logger);
            }
        }
        finally
        {
            await UnsubscribeAsync();
        }
    }

    // One subscription for the listed linear contracts not subscribed to yet: all of them at the first pass, later only the ones listed
    // since and the ones Bybit did not take.
    private async Task SubscribeToBybitAsync(CancellationToken stoppingToken)
    {
        if (await SinceAsync(stoppingToken) is not { } since)
        {
            return;
        }

        IReadOnlyCollection<InstrumentInternal> instruments = await scopes.InstrumentsOrNoneAsync(Exchange.Bybit, logger, stoppingToken);

        (Guid Id, DateTimeOffset FirstSeenAt, string Symbol)[] listed =
        [
            .. instruments
                .Where(instrument => instrument.DelistedAt is null)
                .Select(instrument => (instrument.Id, instrument.FirstSeenAt, Listing: (BybitListedInstrumentInternal)instrument.Listing))
                .Where(contract => contract.Listing.Category == BybitCategory.Linear && !_symbols.Contains(contract.Listing.Symbol))
                .Select(contract => (contract.Id, contract.FirstSeenAt, contract.Listing.Symbol))
        ];

        if (listed.Length == 0)
        {
            return;
        }

        Dictionary<string, Guid> ids = listed.ToDictionary(contract => contract.Symbol, contract => contract.Id);

        lock (_lock)
        {
            // One that Bybit did not take before is not heard from then on, not from now.
            foreach ((Guid id, DateTimeOffset firstSeenAt, _) in listed)
            {
                DateTimeOffset seen = MinuteRange.StartOfMinute(firstSeenAt);
                _unheard.TryAdd(id, seen > since ? seen : since);
            }
        }

        try
        {
            _subscriptions.Add(
                await bybit.SubscribeAsync(
                    ids.Keys,
                    liquidation => Count(ids, liquidation),
                    symbols => Lost(ids, symbols),
                    symbols => Restored(ids, symbols),
                    stoppingToken));
            _symbols.UnionWith(ids.Keys);
            Heard(ids.Values);
            logger.LogInformation("Subscribed to the liquidations of {Count} linear contracts of Bybit.", ids.Count);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                // A part of them may have been subscribed to, lost and restored before Bybit refused the rest: none is heard now.
                DateTimeOffset now = MinuteRange.StartOfMinute(time.GetUtcNow());
                _lost.ExceptWith(ids.Values);

                foreach (Guid id in ids.Values)
                {
                    _unheard.TryAdd(id, now);
                }
            }

            logger.LogError(exception, "Subscribing to the liquidations of {Count} linear contracts of Bybit failed; it is tried again later.", ids.Count);
        }
    }

    // The minute nobody listened from: read from storage when the listening starts, before anything of this run is saved, and kept
    // while it goes on. Null when storage cannot be read, which is logged: nothing is subscribed to until it can.
    private async Task<DateTimeOffset?> SinceAsync(CancellationToken stoppingToken)
    {
        if (_since is not null)
        {
            return _since;
        }

        try
        {
            DateTimeOffset? last = await scopes.SendAsync(new GetLastLiquidationTimeQuery(), stoppingToken);

            // Nothing stored: nobody listened before, and no minute before this one is to mark.
            _since = last ?? MinuteRange.StartOfMinute(time.GetUtcNow());
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading the latest stored minute of liquidations failed; nothing is subscribed to until it is read.");
        }

        return _since;
    }

    // Ends the subscriptions and forgets what they were to, so it is subscribed to anew when Bybit is on again. No contract is marked
    // from here on: when the listening starts again, every contract is marked from the latest minute storage holds by then.
    private async Task UnsubscribeAsync()
    {
        foreach (IAsyncDisposable subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions.Clear();
        _symbols.Clear();
        _since = null;

        lock (_lock)
        {
            _unheard.Clear();
            _lost.Clear();
        }
    }

    // Called on the threads of the stream: the liquidation only joins the sum of its minute.
    private void Count(Dictionary<string, Guid> ids, BybitLinearLiquidation liquidation)
    {
        if (!ids.TryGetValue(liquidation.Symbol, out Guid id))
        {
            return;
        }

        (Guid, DateTimeOffset) minute = (id, MinuteRange.StartOfMinute(liquidation.Time));

        lock (_lock)
        {
            LiquidationMinute sums = _heard.GetValueOrDefault(minute) ?? new LiquidationMinute(id, minute.Item2, LiquidationSum.None, LiquidationSum.None);
            _heard[minute] = sums.Add(liquidation.OfLong, liquidation.Size, liquidation.Price);
        }
    }

    // A lost connection is found a while after it broke, so the minute before is not heard in full either.
    private void Lost(Dictionary<string, Guid> ids, IReadOnlyCollection<string> symbols)
    {
        lock (_lock)
        {
            DateTimeOffset before = MinuteRange.StartOfMinute(time.GetUtcNow()) - Minute;

            foreach (string symbol in symbols)
            {
                _lost.Add(ids[symbol]);
                _unheard.TryAdd(ids[symbol], before);
            }
        }
    }

    private void Restored(Dictionary<string, Guid> ids, IReadOnlyCollection<string> symbols)
    {
        lock (_lock)
        {
            foreach (string symbol in symbols)
            {
                _lost.Remove(ids[symbol]);
            }
        }

        Heard([.. symbols.Select(symbol => ids[symbol])]);
    }

    // The contracts are heard from now on: what is left to mark of each ends with this minute.
    private void Heard(IReadOnlyCollection<Guid> ids)
    {
        lock (_lock)
        {
            DateTimeOffset now = MinuteRange.StartOfMinute(time.GetUtcNow());

            foreach (Guid id in ids)
            {
                if (!_lost.Contains(id) && _unheard.Remove(id, out DateTimeOffset since))
                {
                    _gaps.Add(new Gap(id, since, now, Open: false));
                }
            }
        }
    }

    // Every SaveSeconds: the sums heard since the last time, then the minutes that were not heard in full.
    private async Task SaveAsync(CancellationToken stoppingToken)
    {
        // Valid here: the app checks the settings when it starts.
        LiquidationStreamSettings stream = settings.CurrentValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(stream.SaveSeconds), time, stoppingToken);

            await SaveHeardAsync(stoppingToken);
            await MarkUnheardAsync(stoppingToken);

            stream = settings.CurrentOrLastValid(stream, logger);
        }
    }

    // The sums are saved in a scope of their own. A save that fails is not tried again: storage may have taken the sums, and adding
    // them once more would count them twice. Its minutes are marked not valid instead.
    private async Task SaveHeardAsync(CancellationToken stoppingToken)
    {
        LiquidationMinute[] minutes;

        lock (_lock)
        {
            minutes = [.. _heard.Values];
            _heard.Clear();
        }

        if (minutes.Length == 0)
        {
            return;
        }

        try
        {
            await scopes.SendAsync(new SaveLiquidationsCommand(minutes), stoppingToken);
            logger.LogDebug("Saved the liquidations of {Minutes} minutes of the contracts of Bybit.", minutes.Length);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Saving the liquidations of {Minutes} minutes failed; the minutes are marked not valid.", minutes.Length);

            lock (_lock)
            {
                _gaps.AddRange(minutes.Select(minute => new Gap(minute.InstrumentId, minute.Time, minute.Time, Open: false)));
            }
        }
    }

    // Marks what is left to mark, the contracts of the same minutes together. A contract still not heard is marked up to this minute
    // and from it again the next time. A marking that fails is logged and tried again at the next save.
    private async Task MarkUnheardAsync(CancellationToken stoppingToken)
    {
        Gap[] gaps;

        lock (_lock)
        {
            DateTimeOffset now = MinuteRange.StartOfMinute(time.GetUtcNow());
            gaps = [.. _gaps, .. _unheard.Select(unheard => new Gap(unheard.Key, unheard.Value, now, Open: true))];
            _gaps.Clear();
        }

        foreach (IGrouping<(DateTimeOffset From, DateTimeOffset Until), Gap> span in gaps.GroupBy(gap => (gap.From, gap.Until)))
        {
            try
            {
                await scopes.SendAsync(
                    new InvalidateLiquidationsCommand([.. span.Select(gap => gap.InstrumentId).Distinct()], span.Key.From, span.Key.Until),
                    stoppingToken);
                logger.LogInformation(
                    "Marked the liquidations of {Count} contracts of Bybit from {From:O} to {Until:O} as not valid.",
                    span.Count(),
                    span.Key.From,
                    span.Key.Until);

                lock (_lock)
                {
                    // Still not heard, and not lost anew since: the minutes up to this one are marked.
                    foreach (Gap gap in span.Where(gap => gap.Open && _unheard.GetValueOrDefault(gap.InstrumentId) == gap.From))
                    {
                        _unheard[gap.InstrumentId] = gap.Until;
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Marking the liquidations of {Count} contracts of Bybit from {From:O} to {Until:O} as not valid failed; it is tried again.",
                    span.Count(),
                    span.Key.From,
                    span.Key.Until);

                lock (_lock)
                {
                    // The ones still not heard are marked again from where they were; the others would be forgotten.
                    _gaps.AddRange(span.Where(gap => !gap.Open));
                }
            }
        }
    }

    // The minutes of a contract to mark not valid, both ends included. Open while the contract is still not heard: its end is the
    // minute of the save, and it goes on from there.
    private readonly record struct Gap(Guid InstrumentId, DateTimeOffset From, DateTimeOffset Until, bool Open);
}