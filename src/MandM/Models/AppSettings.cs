namespace MandM.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;

    /// <summary>Legacy single regions — migrated into <see cref="CaptureRegions"/> on load.</summary>
    public CaptureRegion? AbilitiesRegion { get; set; }
    public CaptureRegion? CombatTextRegion { get; set; }

    public List<NamedCaptureRegion> CaptureRegions { get; set; } = [];

    /// <summary>Named capture region whose new lines are parsed as personal damage. Empty until the user picks one.</summary>
    public string DamageRegionId { get; set; } = string.Empty;

    public List<BuffDefinition> Buffs { get; set; } = [];
    public List<UserTimerDefinition> UserTimers { get; set; } = [];

    public bool OverlayEnabled { get; set; }
    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 80;
    public double OverlayWidth { get; set; } = 280;
    public double OverlayHeight { get; set; } = 220;

    public bool TimerOverlayEnabled { get; set; }
    public double TimerOverlayLeft { get; set; } = 40;
    public double TimerOverlayTop { get; set; } = 360;
    public double TimerOverlayWidth { get; set; } = 280;
    public double TimerOverlayHeight { get; set; } = 220;

    public int OcrIntervalMs { get; set; } = 750;
    public string UpdateRepo { get; set; } = "sayser/mandm";
    public MouseHighlightSettings MouseHighlight { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        var abilities = new NamedCaptureRegion { Name = "Abilities" };
        var combat = new NamedCaptureRegion { Name = "Combat Text" };
        settings.CaptureRegions.Add(abilities);
        settings.CaptureRegions.Add(combat);

        var seed = BuffDefinition.CreateSpeedOfThePackSeed();
        seed.CastRegionId = combat.Id;
        seed.LandRegionId = combat.Id;
        seed.FadeRegionId = combat.Id;
        seed.WarnRegionId = combat.Id;
        settings.Buffs.Add(seed);
        return settings;
    }

    public NamedCaptureRegion? FindRegion(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : CaptureRegions.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.Ordinal));

    public NamedCaptureRegion? FindRegionByName(string name) =>
        CaptureRegions.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Prefer Combat Text, else first non-Abilities region, else first region.</summary>
    public string? DefaultBuffRegionId()
    {
        var combat = FindRegionByName("Combat Text");
        if (combat is not null) return combat.Id;

        var other = CaptureRegions.FirstOrDefault(r =>
            !string.Equals(r.Name, "Abilities", StringComparison.OrdinalIgnoreCase));
        return other?.Id ?? CaptureRegions.FirstOrDefault()?.Id;
    }
}
