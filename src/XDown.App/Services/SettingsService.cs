using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace XDown.App.Services;

/// <summary>
/// Manages application settings persisted to %AppData%\XDown\settings.json
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xdownDir = Path.Combine(appDataPath, "XDown");
        _settingsPath = Path.Combine(xdownDir, "settings.json");

        // Ensure directory exists
        Directory.CreateDirectory(xdownDir);
    }

    /// <summary>
    /// Load settings from disk. Returns empty settings if file doesn't exist.
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _settings = new AppSettings();
                return _settings;
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            return _settings;
        }
        catch
        {
            // If deserialization fails, start fresh
            _settings = new AppSettings();
            return _settings;
        }
    }

    /// <summary>
    /// Save current settings to disk.
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            _settings = settings;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch
        {
            // Silently fail on save errors
        }
    }

    /// <summary>
    /// Get current in-memory settings.
    /// </summary>
    public AppSettings GetSettings() => _settings;
}
