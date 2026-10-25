namespace MandM.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public CaptureRegion AbilitiesRegion { get; set; } = new();
    public CaptureRegion CombatTextRegion { get; set; } = new();
    public List<BuffDefinition> Buffs { get; set; } = [];
    public bool OverlayEnabled { get; set; }
    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 80;
    public double OverlayWidth { get; set; } = 280;
    public double OverlayHeight { get; set; } = 220;
    public int OcrIntervalMs { get; set; } = 750;
    public string UpdateRepo { get; set; } = "sayser/mandm";

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.Buffs.Add(BuffDefinition.CreateSpeedOfThePackSeed());
        return settings;
    }
}
