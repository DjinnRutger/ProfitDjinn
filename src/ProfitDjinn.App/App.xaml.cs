using System.Windows;
using System.Windows.Threading;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.App;

public partial class App : Application
{
    /// <summary>
    /// Test runs only: PROFITDJINN_OFFSCREEN=1 together with PROFITDJINN_DATA_DIR opens every
    /// window outside the visible screen and without taking focus, so smoke
    /// tests (which drive the app through UI Automation) do not cover what the user is doing.
    /// </summary>
    public static bool Offscreen { get; } =
        Environment.GetEnvironmentVariable("PROFITDJINN_OFFSCREEN") == "1"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROFITDJINN_DATA_DIR"));

    /// <summary>Moves a window outside the visible screen and stops it taking focus (see <see cref="Offscreen"/>).</summary>
    public static void PlaceOffscreen(Window w)
    {
        w.ShowActivated = false;
        w.WindowState = WindowState.Normal;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = SystemParameters.VirtualScreenLeft - w.Width - 400;
        w.Top = SystemParameters.VirtualScreenTop;
        // UI Automation lets the process it drives come to the front, and WPF activates the window
        // when it moves keyboard focus. Refuse: a no-activate style, and whenever this app ends up
        // in front anyway, the window the user was in gets the foreground straight back.
        w.SourceInitialized += (_, _) =>
        {
            IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            SetWindowLongPtr(h, GwlExStyle, new IntPtr(GetWindowLongPtr(h, GwlExStyle).ToInt64() | WsExNoActivate));
            System.Windows.Interop.HwndSource.FromHwnd(h)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WmActivate && (wParam.ToInt64() & 0xFFFF) != 0) GiveBackForeground();
                return IntPtr.Zero;
            });
        };
        if (_userWindowWatch is null)
        {
            _userWindowWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _userWindowWatch.Tick += (_, _) => GiveBackForeground();
            _userWindowWatch.Start();
        }
    }

    private static DispatcherTimer? _userWindowWatch;
    private static IntPtr _userWindow;

    /// <summary>Remembers the user's foreground window, or returns the foreground to it if this app has it.</summary>
    private static void GiveBackForeground()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid != (uint)Environment.ProcessId) _userWindow = fg;
        else if (_userWindow != IntPtr.Zero) SetForegroundWindow(_userWindow);
    }

    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const int WmActivate = 0x0006;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        Store store;
        try
        {
            store = new Store(AppPaths.Default());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "ProfitDjinn could not open its database.\n\n" + ex.Message +
                "\n\nIf another copy of ProfitDjinn is open, close it and try again.",
                "ProfitDjinn", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ThemeManager.Apply(store.Settings.Theme(), store.Settings.Get(SettingKeys.PrimaryColor));
        // --page <name> opens a screen at start (smoke tests and screenshots use it).
        int at = Array.IndexOf(e.Args, "--page");
        string? startPage = at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : null;
        var window = new MainWindow(store, startPage);
        MainWindow = window;
        if (Offscreen)
        {
            window.Width = 1440;
            window.Height = 900;
            PlaceOffscreen(window);
        }
        window.Show();
        if (Offscreen && Environment.GetEnvironmentVariable("PROFITDJINN_SNAPSHOT") is { Length: > 0 } snapshot)
        {
            int delay = int.TryParse(Environment.GetEnvironmentVariable("PROFITDJINN_SNAPSHOT_DELAY"), out int d) ? d : 3;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) => { timer.Stop(); SaveSnapshot(window, snapshot); };
            timer.Start();
        }
    }

    /// <summary>
    /// Test screenshots (Capture.ps1): Windows does not draw a window that is off the screen, so
    /// the app draws its own window contents to a PNG. Written to a temp name, then renamed, so
    /// the script never reads a half-written file.
    /// </summary>
    private static void SaveSnapshot(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(size));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(size));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string temp = path + ".tmp";
        using (var file = File.Create(temp)) png.Save(file);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Last line of defence. A UserFacingException carries a message written for the user;
    /// anything else is a bug, shown with its details so it can be reported.
    /// </summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (e.Exception is UserFacingException uf)
        {
            (MainWindow as MainWindow)?.ShowError(uf.Message);
            return;
        }
        MessageBox.Show(
            "Something went wrong that ProfitDjinn did not expect. Your data has not been changed by the step that failed.\n\n" +
            $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
            $"ProfitDjinn {AppInfo.Version}", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
