using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public record RenderProgress(string StageName, double Percentage, string DetailMessage);

public record RenderRequest(
    string InputVideoPath,
    IReadOnlyList<TimeRange> Segments,
    BlurPreset BlurPreset,
    CompressionPreset CompressionPreset,
    bool MixAudioTracks
);

public class RenderPipelineService
{
    private readonly PresetManager _presetManager;
    private readonly FfmpegCommandBuilder _commandBuilder;

    private static readonly Regex FfmpegTimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2}\.\d+)", RegexOptions.Compiled);
    private static readonly Regex VsPercentRegex = new(@"\[(\d+)%\]", RegexOptions.Compiled);

    public RenderPipelineService(PresetManager presetManager, FfmpegCommandBuilder commandBuilder)
    {
        _presetManager = presetManager;
        _commandBuilder = commandBuilder;
    }

    public static double? TryParseProgressPercent(string logLine, TimeSpan totalDuration)
    {
        if (string.IsNullOrWhiteSpace(logLine)) return null;

        // Try VapourSynth percentage: [50%]
        var vsMatch = VsPercentRegex.Match(logLine);
        if (vsMatch.Success && double.TryParse(vsMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var vsVal))
        {
            return Math.Clamp(vsVal, 0, 100);
        }

        // Try FFmpeg time: time=00:00:05.00
        var ffMatch = FfmpegTimeRegex.Match(logLine);
        if (ffMatch.Success && totalDuration > TimeSpan.Zero)
        {
            int hours = int.Parse(ffMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = int.Parse(ffMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            double seconds = double.Parse(ffMatch.Groups[3].Value, CultureInfo.InvariantCulture);

            var currentTime = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            var pct = (currentTime.TotalSeconds / totalDuration.TotalSeconds) * 100.0;
            return Math.Clamp(pct, 0, 100);
        }

        return null;
    }

    public async Task<int> ProbeAudioStreamCountAsync(string ffprobePath, string videoPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ffprobePath) || !File.Exists(ffprobePath))
        {
            return 1; // Default assumption
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return 1;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length;
        }
        catch
        {
            return 1;
        }
    }

    public async Task<string> ExecuteAsync(
        RenderRequest request,
        EnvironmentPaths env,
        IProgress<RenderProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(env.BlurCliPath) || !File.Exists(env.BlurCliPath))
        {
            throw new FileNotFoundException($"blur-cli.exe não foi encontrado em: {env.BlurCliPath}");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "VELOCE", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            // Probe audio streams
            progress?.Report(new RenderProgress("Analisando vídeo...", 2, "Inspecionando faixas de áudio"));
            int audioCount = await ProbeAudioStreamCountAsync(env.FfprobePath, request.InputVideoPath, ct);

            // Step 1: Trim and concat via FFmpeg
            var cutTempFile = Path.Combine(workDir, "cut_temp.mp4");
            progress?.Report(new RenderProgress("Cortando segmentos...", 5, "Extraindo trechos da timeline"));

            var trimArgs = _commandBuilder.BuildTrimAndConcatArgs(
                request.InputVideoPath,
                request.Segments,
                audioCount,
                request.MixAudioTracks,
                env.Gpu,
                cutTempFile
            );

            await RunProcessAsync(env.FfmpegPath, trimArgs, workDir, line =>
            {
                progress?.Report(new RenderProgress("Cortando segmentos...", 10, line));
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Step 2: Tekno Blur CLI
            var blurTempFile = Path.Combine(workDir, "blur_temp.mp4");
            var cfgTempFile = Path.Combine(workDir, "run.cfg");

            string? existingTemplate = null;
            if (!string.IsNullOrEmpty(env.BlurConfigDir))
            {
                var userCfgPath = Path.Combine(env.BlurConfigDir, ".blur-config.cfg");
                if (File.Exists(userCfgPath))
                {
                    existingTemplate = await File.ReadAllTextAsync(userCfgPath, ct);
                }
            }

            var cfgContent = _presetManager.GenerateTeknoBlurCfg(request.BlurPreset, existingTemplate);
            await File.WriteAllTextAsync(cfgTempFile, cfgContent, ct);

            progress?.Report(new RenderProgress("Aplicando Motion Blur...", 15, "Iniciando blur-cli"));
            var blurArgs = $"-i \"{cutTempFile}\" -o \"{blurTempFile}\" -c \"{cfgTempFile}\" -v";

            await RunProcessAsync(env.BlurCliPath, blurArgs, workDir, line =>
            {
                var pct = TryParseProgressPercent(line, TimeSpan.FromSeconds(30));
                double mappedPct = pct.HasValue ? 15.0 + (pct.Value * 0.65) : 40.0;
                progress?.Report(new RenderProgress("Renderizando Blur...", mappedPct, line));
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Step 3: Compression / Finalization
            var finalOutput = _commandBuilder.ResolveNonCollidingOutputPath(request.InputVideoPath);

            if (request.CompressionPreset.Enabled && request.CompressionPreset.TargetHeight > 0)
            {
                progress?.Report(new RenderProgress("Comprimindo vídeo final...", 85, "Otimizando resolução e taxa de bits"));
                var compArgs = _commandBuilder.BuildCompressionArgs(blurTempFile, request.CompressionPreset, env.Gpu, finalOutput);

                await RunProcessAsync(env.FfmpegPath, compArgs, workDir, line =>
                {
                    progress?.Report(new RenderProgress("Comprimindo vídeo final...", 92, line));
                }, ct);
            }
            else
            {
                progress?.Report(new RenderProgress("Finalizando...", 95, "Salvando arquivo original"));
                File.Move(blurTempFile, finalOutput, overwrite: true);
            }

            progress?.Report(new RenderProgress("Concluído!", 100, Path.GetFileName(finalOutput)));
            return finalOutput;
        }
        finally
        {
            // Clean up temporary workspace directory
            try
            {
                if (Directory.Exists(workDir))
                {
                    Directory.Delete(workDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static async Task RunProcessAsync(
        string executable,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputLine,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill errors
            }
        });

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"O processo '{Path.GetFileName(executable)}' falhou com código de saída {process.ExitCode}.");
        }
    }
}
