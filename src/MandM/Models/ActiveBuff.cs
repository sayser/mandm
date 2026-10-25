using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MandM.Models;

public sealed class ActiveBuff : INotifyPropertyChanged
{
    private TimeSpan _remaining;
    private bool _warned;

    public required BuffDefinition Definition { get; init; }
    public DateTime LandedAtUtc { get; init; }

    public TimeSpan Remaining
    {
        get => _remaining;
        set
        {
            if (_remaining == value) return;
            _remaining = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingDisplay));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(Progress));
        }
    }

    public bool Warned
    {
        get => _warned;
        set
        {
            if (_warned == value) return;
            _warned = value;
            OnPropertyChanged();
        }
    }

    public string Name => Definition.Name;
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

    public bool IsWarning => Remaining.TotalSeconds <= Definition.WarnSeconds && Remaining > TimeSpan.Zero;

    public double Progress
    {
        get
        {
            if (Definition.DurationSeconds <= 0) return 0;
            var ratio = Remaining.TotalSeconds / Definition.DurationSeconds;
            return Math.Clamp(ratio, 0, 1);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
