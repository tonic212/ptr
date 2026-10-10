using System;
using System.IO;
using System.Threading;
using System.Windows;
using PokerCompanion.App.Services;

namespace PokerCompanion.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            try
            {
                var charts = new ChartRepository().Load();
                string seeds = Path.Combine(BundledDataStore.Root, "Data", "recognition-seeds.json");
                if (!File.Exists(seeds)) throw new InvalidDataException("Bundled recognition seeds missing.");
                File.WriteAllText(e.Args[1], $"PASS: standalone assets loaded; {charts.Count} charts.");
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(e.Args[1], "FAILED: " + ex); Shutdown(1); }
            return;
        }
        _instance = new Mutex(true, "Local\\PokerCompanion.SingleInstance", out bool created);
        if (!created) { _instance.Dispose(); _instance = null; MessageBox.Show("PokerCompanion is already running."); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance is not null) { _instance.ReleaseMutex(); _instance.Dispose(); }
        base.OnExit(e);
    }
}
