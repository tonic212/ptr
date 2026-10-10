using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PokerCompanion.App.Services;
using PokerCompanion.App.Services.Recognition;
using PokerCompanion.App.ViewModels;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App;

public partial class MainWindow : Window
{
    private readonly ScreenCaptureService _capture = new();
    private readonly RecognitionProfileStore _profileStore = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private HandRecognitionSession _recognition = null!;
    private CaptureTarget? _target;
    private OverlayWindow? _overlay;
    private BitmapSource? _lastFrame;
    private int _selectionSlot;
    private Point? _selectionStart;
    private bool _frozen;
    private bool _lastAutomaticMode;
    private MainViewModel Model => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            DataContext = new MainViewModel();
            _recognition = new(_profileStore.Load());
            Model.Coverage = _recognition.Coverage;
            if (_profileStore.LoadWarning is not null) CaptureStatus.Text = _profileStore.LoadWarning;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Startup failed"); Close(); return; }
        _timer.Tick += CaptureFrame;
        Loaded += (_, _) => LoadWindows();
        Closed += (_, _) => { _timer.Stop(); _overlay?.Close(); _lastFrame = null; Preview.Source = null; };
    }
    private void LoadWindows()
    {
        var windows = _capture.ListWindows(new WindowInteropHelper(this).Handle);
        CaptureTargets.ItemsSource = windows;
        CaptureTargets.SelectedItem = windows.FirstOrDefault(w => w.Title.StartsWith("PPPoker", StringComparison.OrdinalIgnoreCase));
    }
    private void RefreshWindows(object sender, RoutedEventArgs e) => LoadWindows();
    private void ToggleCapture(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled) { StopCapture("Capture stopped."); return; }
        if (CaptureTargets.SelectedItem is not CaptureTarget) LoadWindows();
        if (CaptureTargets.SelectedItem is not CaptureTarget target) { CaptureStatus.Text = "Open PPPoker first, or select its window."; return; }
        if (target.Title.StartsWith("PPPoker", StringComparison.OrdinalIgnoreCase)) _capture.Focus(target);
        _target = target; _frozen = false; FreezeButton.Content = "Freeze frame";
        CaptureTargets.IsEnabled = false; CaptureButton.Content = "Stop preview";
        _lastAutomaticMode = Model.AutomaticHeroCards; _recognition.Reset(); _timer.Start(); PreviewTabs.SelectedItem = WindowPreviewTab;
        CaptureFrame(this, EventArgs.Empty);
    }
    private void CaptureFrame(object? sender, EventArgs e)
    {
        if (_frozen || _selectionSlot != 0) return;
        try
        {
            if (_target is null) return;
            _lastFrame = _capture.Capture(_target); Preview.Source = _lastFrame;
            if (_lastAutomaticMode != Model.AutomaticHeroCards) { _recognition.Reset(); _lastAutomaticMode = Model.AutomaticHeroCards; }
            Model.ApplyObservation(_recognition.Observe(FrameAdapter.ToPixels(_lastFrame), Model.AutomaticHeroCards));
            CaptureStatus.Text = $"Preview active: {_target.Title}. Frames remain in memory.";
        }
        catch (Exception ex) { StopCapture(ex.Message); }
    }
    private void StopCapture(string message)
    {
        _timer.Stop(); _target = null; _frozen = false; CancelCalibration();
        CaptureTargets.IsEnabled = true; CaptureButton.Content = "Start PPPoker preview"; FreezeButton.Content = "Freeze frame";
        CaptureStatus.Text = message; _lastFrame = null; Preview.Source = null;
        ClearRecognition(message);
    }
    private void ToggleFreeze(object sender, RoutedEventArgs e)
    {
        if (!_timer.IsEnabled || _lastFrame is null) { CaptureStatus.Text = "Start capture first, or load a screenshot."; return; }
        CancelCalibration(); _frozen = !_frozen;
        FreezeButton.Content = _frozen ? "Resume preview" : "Freeze frame";
        ClearRecognition(_frozen ? "Capture frozen for calibration or teaching." : "Checking current cards.");
        if (!_frozen) CaptureFrame(this, EventArgs.Empty);
    }
    private void LoadScreenshot(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp", Title = "Load a table screenshot" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            StopCapture("Loading screenshot.");
            using var stream = File.OpenRead(picker.FileName);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (image.PixelWidth > 8192 || image.PixelHeight > 8192) throw new InvalidOperationException("Screenshot is too large.");
            _lastFrame = image; Preview.Source = image; _frozen = true;
            PreviewTabs.SelectedItem = WindowPreviewTab;
            CaptureStatus.Text = "Screenshot loaded. Automatic mode looks for the bottom hero cards.";
            InspectStaticFrame();
        }
        catch (Exception ex) { StopCapture(ex.Message); }
    }
    private void CalibrateFirst(object sender, RoutedEventArgs e) => BeginCalibration(1);
    private void CalibrateSecond(object sender, RoutedEventArgs e) => BeginCalibration(2);
    private void BeginCalibration(int slot)
    {
        if (_lastFrame is null) { CaptureStatus.Text = "Start capture or load a screenshot first."; return; }
        Model.AutomaticHeroCards = false;
        CancelCalibration(); _selectionSlot = slot; PreviewTabs.SelectedItem = WindowPreviewTab;
        ClearRecognition($"Drag around card {slot}'s small rank and suit in the preview.");
    }
    private void CancelSelection(object sender, RoutedEventArgs e) => CancelCalibration();
    private void CancelCalibration()
    {
        _selectionSlot = 0; _selectionStart = null; SelectionBox.Visibility = Visibility.Collapsed;
        PreviewHost.ReleaseMouseCapture();
    }
    private void SelectionDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectionSlot == 0 || _lastFrame is null) return;
        _selectionStart = e.GetPosition(PreviewHost); PreviewHost.CaptureMouse();
        SelectionBox.Visibility = Visibility.Visible; DrawSelection(_selectionStart.Value); e.Handled = true;
    }
    private void SelectionMove(object sender, MouseEventArgs e)
    {
        if (_selectionStart is not null) DrawSelection(e.GetPosition(PreviewHost));
    }
    private void DrawSelection(Point end)
    {
        if (_selectionStart is not Point start) return;
        System.Windows.Controls.Canvas.SetLeft(SelectionBox, Math.Min(start.X, end.X));
        System.Windows.Controls.Canvas.SetTop(SelectionBox, Math.Min(start.Y, end.Y));
        SelectionBox.Width = Math.Abs(end.X - start.X); SelectionBox.Height = Math.Abs(end.Y - start.Y);
    }
    private void SelectionUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionStart is not Point start || _lastFrame is null) return;
        int slot = _selectionSlot; Point end = e.GetPosition(PreviewHost); CancelCalibration();
        try
        {
            CardRegion? region = PreviewCoordinateMapper.Map(start.X, start.Y, end.X, end.Y,
                PreviewHost.ActualWidth, PreviewHost.ActualHeight, _lastFrame.PixelWidth, _lastFrame.PixelHeight);
            if (region is null) throw new InvalidOperationException("Select at least a 5×12 pixel corner, inside the image.");
            // Reject a bad selection before saving it.
            _ = new CardGlyphExtractor().Extract(FrameAdapter.ToPixels(_lastFrame), region);
            _recognition.SetRegion(slot, region, _lastFrame.PixelWidth, _lastFrame.PixelHeight);
            _profileStore.Save(_recognition.Profile);
            CaptureStatus.Text = $"Card {slot} corner saved.";
            if (_frozen) InspectStaticFrame();
        }
        catch (Exception ex) { CaptureStatus.Text = ex.Message; }
        e.Handled = true;
    }
    private void TeachVisibleCards(object sender, RoutedEventArgs e)
    {
        if (!_frozen || _lastFrame is null) { CaptureStatus.Text = "Freeze the capture before teaching, or load a screenshot."; return; }
        try
        {
            _recognition.Teach(FrameAdapter.ToPixels(_lastFrame), TeachCards.Text, Model.AutomaticHeroCards);
            _profileStore.Save(_recognition.Profile); Model.Coverage = _recognition.Coverage;
            CaptureStatus.Text = "Visible rank/suit samples saved. Resume preview for live recognition.";
            InspectStaticFrame();
        }
        catch (Exception ex) { CaptureStatus.Text = ex.Message; }
    }
    private void InspectStaticFrame()
    {
        if (_lastFrame is null) return;
        // A static screenshot is one observation, not three temporal confirmations.
        _recognition.Reset();
        HandObservation result = _recognition.Inspect(FrameAdapter.ToPixels(_lastFrame), Model.AutomaticHeroCards);
        Model.ApplyStaticObservation(result with { Status = result.Status + " Resume live capture for temporal confirmation." });
    }
    private void ClearRecognition(string status)
    {
        _recognition.Reset(); Model.ApplyObservation(new(false, "", "", status));
    }
    private void ShowOverlay(object sender, RoutedEventArgs e)
    {
        if (_overlay is not null) { _overlay.Activate(); return; }
        _overlay = new OverlayWindow { DataContext = DataContext, Owner = this };
        _overlay.Closed += (_, _) => _overlay = null; _overlay.Show();
    }
}
