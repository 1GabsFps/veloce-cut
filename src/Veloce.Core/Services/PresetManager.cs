using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public class PresetManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private const string DefaultTemplateCfg = @"[blur v2.45]

- blur
blur: true
blur amount: 1.35
blur output fps: 60
blur weighting: equal
blur gamma: 1.4

- interpolation
interpolate: true
interpolated fps: 1440
interpolation method: svp

- pre-interpolation
pre-interpolate: false
pre-interpolated fps: 360

- deduplication
deduplicate: true
deduplicate method: svp

- rendering
encode preset: h264
quality: 14
preview: false
detailed filenames: false
copy dates: false

- gpu acceleration
gpu decoding: true
gpu interpolation: true
gpu encoding: true

- timescale
timescale: false
input timescale: 1
output timescale: 1
adjust timescaled audio pitch: false

- filters
filters: false
brightness: 1
saturation: 1
contrast: 1

- advanced
advanced: true

- advanced deduplication
deduplicate range: 2
deduplicate threshold: 0.001

- advanced rendering
video container: mp4
custom ffmpeg filters: 
debug: false

- advanced blur
blur weighting gaussian std dev: 2
blur weighting gaussian mean: 2
blur weighting gaussian bound: [0,2]

- advanced interpolation
svp interpolation preset: default
svp interpolation algorithm: 13
interpolation block size: 16
interpolation mask area: 0
rife model: rife-v4.26_ensembleFalse

- manual svp override
manual svp: false
super string: 
vectors string: 
smooth string: 
";

    public PresetsConfig LoadOrCreateDefault(string jsonFilePath)
    {
        if (File.Exists(jsonFilePath))
        {
            try
            {
                var json = File.ReadAllText(jsonFilePath);
                var config = JsonSerializer.Deserialize<PresetsConfig>(json, JsonOptions);
                if (config != null) return config;
            }
            catch
            {
                // Fall back to creating default if corrupted
            }
        }

        var defaultConfig = PresetsConfig.CreateDefault();
        Save(jsonFilePath, defaultConfig);
        return defaultConfig;
    }

    public void Save(string jsonFilePath, PresetsConfig config)
    {
        var dir = Path.GetDirectoryName(jsonFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(jsonFilePath, json, Encoding.UTF8);
    }

    public string GenerateTeknoBlurCfg(BlurPreset preset, string? existingUserBlurCfgContent = null)
    {
        var template = !string.IsNullOrWhiteSpace(existingUserBlurCfgContent)
            ? existingUserBlurCfgContent
            : DefaultTemplateCfg;

        var overrides = new Dictionary<string, string>
        {
            ["blur amount"] = preset.BlurAmount.ToString("0.##", CultureInfo.InvariantCulture),
            ["blur output fps"] = preset.OutputFps.ToString(CultureInfo.InvariantCulture),
            ["blur weighting"] = preset.BlurWeighting,
            ["blur gamma"] = preset.BlurGamma.ToString("0.##", CultureInfo.InvariantCulture),
            ["interpolate"] = preset.Interpolate ? "true" : "false",
            ["interpolated fps"] = preset.InterpolatedFps,
            ["interpolation method"] = preset.InterpolationMethod,
            ["rife model"] = preset.RifeModel,
            ["quality"] = preset.Quality.ToString(CultureInfo.InvariantCulture)
        };

        var lines = template.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            bool replaced = false;

            foreach (var kv in overrides)
            {
                if (trimmed.StartsWith(kv.Key + ":", StringComparison.OrdinalIgnoreCase))
                {
                    result.AppendLine($"{kv.Key}: {kv.Value}");
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }
}
