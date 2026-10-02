using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using Microsoft.Win32;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// Backup and restore (1.x admin/database.html, Backup and Restore tabs). A backup is a
/// complete copy of the database. Restoring checks the file first, keeps a safety copy of the
/// current data, and puts it back automatically if anything goes wrong.
/// </summary>
public sealed class BackupPage : AppPage
{
    private readonly StackPanel _restoreArea = new();

    public override string NavKey => "backup";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Backup & Restore") };

    public BackupPage(MainWindow shell) : base(shell)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("database", "Backup & Restore", "Keep a copy of everything, and put it back when you need to."));

        // ---- database facts (1.x Overview tab)
        string path = Store.Database.Path;
        var tables = Store.Database.Run(db => db.Query<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name").ToList());
        var counts = Store.Database.Run(db => tables.ToDictionary(t => t, t => db.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{t}\"")));
        var size = File.Exists(path) ? new FileInfo(path).Length : 0;
        var facts = new StackPanel();
        facts.Children.Add(Fact("Database file", path));
        facts.Children.Add(Fact("Size", HumanSize(size)));
        facts.Children.Add(Fact("Tables", $"{tables.Count} ({counts.Values.Sum():N0} rows)"));
        var bars = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        long maxRows = Math.Max(1, counts.Values.DefaultIfEmpty(0).Max());
        foreach (var (table, rows) in counts.OrderByDescending(kv => kv.Value).Take(8))
            bars.Children.Add(TableBar(table, rows, maxRows));
        facts.Children.Add(bars);
        var dbCard = Ui.Card(facts, "Database", "hdd");

        // ---- backup
        var backupBody = Ui.Stack(12,
            Ui.Text("A backup is a complete copy of the database: customers, invoices, payments, work orders and settings. Keep it somewhere other than this PC.", "Body", 14.4, wrap: true),
            Ui.Button("Download Backup", "Btn.Primary", "cloud-download", Backup).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        var backupCard = Ui.Card(backupBody, "Backup", "download", "Success");

        // ---- restore
        var restoreIntro = Ui.Stack(12,
            Ui.Text("Choose a backup file. It is checked first; nothing changes until you confirm.", "Body", 14.4, wrap: true),
            Ui.Button("Choose Backup File…", "Btn.OutlinePrimary", "folder2-open", ChooseBackup).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left),
            _restoreArea);
        var restoreCard = Ui.Card(restoreIntro, "Restore", "upload", "Warning");

        page.Children.Add(Ui.Columns(24, (Ui.Star(), Ui.Stack(24, backupCard, restoreCard)), (Ui.Star(), dbCard)));
        Content = page;
    }

    private static FrameworkElement Fact(string label, string value)
    {
        var g = Ui.Columns(8, (Ui.Px(130), Ui.Muted(label, 14.4)), (Ui.Star(), Ui.Text(value, "Body", 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap)));
        return g.Margin(0, 0, 0, 6);
    }

    private static FrameworkElement TableBar(string table, long rows, long max)
    {
        var bar = new Border { Height = 10, CornerRadius = new CornerRadius(5) }.WithResource(Border.BackgroundProperty, "SecondaryBg");
        var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left }.WithResource(Border.BackgroundProperty, "BrandPrimary");
        var track = new Grid();
        track.Children.Add(bar);
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * rows / max;
        return Ui.Columns(8,
            (Ui.Px(150), Ui.Text(table, "Body", 13).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"))),
            (Ui.Star(), track.Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
            (Ui.Px(60), Ui.Muted(rows.ToString("N0", CultureInfo.InvariantCulture), 12.5).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right))).Margin(0, 0, 0, 6);
    }

    private static string HumanSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
    };

    private void Backup() => SaveBackupAs(Shell);

    /// <summary>
    /// Asks where to save, writes the backup and says where it went. Used by this page and the
    /// start-up reminder. A saved backup restarts the reminder countdown.
    /// </summary>
    public static void SaveBackupAs(MainWindow shell)
    {
        var dialog = new SaveFileDialog
        {
            FileName = BackupService.SuggestedFileName(DateTime.Now),
            Filter = "ProfitDjinn backup (*.db)|*.db",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog(shell) != true) return;
        try
        {
            shell.Store.Backups.Backup(dialog.FileName);
            shell.Store.BackupReminder.Restart();
            shell.ShowNotice(Notice.Success($"Backup saved to {dialog.FileName}"));
        }
        catch (UserFacingException ex) { shell.ShowError(ex.Message); }
    }

    private void ChooseBackup()
    {
        var dialog = new OpenFileDialog { Filter = "ProfitDjinn backup (*.db)|*.db|All files (*.*)|*.*" };
        if (dialog.ShowDialog(Shell) != true) return;
        _restoreArea.Children.Clear();
        BackupAnalysis analysis;
        try { analysis = Store.Backups.Analyze(dialog.FileName); }
        catch (UserFacingException ex) { Shell.ShowError(ex.Message); return; }

        string kind = analysis.Status switch
        {
            BackupCompatibility.Compatible => "Success",
            BackupCompatibility.NeedsMigration => "Warning",
            _ => "Danger",
        };
        var alert = new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Child = Ui.Text(analysis.Message, "Body", 14, wrap: true).WithResource(TextBlock.ForegroundProperty, $"Alert.{kind}.Fg") }
            .WithResource(Border.BackgroundProperty, $"Alert.{kind}.Bg").WithResource(Border.BorderBrushProperty, $"Alert.{kind}.Border").WithResource(Border.CornerRadiusProperty, "Radius");
        _restoreArea.Children.Add(alert);

        var stats = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        stats.Children.Add(Ui.Badge($"{analysis.RowCounts.Count} tables", "secondary").Margin(0, 0, 6, 6));
        stats.Children.Add(Ui.Badge($"{analysis.TotalRows:N0} rows", "secondary").Margin(0, 0, 6, 6));
        if (analysis.MissingTables.Count > 0) stats.Children.Add(Ui.Badge($"{analysis.MissingTables.Count} missing tables", "warningdark").Margin(0, 0, 6, 6));
        if (analysis.MissingColumns.Count > 0) stats.Children.Add(Ui.Badge($"{analysis.MissingColumns.Values.Sum(c => c.Count)} missing columns", "warningdark").Margin(0, 0, 6, 6));
        _restoreArea.Children.Add(stats);
        if (analysis.MissingTables.Count > 0) _restoreArea.Children.Add(Ui.Muted("Missing tables: " + string.Join(", ", analysis.MissingTables), 13).Also(t => t.TextWrapping = TextWrapping.Wrap));
        foreach (var (table, cols) in analysis.MissingColumns)
            _restoreArea.Children.Add(Ui.Muted($"{table}: missing {string.Join(", ", cols)}", 13).Also(t => t.TextWrapping = TextWrapping.Wrap));
        var top = string.Join("   ", analysis.RowCounts.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value:N0}"));
        _restoreArea.Children.Add(Ui.Muted(top, 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 4, 0, 0));

        if (analysis.Status == BackupCompatibility.Incompatible) return;
        var confirm = new CheckBox { Content = "I understand this replaces ALL current data with the backup.", Margin = new Thickness(0, 12, 0, 8) };
        var restore = Ui.Button("Restore This Backup", "Btn.Danger", "arrow-counterclockwise", () => Restore(dialog.FileName)).Also(b => { b.IsEnabled = false; b.HorizontalAlignment = HorizontalAlignment.Left; });
        confirm.Click += (_, _) => restore.IsEnabled = confirm.IsChecked == true;
        _restoreArea.Children.Add(confirm);
        _restoreArea.Children.Add(restore);
    }

    private void Restore(string file)
    {
        Try(() =>
        {
            string safety = Store.Backups.Restore(file, DateTime.Now);
            ThemeManager.Apply(Store.Settings.Theme(), Store.Settings.Get(Core.Data.SettingKeys.PrimaryColor));
            Shell.RefreshChrome();
            Shell.Navigate(Routes.Dashboard(Shell), Notice.Success($"Backup restored. Your data from before the restore is kept in {safety}"));
        });
    }
}
