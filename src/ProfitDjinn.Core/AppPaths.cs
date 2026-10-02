namespace ProfitDjinn.Core;

/// <summary>
/// Where ProfitDjinn keeps its data: %LOCALAPPDATA%\ProfitDjinn\. The same folder the 1.x
/// (Python) build used, so 2.0 opens the existing database where it already is.
/// Tests and smoke runs pass their own folder instead.
/// </summary>
public sealed class AppPaths
{
    public const string FolderName = "ProfitDjinn";

    public string DataFolder { get; }

    public AppPaths(string dataFolder) => DataFolder = dataFolder;

    /// <summary>
    /// Points the app at another data folder: smoke tests and screenshots use it so they never
    /// open the real database. .NET ignores a changed LOCALAPPDATA variable (it asks Windows
    /// for the known folder), so this explicit variable is the only override.
    /// </summary>
    public const string OverrideVariable = "PROFITDJINN_DATA_DIR";

    /// <summary>True when the data folder came from <see cref="OverrideVariable"/>. The window title says so.</summary>
    public bool IsOverride { get; private init; }

    public static AppPaths Default()
    {
        string? forced = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(forced)) return new AppPaths(Path.GetFullPath(forced)) { IsOverride = true };

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
            throw new UserFacingException(
                "Windows did not say where this account's local app data folder is, so ProfitDjinn " +
                "cannot find its database. Sign out of Windows and back in, then start ProfitDjinn again.");
        return new AppPaths(Path.Combine(local, FolderName));
    }

    public string DatabasePath => Path.Combine(DataFolder, "app.db");

    /// <summary>Custom login logo and sidebar icon. In 1.x these lived inside the program folder.</summary>
    public string BrandingFolder => Path.Combine(DataFolder, "branding");

    public string LoginLogoPath => Path.Combine(BrandingFolder, "login_logo.png");

    public string AppIconPath => Path.Combine(BrandingFolder, "app_icon.png");

    /// <summary>Per-user window state (sidebar collapsed, window size). Not in the database.</summary>
    public string UiStatePath => Path.Combine(DataFolder, "ui.json");

    public void EnsureExists() => Directory.CreateDirectory(DataFolder);
}
