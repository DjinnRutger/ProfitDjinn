using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// 2.2. Expense categories: add, rename, hide or show, and delete an unused one. Laid out like
/// the service item list.
/// </summary>
public sealed class ExpenseCategoriesPage : AppPage
{
    private readonly TextBox _newName;
    private readonly Field _newField;

    public override string NavKey => "expenses";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Expenses", () => Shell.Navigate(Routes.Expenses(Shell))), new Crumb("Categories") };

    public ExpenseCategoriesPage(MainWindow shell) : base(shell)
    {
        var categories = Store.Categories.List();

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("tags", "Expense Categories",
            "Categories group your expenses for reports and taxes. Hide one you don't use; it stays on the expenses that have it."));

        _newName = Ui.TextBox(null, "New category name…", 320);
        _newName.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
        _newField = Ui.Field("New Category", Ui.Row(8, _newName, Ui.Button("Add", "Btn.Primary", "plus-lg", Add, small: true)));
        page.Children.Add(Ui.Card(_newField, bodyPadding: new Thickness(16, 16, 16, 4)).Margin(0, 0, 0, 24));

        var columns = new List<Column<CategoryUsage>>
        {
            new("Name", Ui.Star(), u =>
            {
                var t = Ui.Text(u.Category.Name, "Body");
                if (!u.Category.IsActive) { t.TextDecorations = TextDecorations.Strikethrough; t.Opacity = 0.6; t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); }
                return t;
            }),
            new("Used", Ui.Px(110), u => Ui.Muted(u.InUse ? $"{u.Expenses + u.Recurring}" : "—", 13.6), HorizontalAlignment.Center),
            new("Status", Ui.Px(100), u => u.Category.IsActive ? Ui.Badge("Shown", "success75") : Ui.Badge("Hidden", "secondary"), HorizontalAlignment.Center),
            new("", Ui.Px(140), u =>
            {
                var delete = Ui.IconButton("trash", "Btn.OutlineDanger", u.InUse ? "In use — hide it instead" : "Delete", () => Delete(u));
                if (u.InUse) { delete.IsEnabled = false; ToolTipService.SetShowOnDisabled(delete, true); }
                return Ui.Row(4,
                    Ui.IconButton("pencil", "Btn.OutlinePrimary", "Rename", () => Rename(u)),
                    Ui.IconButton(u.Category.IsActive ? "eye-slash" : "eye", "Btn.OutlineSecondary", u.Category.IsActive ? "Hide" : "Show",
                        () => Try(() => Shell.Reload(Store.Categories.ToggleActive(u.Category.Id)))),
                    delete);
            }, HorizontalAlignment.Right),
        };
        page.Children.Add(Ui.Card(Table.Build(columns, categories), bodyPadding: new Thickness(0)));
        Content = page;
    }

    public override void OnShown() => _newName.Focus();

    private void Add()
    {
        _newField.Error = null;
        try { Shell.Reload(Store.Categories.Create(_newName.Text).Notice); }
        catch (ValidationException v) { _newField.Error = v.Message; }
        catch (Core.UserFacingException ex) { Shell.ShowError(ex.Message); }
    }

    private async void Rename(CategoryUsage u)
    {
        var box = Ui.TextBox(u.Category.Name);
        var field = Ui.Field("Name", box, required: true);
        await Shell.OpenDialog("Rename Category", "pencil", field, "Save", () =>
        {
            field.Error = null;
            try
            {
                Shell.Reload(Store.Categories.Rename(u.Category.Id, box.Text));
                return true;
            }
            catch (ValidationException v) { field.Error = v.Message; return false; }
            catch (Core.UserFacingException ex) { field.Error = ex.Message; return false; }
        }, primaryGlyph: "check-lg", maxWidth: 440);
    }

    private async void Delete(CategoryUsage u)
    {
        if (!await Shell.Confirm($"Delete the category '{u.Category.Name}'?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Categories.Delete(u.Category.Id)));
    }
}
