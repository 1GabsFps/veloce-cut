using System;
using System.Collections.Generic;
using System.IO;
using Veloce.Core.Models;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class BootstrapperAndSettingsTests
{
    [Fact]
    public void SelectBestAsset_PrefersZipOverInstallerOverCli()
    {
        var assetsWithZip = new List<GitHubReleaseAsset>
        {
            new("blur-Windows-Installer-x64.exe", "https://download/installer.exe", 180000000),
            new("blur-Windows-portable-x64.zip", "https://download/portable.zip", 50000000),
            new("blur-cli-Windows-Release-x64.exe", "https://download/cli.exe", 2500000)
        };

        var selected = BootstrapperService.SelectBestAsset(assetsWithZip);
        Assert.NotNull(selected);
        Assert.Equal("blur-Windows-portable-x64.zip", selected.Name);

        var assetsWithoutZip = new List<GitHubReleaseAsset>
        {
            new("blur-Windows-Installer-x64.exe", "https://download/installer.exe", 180000000),
            new("blur-cli-Windows-Release-x64.exe", "https://download/cli.exe", 2500000)
        };

        var selectedInstaller = BootstrapperService.SelectBestAsset(assetsWithoutZip);
        Assert.NotNull(selectedInstaller);
        Assert.Equal("blur-Windows-Installer-x64.exe", selectedInstaller.Name);
    }

    [Fact]
    public void SettingsManager_SavesAndLoadsPathsCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"qb_settings_{Guid.NewGuid():N}.json");
        try
        {
            var initial = SettingsManager.Load(tempFile);
            Assert.True(string.IsNullOrEmpty(initial.BlurCliPath));

            initial.BlurCliPath = @"C:\FakeEngine\blur-cli.exe";
            initial.FfmpegPath = @"C:\FakeEngine\ffmpeg.exe";
            initial.FfprobePath = @"C:\FakeEngine\ffprobe.exe";

            SettingsManager.Save(tempFile, initial);

            var reloaded = SettingsManager.Load(tempFile);
            Assert.Equal(@"C:\FakeEngine\blur-cli.exe", reloaded.BlurCliPath);
            Assert.Equal(@"C:\FakeEngine\ffmpeg.exe", reloaded.FfmpegPath);
            Assert.Equal(@"C:\FakeEngine\ffprobe.exe", reloaded.FfprobePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EnvironmentDetector_PrioritizesPortableThenLocalAppDataThenSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"qb_env_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var portableDir = Path.Combine(tempDir, "app");
            var localAppDir = Path.Combine(tempDir, "localapp");
            var portableExe = Path.Combine(portableDir, "engine", "blur-cli.exe");
            var localAppExe = Path.Combine(localAppDir, "engine", "blur-cli.exe");

            Directory.CreateDirectory(Path.GetDirectoryName(portableExe)!);
            Directory.CreateDirectory(Path.GetDirectoryName(localAppExe)!);

            File.WriteAllText(portableExe, "fake-portable");
            File.WriteAllText(localAppExe, "fake-localapp");

            var detector = new EnvironmentDetectorService();

            // 1. When portable exists, it must win
            var found = detector.FindBlurCliPath(portableDir, localAppDir, customSettingsPath: null);
            Assert.Equal(portableExe, found);

            // 2. When portable does not exist, localApp must win
            File.Delete(portableExe);
            var foundLocal = detector.FindBlurCliPath(portableDir, localAppDir, customSettingsPath: null);
            Assert.Equal(localAppExe, foundLocal);

            // 3. When both don't exist, settings must win if valid
            File.Delete(localAppExe);
            var customExe = Path.Combine(tempDir, "custom", "blur-cli.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(customExe)!);
            File.WriteAllText(customExe, "fake-custom");

            var foundSettings = detector.FindBlurCliPath(portableDir, localAppDir, customSettingsPath: customExe);
            Assert.Equal(customExe, foundSettings);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
