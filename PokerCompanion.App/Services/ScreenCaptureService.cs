using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace PokerCompanion.App.Services;

public sealed record CaptureTarget(IntPtr Handle, string Title);

// Captures the visible desktop rectangle of the selected window. Overlapping
// windows appear in the image; this does not capture minimized/hidden contents.
public sealed class ScreenCaptureService
{
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    public bool Focus(CaptureTarget target)
    {
        IntPtr root = GetAncestor(target.Handle, 2);
        if (IsIconic(root)) ShowWindow(root, 9);
        return SetForegroundWindow(root);
    }

    public IReadOnlyList<CaptureTarget> ListWindows(IntPtr ownWindow)
    {
        var windows = new List<CaptureTarget>();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == ownWindow || !IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) windows.Add(new(hwnd, title.ToString()));
            if (IsPokerTitle(title.ToString()))
                EnumChildWindows(hwnd, (child, _) =>
                {
                    if (!IsWindowVisible(child)) return true;
                    var caption = new StringBuilder(512); GetWindowText(child, caption, caption.Capacity);
                    if (IsPokerTitle(caption.ToString()) && GetWindowRect(child, out Rect childRect)
                        && childRect.Right - childRect.Left >= 180 && childRect.Bottom - childRect.Top >= 300)
                        windows.Add(new(child, caption.ToString()));
                    return true;
                }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        windows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title));
        return windows;
    }
    public static bool IsPokerTitle(string title) => title.TrimStart().StartsWith("PPPoker", StringComparison.OrdinalIgnoreCase);

    public CaptureTarget? FindPokerWindow(IReadOnlyList<CaptureTarget> windows)
    {
        CaptureTarget? best = null; double bestScore = double.MinValue;
        foreach (CaptureTarget window in windows)
        {
            if (!IsPokerTitle(window.Title) || !GetWindowRect(window.Handle, out Rect rect)) continue;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) continue;
            double score = window.Title.Contains("-v", StringComparison.OrdinalIgnoreCase) ? 10 : 0;
            if (width / (double)height is >= .30 and <= .85) score += 20;
            if (IsIconic(window.Handle)) score -= 5;
            if (score > bestScore) { best = window; bestScore = score; }
        }
        return best;
    }
    public BitmapSource Capture(CaptureTarget target)
    {
        if (!IsWindowVisible(target.Handle) || IsIconic(target.Handle) || !GetWindowRect(target.Handle, out Rect rect))
            throw new InvalidOperationException("Keep the selected window visible and restored.");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidOperationException("Window dimensions are unsupported.");
        using var bitmap = new Bitmap(width, height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        return image;
    }
}
