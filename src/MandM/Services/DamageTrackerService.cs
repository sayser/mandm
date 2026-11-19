using System.Collections.ObjectModel;
using System.Windows;
using MandM.Models;

namespace MandM.Services;

/// <summary>
/// Totals and a live list of personal outgoing and incoming damage.
/// Does not attribute other players.
/// </summary>
public sealed class DamageTrackerService
{
    private const int MaxHits = 120;
    private readonly object _gate = new();
    private readonly DamageLineBuffer _buffer = new();
    private string _regionId;

    public ObservableCollection<DamageHit> Hits { get; } = [];
    public int OutgoingTotal { get; private set; }
    public int IncomingTotal { get; private set; }
    public int OutgoingHits { get; private set; }
    public int IncomingHits { get; private set; }
    public event Action? Changed;

    public DamageTrackerService(AppSettings settings)
    {
        _regionId = settings.DamageRegionId ?? string.Empty;
    }

    public void UpdateSettings(AppSettings settings)
    {
        var next = settings.DamageRegionId ?? string.Empty;
        var regionChanged = !string.Equals(_regionId, next, StringComparison.Ordinal);
        _regionId = next;
        InvalidateMatchBaseline();
        if (regionChanged)
            ClearTotals();
    }

    public void InvalidateMatchBaseline()
    {
        lock (_gate)
            _buffer.Invalidate();
    }

    public void MarkBaselineReady()
    {
        lock (_gate)
            _buffer.MarkReady();
    }

    public void PrimeRegionBaseline(string regionId, IEnumerable<string> lines)
    {
        if (!IsSelected(regionId))
            return;
        lock (_gate)
            _buffer.Prime(lines);
    }

    public void ProcessRegionLines(string regionId, IEnumerable<string> lines)
    {
        if (!IsSelected(regionId))
            return;

        List<string> fresh;
        lock (_gate)
            fresh = _buffer.Observe(lines);
        if (fresh.Count == 0)
            return;

        var hits = new List<DamageHit>(fresh.Count);
        foreach (var line in fresh)
        {
            if (!DamageLineParser.TryParse(line, out var parsed))
                continue;
            hits.Add(new DamageHit
            {
                SeenLocal = DateTime.Now,
                Outgoing = parsed.Outgoing,
                Amount = parsed.Amount,
                Who = parsed.Who,
                Detail = parsed.Detail
            });
        }

        if (hits.Count > 0)
            AddHits(hits);
    }

    public void ClearTotals()
    {
        void Apply()
        {
            OutgoingTotal = 0;
            IncomingTotal = 0;
            OutgoingHits = 0;
            IncomingHits = 0;
            Hits.Clear();
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else if (Application.Current is not null)
            Application.Current.Dispatcher.Invoke(Apply);
        else
            Apply();
    }

    private void AddHits(List<DamageHit> hits)
    {
        void Apply()
        {
            foreach (var hit in hits)
            {
                if (hit.Outgoing)
                {
                    OutgoingTotal += hit.Amount;
                    OutgoingHits++;
                }
                else
                {
                    IncomingTotal += hit.Amount;
                    IncomingHits++;
                }

                Hits.Insert(0, hit);
            }

            while (Hits.Count > MaxHits)
                Hits.RemoveAt(Hits.Count - 1);
            Changed?.Invoke();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
            Apply();
        else
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private bool IsSelected(string regionId) =>
        !string.IsNullOrWhiteSpace(_regionId)
        && string.Equals(_regionId, regionId, StringComparison.Ordinal);
}
