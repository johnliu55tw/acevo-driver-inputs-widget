using System.Text.Json;
using System.IO;

namespace ACEvo_Simple_Telemetry;

public sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACEvoSimpleTelemetry");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public bool ShowThrottle { get; set; } = true;
    public bool ShowBrake { get; set; } = true;
    public bool ShowClutch { get; set; } = true;
    public int GraphTimeSpanSeconds { get; set; } = 10;
    public bool PositionLocked { get; set; }
    public double WindowScale { get; set; } = 1.0;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? LiveWindowWidth { get; set; }
    public double? LiveWindowHeight { get; set; }
    public double? StatusWindowWidth { get; set; }
    public double? StatusWindowHeight { get; set; }
    public string Theme { get; set; } = "Dark";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Invalid or inaccessible settings should never prevent the overlay from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Runtime telemetry remains usable even when preferences cannot be persisted.
        }
    }
}
