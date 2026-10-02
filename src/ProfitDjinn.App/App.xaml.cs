using System.Windows;
using System.Windows.Threading;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.App;

public partial class App : Application
{
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
        window.Show();
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
