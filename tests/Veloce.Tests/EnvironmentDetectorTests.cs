using System;
using Veloce.Core.Models;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class EnvironmentDetectorTests
{
    [Fact]
    public void Detect_FindsInstalledBlurAndDetectsNvidiaGpu()
    {
        var detector = new EnvironmentDetectorService();
        var paths = detector.Detect();

        Assert.False(string.IsNullOrEmpty(paths.BlurCliPath), "blur-cli.exe should be located.");
        Assert.False(string.IsNullOrEmpty(paths.FfmpegPath), "ffmpeg.exe should be located.");
        Assert.Equal(GpuVendor.Nvidia, paths.Gpu);
    }

    [Theory]
    [InlineData("28.248813", "60/1", 28.248, 60.0)]
    [InlineData("12.500000", "240/1", 12.5, 240.0)]
    [InlineData("10.0", "59.94", 10.0, 59.94)]
    public void ParseFfprobeOutput_ExtractsDurationAndFpsCorrectly(string durStr, string fpsStr, double expectedSec, double expectedFps)
    {
        var (duration, fps) = EnvironmentDetectorService.ParseFfprobeOutput(durStr, fpsStr);

        Assert.Equal(expectedSec, duration.TotalSeconds, 2);
        Assert.Equal(expectedFps, fps, 1);
    }
}
