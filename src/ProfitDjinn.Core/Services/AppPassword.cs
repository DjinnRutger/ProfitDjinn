using System.Security.Cryptography;
using System.Text;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.Core.Services;

/// <summary>
/// The optional app password, asked for when ProfitDjinn starts. Off until one is set.
/// Stored as "pbkdf2_sha256$iterations$salt$hash" (base64), using .NET's built-in PBKDF2,
/// so no extra library is needed. It keeps casual eyes out on a shared PC; it does not
/// encrypt the database file.
/// </summary>
public sealed class AppPassword
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    public const int MinimumLength = 8;

    private readonly SettingsService _settings;

    public AppPassword(SettingsService settings) => _settings = settings;

    public bool IsSet => _settings.Get(SettingKeys.AppPasswordHash).Length > 0;

    public bool Verify(string password)
    {
        string stored = _settings.Get(SettingKeys.AppPasswordHash);
        if (stored.Length == 0) return true;
        return Matches(stored, password ?? "");
    }

    /// <summary>Sets or changes the password. Changing it requires the current one.</summary>
    public void Set(string? current, string newPassword, string confirm)
    {
        if (IsSet && !Verify(current ?? "")) throw new UserFacingException("Current password is incorrect.");
        if ((newPassword ?? "").Length < MinimumLength) throw new UserFacingException($"New password must be at least {MinimumLength} characters.");
        if (newPassword != confirm) throw new UserFacingException("Passwords do not match.");
        if (IsSet && current == newPassword) throw new UserFacingException("New password must be different from the current one.");
        _settings.Set(SettingKeys.AppPasswordHash, Hash(newPassword));
    }

    /// <summary>Turns the password off. Requires the current one.</summary>
    public void Remove(string current)
    {
        if (!Verify(current)) throw new UserFacingException("Current password is incorrect.");
        _settings.Set(SettingKeys.AppPasswordHash, "");
    }

    internal static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2_sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    internal static bool Matches(string stored, string password)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2_sha256" || !int.TryParse(parts[1], out int iterations)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
