using System.Globalization;
using Dapper;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.Core.Services;

/// <summary>
/// Saves a copy of the database before a different version of ProfitDjinn changes it. The
/// copy goes to the data folder's backups\ folder and the newest five are kept. A database
/// that has never recorded a version (made by 1.x or by 2.0/2.1 before this existed) counts
/// as an upgrade. A brand-new database has nothing to save.
/// </summary>
public static class UpgradeBackup
{
    public const int Keep = 5;
    public const string FilePrefix = "pre-upgrade_";

    /// <summary>Returns the path of the copy it saved, or null when none was needed.</summary>
    public static string? SaveIfVersionChanged(Database database, AppPaths paths, string appVersion, DateTime now)
    {
        if (!File.Exists(database.Path) || new FileInfo(database.Path).Length == 0) return null;

        string? lastVersion = database.Run(db =>
            db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'settings')")
                ? db.ExecuteScalar<string?>("SELECT value FROM settings WHERE \"key\" = @key", new { key = SettingKeys.DatabaseAppVersion })
                : null);
        if (lastVersion == appVersion) return null;

        string from = string.IsNullOrWhiteSpace(lastVersion) ? "older" : Safe(lastVersion);
        string file = Path.Combine(paths.UpgradeBackupFolder,
            $"{FilePrefix}{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}_from-{from}_to-{Safe(appVersion)}.db");
        try
        {
            Directory.CreateDirectory(paths.UpgradeBackupFolder);
            new BackupService(database).Backup(file);
        }
        catch (Exception ex) when (ex is UserFacingException or IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException(
                $"This version of ProfitDjinn saves a copy of your data before updating it, and the copy could not be saved to\n{paths.UpgradeBackupFolder}\n\n" +
                $"{(ex is UserFacingException ? ex.InnerException?.Message ?? ex.Message : ex.Message)}\n\n" +
                "Your data has not been changed. Make sure the drive has free space, then start ProfitDjinn again.", ex);
        }
        Prune(paths.UpgradeBackupFolder);
        return file;
    }

    /// <summary>Deletes all but the newest <see cref="Keep"/> upgrade copies. Names start with the date, so name order is age order.</summary>
    private static void Prune(string folder)
    {
        var old = Directory.GetFiles(folder, FilePrefix + "*.db")
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(Keep);
        foreach (string f in old)
        {
            // Failing to tidy up is not worth stopping the app for; the next start tries again.
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Safe(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
}
