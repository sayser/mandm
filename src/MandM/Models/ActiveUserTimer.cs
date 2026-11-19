using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MandM.Models;

public sealed class ActiveUserTimer : INotifyPropertyChanged
{
    private TimeSpan _remaining;
    private bool _running;
    private bool _expireAlerted;
    private DateTime? _startedAtUtc;
    private TimeSpan _remainingAtStart;

    public required UserTimerDefinition Definition { get; init; }

    public string Name => Definition.Name;

    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (_running == value) return;
            _running = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public TimeSpan Remaining
    {
        get => _remaining;
        private set
        {
            if (_remaining == value) return;
            _remaining = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingDisplay));
            OnPropertyChanged(nameof(Progress));
        }
    }

    public string RemainingDisplay
    {
        get
        {
            var t = Remaining < TimeSpan.Zero ? TimeSpan.Zero : Remaining;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";
        }
    }

    public string StatusDisplay => IsRunning ? "Running" : (Remaining <= TimeSpan.Zero ? "Done" : "Stopped");

    public double Progress
    {
        get
        {
            if (Definition.DurationSeconds <= 0) return 0;
            return Math.Clamp(Remaining.TotalSeconds / Definition.DurationSeconds, 0, 1);
        }
    }

    /// <summary>Raised once when a running timer reaches zero (UI thread of the tick).</summary>
    public event Action<ActiveUserTimer>? Expired;

    public void Reset()
    {
        IsRunning = false;
        _startedAtUtc = null;
        _expireAlerted = false;
        Remaining = TimeSpan.FromSeconds(Math.Max(1, Definition.DurationSeconds));
    }

    public void Start()
    {
        if (Remaining <= TimeSpan.Zero)
            Remaining = TimeSpan.FromSeconds(Math.Max(1, Definition.DurationSeconds));
        _remainingAtStart = Remaining;
        _startedAtUtc = DateTime.UtcNow;
        _expireAlerted = false;
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        Tick(); // snap remaining
        IsRunning = false;
        _startedAtUtc = null;
    }

    public void Tick()
    {
        if (!IsRunning || _startedAtUtc is null) return;
        var elapsed = DateTime.UtcNow - _startedAtUtc.Value;
        var left = _remainingAtStart - elapsed;
        if (left <= TimeSpan.Zero)
        {
            Remaining = TimeSpan.Zero;
            IsRunning = false;
            _startedAtUtc = null;
            if (!_expireAlerted)
            {
                _expireAlerted = true;
                Expired?.Invoke(this);
            }
        }
        else
        {
            Remaining = left;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
