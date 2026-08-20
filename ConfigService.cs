using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RockyBackup;

public sealed class ConfigService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RockyBackup");

    public string ConfigPath => Path.Combine(BaseDirectory, "settings.json");
    public string LogDirectory => Path.Combine(BaseDirectory, "logs");

    public ConfigService()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(BaseDirectory);
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
    }

    public static string ProtectPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        var data = Encoding.UTF8.GetBytes(plainText);
        var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public static string UnprotectPassword(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return "";
        try
        {
            var data = Convert.FromBase64String(cipherText);
            var plain = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }
}
