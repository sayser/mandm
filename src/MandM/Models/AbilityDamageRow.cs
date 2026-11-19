using System.Windows.Media;

namespace MandM.Models;

public sealed class AbilityDamageRow
{
    public required string Name { get; init; }
    public required int Damage { get; init; }
    public required double Share { get; init; }
    public required Brush Color { get; init; }

    public string DamageDisplay => Damage.ToString("N0");
    public string ShareDisplay => Share.ToString("0.0") + "%";

    /// <summary>Same slice order as EQDM's ability doughnut.</summary>
    public static Brush ColorAt(int index) => Palette[index % Palette.Length];

    private static readonly Brush[] Palette = CreatePalette();

    private static Brush[] CreatePalette()
    {
        System.Windows.Media.Color[] colors =
        [
            System.Windows.Media.Color.FromRgb(124, 92, 252),
            System.Windows.Media.Color.FromRgb(41, 211, 194),
            System.Windows.Media.Color.FromRgb(64, 156, 255),
            System.Windows.Media.Color.FromRgb(255, 183, 77),
            System.Windows.Media.Color.FromRgb(255, 99, 132),
            System.Windows.Media.Color.FromRgb(155, 112, 255),
            System.Windows.Media.Color.FromRgb(63, 205, 118),
            System.Windows.Media.Color.FromRgb(255, 126, 80),
            System.Windows.Media.Color.FromRgb(88, 204, 255)
        ];
        return colors.Select(color =>
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return (Brush)brush;
        }).ToArray();
    }
}
