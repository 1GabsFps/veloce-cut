using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public class BootstrapperService
{
    private const string GitHubLatestReleaseUrl = "https://api.github.com/repos/f0e/blur/releases/latest";

    public static GitHubReleaseAsset? SelectBestAsset(IEnumerable<GitHubReleaseAsset> assets)
    {
        var list = assets.ToList();
        if (list.Count == 0) return null;

        // 1. Look for portable Windows zip
        var zip = list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                           (a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase)));
        if (zip != null) return zip;

        var anyZip = list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (anyZip != null) return anyZip;

        // 2. Look for Inno Setup Windows installer
        var installer = list.FirstOrDefault(a => a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                                                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (installer != null) return installer;

        // 3. Look for standalone blur-cli exe
        var cli = list.FirstOrDefault(a => a.Name.Contains("blur-cli", StringComparison.OrdinalIgnoreCase) &&
                                           a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (cli != null) return cli;

        // 4. Any Windows exe
        return list.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<List<GitHubReleaseAsset>> FetchLatestReleaseAssetsAsync(HttpClient client, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubLatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VELOCE-App", "1.0"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var assets = new List<GitHubReleaseAsset>();
        if (doc.RootElement.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in assetsElem.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString() ?? string.Empty;
                string url = item.GetProperty("browser_download_url").GetString() ?? string.Empty;
                long size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                {
                    assets.Add(new GitHubReleaseAsset(name, url, size));
                }
            }
        }

        return assets;
    }

    public static async Task DownloadFileWithProgressAsync(
        HttpClient client,
        string downloadUrl,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VELOCE-App", "1.0"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                double pct = (double)totalBytesRead / totalBytes * 100.0;
                progress.Report(Math.Min(100.0, pct));
            }
        }

        progress?.Report(100.0);
    }

    public static async Task ExtractOrInstallAssetAsync(string downloadedFilePath, string targetDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var ext = Path.GetExtension(downloadedFilePath).ToLowerInvariant();

        if (ext == ".zip")
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(downloadedFilePath, targetDirectory, overwriteFiles: true), ct);
        }
        else if (ext == ".exe")
        {
            var fileName = Path.GetFileName(downloadedFilePath);
            if (fileName.Contains("Installer", StringComparison.OrdinalIgnoreCase))
            {
                // Inno Setup silent install to target directory
                var psi = new ProcessStartInfo
                {
                    FileName = downloadedFilePath,
                    Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{targetDirectory}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador do Tekno Blur.");
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Instalação do Tekno Blur falhou com código de saída {proc.ExitCode}.");
                }
            }
            else
            {
                // Standalone blur-cli.exe
                var targetCli = Path.Combine(targetDirectory, "blur-cli.exe");
                File.Copy(downloadedFilePath, targetCli, overwrite: true);
            }
        }
    }

    public static async Task<EnvironmentPaths> AutoBootstrapAsync(
        IProgress<double>? progress = null,
        IProgress<string>? statusText = null,
        CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        statusText?.Report("Consultando versão mais recente do Tekno Blur...");
        progress?.Report(5.0);

        var assets = await FetchLatestReleaseAssetsAsync(client, ct);
        var selectedAsset = SelectBestAsset(assets)
            ?? throw new InvalidOperationException("Nenhum executável ou pacote compatível encontrado no repositório do Tekno Blur.");

        statusText?.Report($"Baixando {selectedAsset.Name}...");

        var tempFile = Path.Combine(Path.GetTempPath(), $"Veloce_Download_{Guid.NewGuid():N}_{selectedAsset.Name}");
        try
        {
            var downloadProgress = new Progress<double>(pct =>
            {
                // Download corresponds to 10% to 80% of total progress
                double overall = 10.0 + (pct * 0.70);
                progress?.Report(overall);
                statusText?.Report($"Baixando motor de Motion Blur ({pct:F0}%)...");
            });

            await DownloadFileWithProgressAsync(client, selectedAsset.DownloadUrl, tempFile, downloadProgress, ct);

            statusText?.Report("Instalando e configurando motor local...");
            progress?.Report(85.0);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var targetDir = Path.Combine(localAppData, "VELOCE", "engine");

            await ExtractOrInstallAssetAsync(tempFile, targetDir, ct);
            progress?.Report(95.0);

            // Locate files in targetDir or subdirectories
            var detector = new EnvironmentDetectorService();
            var detected = detector.Detect();

            var settings = SettingsManager.Load();
            if (File.Exists(detected.BlurCliPath))
            {
                settings.BlurCliPath = detected.BlurCliPath;
            }
            if (File.Exists(detected.FfmpegPath))
            {
                settings.FfmpegPath = detected.FfmpegPath;
            }
            if (File.Exists(detected.FfprobePath))
            {
                settings.FfprobePath = detected.FfprobePath;
            }

            SettingsManager.Save(null, settings);

            progress?.Report(100.0);
            statusText?.Report("Motor de Motion Blur configurado com sucesso!");

            return detected;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch
            {
                // Ignore temp cleanup error
            }
        }
    }
}
