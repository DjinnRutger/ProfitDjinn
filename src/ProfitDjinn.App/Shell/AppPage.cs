using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.Core;

namespace ProfitDjinn.App.Shell;

/// <summary>One breadcrumb step. Open is null for the current page (the last crumb).</summary>
public sealed record Crumb(string Text, Action? Open = null);

/// <summary>
/// A screen shown in the main window's content area. Pages are rebuilt from the database
/// each time they are opened, the way 1.x re-rendered a page on every request, so going
/// Back always shows current data.
/// </summary>
public abstract class AppPage : UserControl
{
    protected AppPage(MainWindow shell)
    {
        Shell = shell;
        Focusable = false;
    }

    protected MainWindow Shell { get; }

    protected Store Store => Shell.Store;

    /// <summary>The sidebar item to highlight: dashboard, customers, invoices, workorders, revenue, items, settings, backup.</summary>
    public abstract string NavKey { get; }

    /// <summary>Breadcrumb after "Home". The last one is the current page.</summary>
    public abstract IReadOnlyList<Crumb> Crumbs { get; }

    /// <summary>Called once the page is in the window. Put focus on the first field here.</summary>
    public virtual void OnShown() { }

    /// <summary>Asks before leaving a page with unsaved edits. Return false to stay.</summary>
    public virtual Task<bool> CanLeaveAsync() => Task.FromResult(true);

    /// <summary>
    /// Runs an action that may be refused. A refusal (UserFacingException) is shown as a
    /// red notice and the page stays as it is; field errors go to the page's fields.
    /// Returns false when it was refused.
    /// </summary>
    protected bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Core.Services.ValidationException v)
        {
            ShowFieldErrors(v.Fields);
            Shell.ShowError(v.Message);
            return false;
        }
        catch (UserFacingException ex)
        {
            Shell.ShowError(ex.Message);
            return false;
        }
    }

    /// <summary>Pages with forms show these next to the fields. Keys are the field names the services use.</summary>
    protected virtual void ShowFieldErrors(IReadOnlyDictionary<string, string> errors) { }

    protected static T Find<T>(DependencyObject root, string name) where T : DependencyObject =>
        (T)LogicalTreeHelper.FindLogicalNode(root, name);
}
