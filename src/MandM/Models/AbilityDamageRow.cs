namespace MandM.Models;

public sealed class AbilityDamageRow
{
    public required string Name { get; init; }
    public required int Damage { get; init; }
    public required double Share { get; init; }

    public string DamageDisplay => Damage.ToString("N0");
    public string ShareDisplay => Share.ToString("0.0") + "%";
}
