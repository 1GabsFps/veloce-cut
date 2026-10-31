namespace Veloce.Core.Models;

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Cpu
}

public record EnvironmentPaths(
    string BlurCliPath,
    string FfmpegPath,
    string FfprobePath,
    string? BlurConfigDir,
    GpuVendor Gpu
);
