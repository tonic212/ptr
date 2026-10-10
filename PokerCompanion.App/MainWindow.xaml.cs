using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PokerCompanion.App.Services;
using PokerCompanion.App.ViewModels;

namespace PokerCompanion.App;

public partial class MainWindow : Window
{
    private readonly ScreenCaptureService _capture = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CaptureTarget? _target;
    private OverlayWindow? _overlay;
    public MainWindow()
    {
        InitializeComponent();
        try { DataContext = new MainViewModel(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Chart loading failed"); Close(); return; }
        _timer.Tick += CaptureFrame;
        Loaded += (_, _) => LoadWindows();
        Closed += (_, _) => { _timer.Stop(); _overlay?.Close(); Preview.Source = null; };
    }
    private void LoadWindows() => CaptureTargets.ItemsSource = _capture.ListWindows(new WindowInteropHelper(this).Handle);
    private void RefreshWindows(object sender, RoutedEventArgs e) => LoadWindows();
    private void ToggleCapture(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled) { StopCapture("Capture stopped."); return; }
        if (CaptureTargets.SelectedItem is not CaptureTarget target) { CaptureStatus.Text = "Select a window first."; return; }
        _target = target; CaptureTargets.IsEnabled = false; CaptureButton.Content = "Stop preview";
        _timer.Start(); CaptureFrame(this, EventArgs.Empty);
    }
    private void CaptureFrame(object? sender, EventArgs e)
    {
        try
        {
            if (_target is null) return;
            Preview.Source = _capture.Capture(_target);
            CaptureStatus.Text = $"Preview active: {_target.Title}. No automatic recognition.";
        }
        catch (Exception ex) { StopCapture(ex.Message); }
    }
    private void StopCapture(string message)
    {
        _timer.Stop(); _target = null; CaptureTargets.IsEnabled = true;
        CaptureButton.Content = "Start preview"; CaptureStatus.Text = message; Preview.Source = null;
    }
    private void ShowOverlay(object sender, RoutedEventArgs e)
    {
        if (_overlay is not null) { _overlay.Activate(); return; }
        _overlay = new OverlayWindow { DataContext = DataContext, Owner = this };
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
    }
}
