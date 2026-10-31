using System;
using System.IO;
using System.Text.Json;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetDefaultSettingsFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var velocePath = Path.Combine(localAppData, "VELOCE", "settings.json");
        var legacyPath = Path.Combine(localAppData, "VELOCE", "settings.json");

        if (!File.Exists(velocePath) && File.Exists(legacyPath))
        {
            return legacyPath;
        }

        return velocePath;
    }

    public static UserSettings Load(string? filePath = null)
    {
        filePath ??= GetDefaultSettingsFilePath();

        if (!File.Exists(filePath))
        {
            return new UserSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public static void Save(string? filePath, UserSettings settings)
    {
        filePath ??= GetDefaultSettingsFilePath();

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(filePath, json);
    }
}
