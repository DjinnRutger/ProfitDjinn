using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.Core.Services;

public enum BackupCompatibility { Compatible, NeedsMigration, Incompatible }

public sealed record BackupAnalysis(
    BackupCompatibility Status,
    string Message,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> ExtraTables,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MissingColumns,
    IReadOnlyDictionary<string, long> RowCounts)
{
    public long TotalRows => RowCounts.Values.Sum();
}

/// <summary>
/// Backup and restore of app.db. A port of 1.x app/blueprints/database_mgr.py, with one
/// change: backups use SQLite's online backup, not a raw file copy, so a backup taken while
/// the app is running is always consistent.
/// </summary>
public sealed class BackupService
{
    /// <summary>A restore is refused unless these tables and columns exist in the backup.</summary>
    private static readonly (string Table, string[] Columns)[] Critical =
    {
        ("users", new[] { "id", "username", "email", "password_hash" }),
        ("settings", new[] { "id", "key", "value" }),
        ("roles", new[] { "id", "name" }),
    };

    private readonly Database _db;

    public BackupService(Database db) => _db = db;

    public static string SuggestedFileName(DateTime now) =>
        $"profitdjinn_backup_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.db";

    /// <summary>Writes a complete copy of the database to <paramref name="destination"/>.</summary>
    public void Backup(string destination)
    {
        string partial = destination + ".partial";
        try
        {
            if (File.Exists(partial)) File.Delete(partial);
            using (var source = _db.Open())
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = partial, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
            }
            File.Move(partial, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            TryDelete(partial);
            throw new UserFacingException(
                $"The backup could not be written to\n{destination}\n\n{ex.Message}\n\nChoose another folder and try again.", ex);
        }
    }

    /// <summary>Checks a backup file before restoring it. Nothing is changed.</summary>
    public BackupAnalysis Analyze(string backupPath)
    {
        RequireSqliteFile(backupPath);
        using var backup = OpenReadOnly(backupPath);
        var backupTables = Tables(backup);
        var liveTables = _db.Run(Tables);

        var missingTables = liveTables.Keys.Except(backupTables.Keys).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var extraTables = backupTables.Keys.Except(liveTables.Keys).OrderBy(t => t, StringComparer.Ordinal).ToList();

        var missingColumns = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (table, liveCols) in liveTables)
        {
            if (!backupTables.TryGetValue(table, out var backupCols)) continue;
            var missing = liveCols.Except(backupCols, StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToList();
            if (missing.Count > 0) missingColumns[table] = missing;
        }

        var rowCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (string table in backupTables.Keys)
            rowCounts[table] = backup.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"");

        bool critical = Critical.Any(c =>
            !backupTables.TryGetValue(c.Table, out var cols) || c.Columns.Any(col => !cols.Contains(col, StringComparer.OrdinalIgnoreCase)));

        var (status, message) = critical
            ? (BackupCompatibility.Incompatible, "This file is missing tables or columns ProfitDjinn cannot work without. It cannot be restored.")
            : missingTables.Count == 0 && missingColumns.Count == 0
                ? (BackupCompatibility.Compatible, "This backup matches the current database and can be restored.")
                : (BackupCompatibility.NeedsMigration, "This backup is from an older version. It can be restored, and the missing tables and columns will be added.");

        return new BackupAnalysis(status, message, missingTables, extraTables, missingColumns, rowCounts);
    }

    /// <summary>
    /// Replaces the whole database with the backup. A safety copy of the current file is kept
    /// next to it (app.db.pre_restore_YYYYmmdd_HHMMSS) and put back automatically if anything
    /// fails. Returns the safety copy's path.
    /// </summary>
    public string Restore(string backupPath, DateTime now)
    {
        if (Analyze(backupPath).Status == BackupCompatibility.Incompatible)
            throw new UserFacingException("This file cannot be restored: it is missing tables or columns ProfitDjinn needs. Nothing was changed.");

        string live = _db.Path;
        string safety = $"{live}.pre_restore_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
        Backup(safety);
        try
        {
            using (var source = OpenReadOnly(backupPath))
            using (var target = _db.Open())
                source.BackupDatabase(target);
            Schema.Ensure(_db);
            return safety;
        }
        catch (Exception ex)
        {
            try
            {
                using (var source = OpenReadOnly(safety))
                using (var target = _db.Open())
                    source.BackupDatabase(target);
            }
            catch (Exception rollbackEx)
            {
                throw new UserFacingException(
                    $"The restore failed, and putting the old database back also failed. Your data before the restore is safe in\n{safety}\n\n" +
                    $"Close ProfitDjinn, copy that file over\n{live}\nand start ProfitDjinn again.\n\nRestore error: {ex.Message}\nRollback error: {rollbackEx.Message}", ex);
            }
            throw new UserFacingException($"The restore failed, so the database was put back the way it was. Nothing was changed.\n\n{ex.Message}", ex);
        }
    }

    private static void RequireSqliteFile(string path)
    {
        byte[] header = new byte[16];
        try
        {
            using var f = File.OpenRead(path);
            if (f.Read(header, 0, 16) < 16) header = Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"That file could not be opened:\n{path}\n\n{ex.Message}", ex);
        }
        if (header.Length < 15 || Encoding.ASCII.GetString(header, 0, 15) != "SQLite format 3")
            throw new UserFacingException("That file is not a ProfitDjinn backup. Backups are SQLite database files (.db).");
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        c.Open();
        return c;
    }

    private static Dictionary<string, HashSet<string>> Tables(SqliteConnection db) =>
        db.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
          .ToDictionary(t => t, t => Schema.Columns(db, null, t), StringComparer.Ordinal);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
