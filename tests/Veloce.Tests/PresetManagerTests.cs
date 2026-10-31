using System;
using System.Globalization;
using System.IO;
using Veloce.Core.Models;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class PresetManagerTests
{
    [Fact]
    public void LoadOrCreateDefault_CreatesDefaultPresetsFile_WithThreeBlurAndThreeCompressionPresets()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"presets_test_{Guid.NewGuid()}.json");
        try
        {
            var manager = new PresetManager();
            var config = manager.LoadOrCreateDefault(tempFile);

            Assert.True(File.Exists(tempFile));
            Assert.Equal(3, config.BlurPresets.Count);
            Assert.Equal(3, config.CompressionPresets.Count);
            Assert.Equal("tekno_default", config.BlurPresets[0].Id);
            Assert.Equal("original", config.CompressionPresets[0].Id);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GenerateTeknoBlurCfg_PreservesHeaderAndOverridesValuesWithInvariantCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var manager = new PresetManager();
            var preset = new BlurPreset
            {
                Id = "custom",
                Name = "Custom",
                BlurAmount = 1.65,
                OutputFps = 60,
                BlurWeighting = "gaussian",
                BlurGamma = 1.5,
                Interpolate = true,
                InterpolatedFps = "1440",
                InterpolationMethod = "svp",
                RifeModel = "rife-v4.26_ensembleFalse",
                Quality = 14
            };

            string cfg = manager.GenerateTeknoBlurCfg(preset);

            Assert.Contains("[blur v2.45]", cfg);
            Assert.Contains("blur amount: 1.65", cfg);
            Assert.Contains("blur gamma: 1.5", cfg);
            Assert.Contains("blur weighting: gaussian", cfg);
            Assert.Contains("interpolated fps: 1440", cfg);
            Assert.Contains("- deduplication", cfg);
            Assert.DoesNotContain("1,65", cfg);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void LoadOrCreateDefault_SupportsMultiplePresetsAndPreservesAllItems()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"presets_multi_{Guid.NewGuid()}.json");
        try
        {
            var manager = new PresetManager();
            var config = new PresetsConfig
            {
                BlurPresets = new System.Collections.Generic.List<BlurPreset>
                {
                    new() { Id = "p1", Name = "Preset 1" },
                    new() { Id = "p2", Name = "Preset 2" },
                    new() { Id = "p3", Name = "Preset 3" },
                    new() { Id = "p4", Name = "Preset 4" },
                    new() { Id = "p5", Name = "Preset 5" },
                    new() { Id = "p6", Name = "Preset 6" }
                }
            };
            manager.Save(tempFile, config);

            var reloaded = manager.LoadOrCreateDefault(tempFile);
            Assert.Equal(6, reloaded.BlurPresets.Count);
            Assert.Equal("Preset 4", reloaded.BlurPresets[3].Name);
            Assert.Equal("Preset 6", reloaded.BlurPresets[5].Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
