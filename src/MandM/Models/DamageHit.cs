namespace MandM.Models;

public sealed class DamageHit
{
    public required DateTime SeenLocal { get; init; }
    public required bool Outgoing { get; init; }
    public required int Amount { get; init; }
    public required string Who { get; init; }
    public required string Detail { get; init; }

    public string TimeDisplay => SeenLocal.ToString("HH:mm:ss");
    public string DirectionDisplay => Outgoing ? "Out" : "In";
    public string AmountDisplay => Amount.ToString("N0");
}
