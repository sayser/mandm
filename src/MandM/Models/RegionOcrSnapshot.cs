using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MandM.Models;

/// <summary>Live OCR text for one named capture region (OCR tab).</summary>
public sealed class RegionOcrSnapshot : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _bounds = string.Empty;
    private string _text = string.Empty;
    private bool _isValid;

    public string Id { get; init; } = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string BoundsDisplay
    {
        get => _bounds;
        set
        {
            if (_bounds == value) return;
            _bounds = value;
            OnPropertyChanged();
        }
    }

    public bool IsValid
    {
        get => _isValid;
        set
        {
            if (_isValid == value) return;
            _isValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public string StatusDisplay =>
        !IsValid ? "Region not set" :
        string.IsNullOrWhiteSpace(Text) ? "Waiting for OCR…" :
        "Live";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
