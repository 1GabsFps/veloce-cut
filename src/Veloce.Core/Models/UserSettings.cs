using System.Text.Json.Serialization;

namespace Veloce.Core.Models;

public class UserSettings
{
    [JsonPropertyName("blurCliPath")]
    public string BlurCliPath { get; set; } = string.Empty;

    [JsonPropertyName("ffmpegPath")]
    public string FfmpegPath { get; set; } = string.Empty;

    [JsonPropertyName("ffprobePath")]
    public string FfprobePath { get; set; } = string.Empty;
}
