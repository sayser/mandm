using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MandM.Models;

namespace MandM;

public partial class App : Application
{
    public static AppState State { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        State = new AppState();

        if (e.Args.Any(a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase)))
        {
            CaptureUiScreenshots();
            Shutdown(0);
            return;
        }

        State.Monitor.Start();
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static void CaptureUiScreenshots()
    {
        var outDir = ResolveScreenshotDir();
        Directory.CreateDirectory(outDir);

        // Seed a sample active buff so the tracker/overlay aren't empty in shots.
        var seed = State.Settings.Buffs.FirstOrDefault() ?? BuffDefinition.CreateSpeedOfThePackSeed();
        State.Tracker.LandBuff(seed);
        State.Tracker.SetLoadedAbilities(
        [
            "Sublime Spirit Mending",
            "Speed of the Pack",
            "Hex of Lethargy",
            "Consuming Pact"
        ]);

        // Seed mouse-highlight so Options shows a realistic enabled state in shots.
        State.Settings.MouseHighlight = new MouseHighlightSettings
        {
            Enabled = true,
            ColorHex = "#2FD8C7",
            Diameter = 52,
            Thickness = 3,
            Opacity = 0.85,
            Blink = false,
            SecondRing = true,
            SecondDiameter = 88
        };

        var main = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 40,
            Top = 40
        };
        main.Show();
        main.UpdateLayout();
        DoEvents();
        SaveWindowPng(main, Path.Combine(outDir, "main-buff-tracker.png"));

        // Damage Tracker tab shell
        main.TabDamage.IsChecked = true;
        DoEvents();
        SaveWindowPng(main, Path.Combine(outDir, "main-damage-tracker.png"));
        main.TabBuffs.IsChecked = true;
        DoEvents();

        var overlay = new OverlayWindow(State.Tracker)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 980,
            Top = 80,
            Width = 360,
            Height = 220
        };
        overlay.Show();
        overlay.UpdateLayout();
        DoEvents();
        SaveWindowPng(overlay, Path.Combine(outDir, "buff-overlay.png"));

        var options = new OptionsWindow
        {
            Owner = main,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 200,
            Top = 40,
            Height = 720
        };
        options.Show();
        options.UpdateLayout();
        DoEvents();
        SaveWindowPng(options, Path.Combine(outDir, "options.png"));
        // Dedicated crop-friendly alias for the cursor settings section
        SaveWindowPng(options, Path.Combine(outDir, "options-mouse-highlight.png"));

        // Region picker with EQDM-style pointer circle staged for the shot.
        var picker = new RegionPickerWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowState = WindowState.Normal,
            Left = 40,
            Top = 40,
            Width = 960,
            Height = 540,
            Topmost = false,
            ShowActivated = false
        };
        picker.Show();
        picker.UpdateLayout();
        DoEvents();
        picker.ShowPreviewDemo(new Point(520, 300), new Rect(280, 180, 360, 160));
        DoEvents();
        SaveWindowPng(picker, Path.Combine(outDir, "region-picker-mouse-circle.png"));

        picker.Close();
        options.Close();
        overlay.Close();
        main.Close();
    }

    private static string ResolveScreenshotDir()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cursor", "AgentStores", "cursor_agent_stores",
                "bc-f3c2393f-772b-4df4-b58a-fb44e43389e6", "files", "media", "ui-after-eqdm-restyle"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "media", "ui-after-eqdm-restyle"))
        };

        foreach (var c in candidates)
        {
            try
            {
                var parent = Directory.GetParent(c)?.FullName;
                if (parent is not null && Directory.Exists(parent))
                    return c;
            }
            catch
            {
                // try next
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "MandM", "screenshots");
        return fallback;
    }

    private static void SaveWindowPng(Window window, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var dpi = 96.0;
        var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(window);
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            State?.SaveSettings();
            State?.Dispose();
        }
        catch
        {
            // ignore shutdown errors
        }

        base.OnExit(e);
    }
}
