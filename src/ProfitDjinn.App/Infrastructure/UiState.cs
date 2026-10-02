using System.Text.Json;
using System.Windows;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// Window size and position and the sidebar state, kept in %LOCALAPPDATA%\ProfitDjinn\ui.json
/// (1.x kept the sidebar state in the browser's localStorage). Not in the database: it is
/// about this PC's screen, not the business.
/// </summary>
public sealed class UiState
{
    public bool SidebarCollapsed { get; set; }
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 900;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Maximized { get; set; }

    public static UiState Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<UiState>(File.ReadAllText(path)) ?? new UiState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged ui.json only costs the window position; start with the defaults.
        }
        return new UiState();
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void ApplyTo(Window w)
    {
        var area = SystemParameters.WorkArea;
        w.Width = Math.Clamp(Width, w.MinWidth, Math.Max(w.MinWidth, area.Width));
        w.Height = Math.Clamp(Height, w.MinHeight, Math.Max(w.MinHeight, area.Height));
        if (Left is { } l && Top is { } t && l >= area.Left - 50 && t >= area.Top - 50 && l < area.Right - 100 && t < area.Bottom - 100)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = l;
            w.Top = t;
        }
        if (Maximized) w.WindowState = WindowState.Maximized;
    }

    public void CaptureFrom(Window w, bool sidebarCollapsed)
    {
        SidebarCollapsed = sidebarCollapsed;
        Maximized = w.WindowState == WindowState.Maximized;
        var bounds = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            Width = bounds.Width;
            Height = bounds.Height;
            Left = bounds.Left;
            Top = bounds.Top;
        }
    }
}
