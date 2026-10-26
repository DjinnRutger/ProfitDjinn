using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Services;

/// <summary>Reads and writes the settings table. Values are text, as in 1.x.</summary>
public sealed class SettingsService
{
    private readonly Database _db;
    private readonly AppPaths? _paths;

    /// <param name="paths">Where the document logo lives; without it <see cref="Company"/> has no logo.</param>
    public SettingsService(Database db, AppPaths? paths = null)
    {
        _db = db;
        _paths = paths;
    }

    public string Get(string key, string fallback = "") =>
        _db.Run(db => db.ExecuteScalar<string?>("SELECT value FROM settings WHERE \"key\" = @key", new { key })) ?? fallback;

    public double GetNumber(string key, double fallback = 0)
    {
        string? raw = _db.Run(db => db.ExecuteScalar<string?>("SELECT value FROM settings WHERE \"key\" = @key", new { key }));
        return raw is null ? fallback : Setting.AsNumber(raw);
    }

    public bool GetBool(string key) => Setting.AsBool(Get(key));

    /// <summary>Sets a value. A key that does not exist yet is created as a text setting.</summary>
    public void Set(string key, string value) => _db.Run(db =>
    {
        int n = db.Execute("UPDATE settings SET value = @value WHERE \"key\" = @key", new { key, value });
        if (n == 0)
            db.Execute("INSERT INTO settings (\"key\", value, type, category) VALUES (@key, @value, 'text', 'general')", new { key, value });
    });

    /// <summary>Every setting, by category then key, as Admin > Settings lists them.</summary>
    public IReadOnlyList<Setting> All() =>
        _db.Run(db => db.Query<Setting>("SELECT * FROM settings ORDER BY category, \"key\"").ToList());

    /// <summary>Saves several values at once (Settings "Save All").</summary>
    public void SetMany(IReadOnlyDictionary<string, string> values) => _db.InTransaction((db, tx) =>
    {
        foreach (var (key, value) in values)
            db.Execute("UPDATE settings SET value = @value WHERE \"key\" = @key", new { key, value }, tx);
    });

    /// <summary>The colour theme. 1.x kept it per user, so the first user's choice carries over.</summary>
    public string Theme()
    {
        string theme = Get(SettingKeys.Theme);
        if (theme is "light" or "dark" or "terminal") return theme;
        string? fromUser = _db.Run(db => db.ExecuteScalar<string?>(
            "SELECT theme FROM users WHERE is_active = 1 ORDER BY id LIMIT 1"));
        return fromUser is "light" or "dark" or "terminal" ? fromUser : "light";
    }

    public void SetTheme(string theme)
    {
        if (theme is not ("light" or "dark" or "terminal"))
            throw new ArgumentException($"Unknown theme '{theme}'.", nameof(theme));
        Set(SettingKeys.Theme, theme);
    }

    public CompanyInfo Company() => new(
        Get(SettingKeys.CompanyName), Get(SettingKeys.CompanyAddress), Get(SettingKeys.CompanyCity),
        Get(SettingKeys.CompanyState), Get(SettingKeys.CompanyZip), Get(SettingKeys.CompanyEmail),
        Get(SettingKeys.CompanyPhone), Get(SettingKeys.CompanyContactLine).Trim(),
        Get(SettingKeys.CompanyServices).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
        DocumentLogo());

    /// <summary>2.9. The logo PNG, or null when none is set or its file is missing.</summary>
    private byte[]? DocumentLogo()
    {
        if (_paths is null || Get(SettingKeys.DocumentLogo).Length == 0) return null;
        try { return File.Exists(_paths.DocumentLogoPath) ? File.ReadAllBytes(_paths.DocumentLogoPath) : null; }
        catch (IOException) { return null; }
    }
}

/// <summary>
/// The business details printed at the top of every invoice. 2.9 adds the optional contact line,
/// services (printed as bullets) and logo (PNG bytes) used on invoices and purchase orders.
/// </summary>
public sealed record CompanyInfo(string Name, string Address, string City, string State, string Zip, string Email, string Phone,
    string ContactLine = "", IReadOnlyList<string>? Services = null, byte[]? Logo = null)
{
    public IReadOnlyList<string> ServiceList => Services ?? Array.Empty<string>();

    /// <summary>True when any of the 2.9 header details is set, so documents use the letterhead header.</summary>
    public bool HasLetterhead => Logo is not null || ContactLine.Length > 0 || ServiceList.Count > 0;
}
