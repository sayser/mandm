using System.Windows;

namespace MandM;

public partial class App : Application
{
    public static AppState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        State = new AppState();
        State.Monitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            State.SaveSettings();
            State.Dispose();
        }
        catch
        {
            // ignore shutdown errors
        }

        base.OnExit(e);
    }
}
