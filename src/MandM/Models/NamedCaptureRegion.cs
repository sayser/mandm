namespace MandM.Models;

/// <summary>User-named screen rectangle OCR'd each poll.</summary>
public sealed class NamedCaptureRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Region";
    public CaptureRegion Region { get; set; } = new();

    public bool IsValid => Region.IsValid;

    public string BoundsDisplay => Region.ToString();

    public string ListDisplay =>
        string.IsNullOrWhiteSpace(Name)
            ? BoundsDisplay
            : $"{Name}  —  {BoundsDisplay}";

    public NamedCaptureRegion Clone() => new()
    {
        Id = Id,
        Name = Name,
        Region = Region.Clone()
    };
}
