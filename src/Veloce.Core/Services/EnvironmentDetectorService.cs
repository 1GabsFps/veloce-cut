using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public class EnvironmentDetectorService
{
    public EnvironmentPaths Detect(string? customBlurCliPath = null, string? settingsFilePath = null)
    {
        var settings = SettingsManager.Load(settingsFilePath);
        string settingsBlurCli = !string.IsNullOrEmpty(customBlurCliPath) ? customBlurCliPath : settings.BlurCliPath;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localAppDir = Directory.Exists(Path.Combine(localAppData, "VELOCE", "engine"))
            ? Path.Combine(localAppData, "VELOCE")
            : Path.Combine(localAppData, "VELOCE");

        string blurCli = FindBlurCliPath(baseDir, localAppDir, settingsBlurCli);
        string? blurDir = !string.IsNullOrEmpty(blurCli) ? Path.GetDirectoryName(blurCli) : null;

        string ffmpeg = FindFfmpegPath(baseDir, localAppDir, blurDir, settings.FfmpegPath);
        string ffprobe = FindFfprobePath(baseDir, localAppDir, blurDir, ffmpeg, settings.FfprobePath);

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string blurConfigDir = Path.Combine(appData, "blur");

        GpuVendor gpu = GpuVendor.Nvidia;
        string blurCfgPath = Path.Combine(blurConfigDir, "blur.cfg");
        if (File.Exists(blurCfgPath))
        {
            gpu = ParseGpuVendorFromBlurCfg(File.ReadAllText(blurCfgPath));
        }

        return new EnvironmentPaths(
            blurCli,
            ffmpeg,
            ffprobe,
            Directory.Exists(blurConfigDir) ? blurConfigDir : null,
            gpu
        );
    }

    public string FindBlurCliPath(string? baseDir = null, string? localAppDir = null, string? customSettingsPath = null)
    {
        // 1. Local portable folder next to executable: ./engine/blur-cli.exe
        if (!string.IsNullOrEmpty(baseDir))
        {
            var portable = Path.Combine(baseDir, "engine", "blur-cli.exe");
            if (File.Exists(portable)) return portable;
        }

        // 2. App data directory: %LOCALAPPDATA%\Veloce\engine\blur-cli.exe
        if (!string.IsNullOrEmpty(localAppDir))
        {
            var localApp = Path.Combine(localAppDir, "engine", "blur-cli.exe");
            if (File.Exists(localApp)) return localApp;
        }

        // 3. Saved configuration path in settings.json
        if (!string.IsNullOrEmpty(customSettingsPath) && File.Exists(customSettingsPath))
        {
            return customSettingsPath;
        }

        // 4. Default system candidates (Program Files, %LOCALAPPDATA%\blur, fixed drives)
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var systemCandidates = new List<string>
        {
            Path.Combine(programFiles, "blur", "blur-cli.exe"),
            Path.Combine(localAppData, "blur", "blur-cli.exe")
        };

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    systemCandidates.Add(Path.Combine(drive.RootDirectory.FullName, "blur", "blur-cli.exe"));
                }
            }
        }
        catch
        {
            // Ignore drive enumeration errors
        }

        foreach (var path in systemCandidates)
        {
            if (File.Exists(path)) return path;
        }

        // 5. PATH environment variable
        var inPath = FindExecutableInPath("blur-cli.exe");
        if (!string.IsNullOrEmpty(inPath)) return inPath;

        return string.Empty;
    }

    public string FindFfmpegPath(string? baseDir = null, string? localAppDir = null, string? blurDir = null, string? customSettingsPath = null)
    {
        // 1. Portable: ./engine/lib/ffmpeg/ffmpeg.exe
        if (!string.IsNullOrEmpty(baseDir))
        {
            var portable = Path.Combine(baseDir, "engine", "lib", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(portable)) return portable;
        }

        // 2. LocalAppData: %LOCALAPPDATA%\Veloce\engine\lib\ffmpeg\ffmpeg.exe
        if (!string.IsNullOrEmpty(localAppDir))
        {
            var localApp = Path.Combine(localAppDir, "engine", "lib", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(localApp)) return localApp;
        }

        // 3. Next to detected blur-cli.exe: {blurDir}\lib\ffmpeg\ffmpeg.exe
        if (!string.IsNullOrEmpty(blurDir))
        {
            var bundled = Path.Combine(blurDir, "lib", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundled)) return bundled;
        }

        // 4. Settings
        if (!string.IsNullOrEmpty(customSettingsPath) && File.Exists(customSettingsPath))
        {
            return customSettingsPath;
        }

        // 5. PATH
        var inPath = FindExecutableInPath("ffmpeg.exe");
        if (!string.IsNullOrEmpty(inPath)) return inPath;

        return "ffmpeg.exe";
    }

    public string FindFfprobePath(string? baseDir = null, string? localAppDir = null, string? blurDir = null, string? ffmpegPath = null, string? customSettingsPath = null)
    {
        // 1. Portable: ./engine/lib/ffmpeg/ffprobe.exe
        if (!string.IsNullOrEmpty(baseDir))
        {
            var portable = Path.Combine(baseDir, "engine", "lib", "ffmpeg", "ffprobe.exe");
            if (File.Exists(portable)) return portable;
        }

        // 2. LocalAppData: %LOCALAPPDATA%\Veloce\engine\lib\ffmpeg\ffprobe.exe
        if (!string.IsNullOrEmpty(localAppDir))
        {
            var localApp = Path.Combine(localAppDir, "engine", "lib", "ffmpeg", "ffprobe.exe");
            if (File.Exists(localApp)) return localApp;
        }

        // 3. Next to blurDir: {blurDir}\lib\ffmpeg\ffprobe.exe
        if (!string.IsNullOrEmpty(blurDir))
        {
            var bundled = Path.Combine(blurDir, "lib", "ffmpeg", "ffprobe.exe");
            if (File.Exists(bundled)) return bundled;
        }

        // 4. Beside ffmpegPath
        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            var ffmpegDir = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrEmpty(ffmpegDir))
            {
                var probeBeside = Path.Combine(ffmpegDir, "ffprobe.exe");
                if (File.Exists(probeBeside)) return probeBeside;
            }
        }

        // 5. Settings
        if (!string.IsNullOrEmpty(customSettingsPath) && File.Exists(customSettingsPath))
        {
            return customSettingsPath;
        }

        // 6. PATH
        var inPath = FindExecutableInPath("ffprobe.exe");
        if (!string.IsNullOrEmpty(inPath)) return inPath;

        return "ffprobe.exe";
    }

    public static (TimeSpan Duration, double Fps) ParseFfprobeOutput(string durationOutput, string fpsOutput)
    {
        double durationSec = 0;
        if (double.TryParse(durationOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSec))
        {
            durationSec = parsedSec;
        }

        double fps = 60.0;
        var fpsTrimmed = fpsOutput.Trim();
        if (fpsTrimmed.Contains('/'))
        {
            var parts = fpsTrimmed.Split('/');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                den > 0)
            {
                fps = num / den;
            }
        }
        else if (double.TryParse(fpsTrimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var directFps))
        {
            fps = directFps;
        }

        return (TimeSpan.FromSeconds(durationSec), fps > 0 ? fps : 60.0);
    }

    public (TimeSpan Duration, double Fps) ProbeVideoMetadata(string ffprobePath, string videoPath)
    {
        if (string.IsNullOrEmpty(ffprobePath) || !File.Exists(videoPath))
        {
            return (TimeSpan.Zero, 60.0);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -show_entries format=duration -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (TimeSpan.Zero, 60.0);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string durStr = lines.Length > 0 ? lines[0] : "0";
            string fpsStr = lines.Length > 1 ? lines[1] : "60/1";

            if (lines.Length >= 2 && lines[0].Contains('/'))
            {
                fpsStr = lines[0];
                durStr = lines[1];
            }

            return ParseFfprobeOutput(durStr, fpsStr);
        }
        catch
        {
            return (TimeSpan.Zero, 60.0);
        }
    }

    public GpuVendor ParseGpuVendorFromBlurCfg(string blurCfgContent)
    {
        var lines = blurCfgContent.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("gpu type", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(':');
                if (parts.Length > 1)
                {
                    var val = parts[1].Trim().ToLowerInvariant();
                    if (val.Contains("nvidia")) return GpuVendor.Nvidia;
                    if (val.Contains("amd")) return GpuVendor.Amd;
                    if (val.Contains("intel")) return GpuVendor.Intel;
                    if (val.Contains("cpu")) return GpuVendor.Cpu;
                }
            }
        }
        return GpuVendor.Nvidia;
    }

    private static string? FindExecutableInPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var dirs = pathEnv.Split(Path.PathSeparator);
        foreach (var dir in dirs)
        {
            try
            {
                var full = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // Ignore invalid PATH entries
            }
        }
        return null;
    }
}
