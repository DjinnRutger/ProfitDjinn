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

    public static AppPaths Default()
    {
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
