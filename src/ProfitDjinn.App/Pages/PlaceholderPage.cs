using System.Windows.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;

namespace ProfitDjinn.App.Pages;

/// <summary>Stands in for a screen that has not been built yet during the 2.0 rewrite.</summary>
public sealed class PlaceholderPage : AppPage
{
    private readonly string _key;

    public override string NavKey => _key;
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb(_key) };

    public PlaceholderPage(MainWindow shell, string key) : base(shell)
    {
        _key = key;
        var p = new StackPanel();
        p.Children.Add(Ui.PageHeader(key, "This screen is part of a later phase of the 2.0 build."));
        p.Children.Add(Ui.Card(Ui.Empty("hourglass-split", "Not built yet.")));
        Content = p;
    }
}
