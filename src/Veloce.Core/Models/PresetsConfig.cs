using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Veloce.Core.Models;

public class BlurPreset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("blur_amount")]
    public double BlurAmount { get; set; } = 1.35;

    [JsonPropertyName("output_fps")]
    public int OutputFps { get; set; } = 60;

    [JsonPropertyName("blur_weighting")]
    public string BlurWeighting { get; set; } = "equal";

    [JsonPropertyName("blur_gamma")]
    public double BlurGamma { get; set; } = 1.4;

    [JsonPropertyName("interpolate")]
    public bool Interpolate { get; set; } = true;

    [JsonPropertyName("interpolated_fps")]
    public string InterpolatedFps { get; set; } = "1440";

    [JsonPropertyName("interpolation_method")]
    public string InterpolationMethod { get; set; } = "svp";

    [JsonPropertyName("rife_model")]
    public string RifeModel { get; set; } = "rife-v4.26_ensembleFalse";

    [JsonPropertyName("quality")]
    public int Quality { get; set; } = 14;
}

public class CompressionPreset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("target_height")]
    public int TargetHeight { get; set; } = 1080;

    [JsonPropertyName("target_fps")]
    public int TargetFps { get; set; } = 60;

    [JsonPropertyName("cq")]
    public int Cq { get; set; } = 22;
}

public class PresetsConfig
{
    [JsonPropertyName("blur_presets")]
    public List<BlurPreset> BlurPresets { get; set; } = new();

    [JsonPropertyName("compression_presets")]
    public List<CompressionPreset> CompressionPresets { get; set; } = new();

    public static PresetsConfig CreateDefault()
    {
        return new PresetsConfig
        {
            BlurPresets = new List<BlurPreset>
            {
                new()
                {
                    Id = "tekno_default",
                    Name = "Padrão (Tekno Blur)",
                    BlurAmount = 1.35,
                    OutputFps = 60,
                    BlurWeighting = "equal",
                    BlurGamma = 1.4,
                    Interpolate = true,
                    InterpolatedFps = "1440",
                    InterpolationMethod = "svp",
                    RifeModel = "rife-v4.26_ensembleFalse",
                    Quality = 14
                },
                new()
                {
                    Id = "smooth_light",
                    Name = "Suave / Leve",
                    BlurAmount = 0.8,
                    OutputFps = 60,
                    BlurWeighting = "gaussian",
                    BlurGamma = 1.2,
                    Interpolate = true,
                    InterpolatedFps = "720",
                    InterpolationMethod = "svp",
                    RifeModel = "rife-v4.26_ensembleFalse",
                    Quality = 14
                },
                new()
                {
                    Id = "montage_heavy",
                    Name = "Montage / Heavy Blur",
                    BlurAmount = 1.65,
                    OutputFps = 60,
                    BlurWeighting = "equal",
                    BlurGamma = 1.5,
                    Interpolate = true,
                    InterpolatedFps = "1440",
                    InterpolationMethod = "svp",
                    RifeModel = "rife-v4.26_ensembleFalse",
                    Quality = 14
                }
            },
            CompressionPresets = new List<CompressionPreset>
            {
                new()
                {
                    Id = "original",
                    Name = "Original (Sem Compressão)",
                    Enabled = false,
                    TargetHeight = 0,
                    TargetFps = 0,
                    Cq = 0
                },
                new()
                {
                    Id = "1080p60",
                    Name = "1080p 60 FPS (Otimizado)",
                    Enabled = true,
                    TargetHeight = 1080,
                    TargetFps = 60,
                    Cq = 22
                },
                new()
                {
                    Id = "1080p120",
                    Name = "1080p 120 FPS (Fluido)",
                    Enabled = true,
                    TargetHeight = 1080,
                    TargetFps = 120,
                    Cq = 22
                }
            }
        };
    }
}
