using System;
using System.Collections.Generic;
using System.IO;
using Veloce.Core.Models;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class FfmpegCommandBuilderTests
{
    [Fact]
    public void BuildTrimAndConcatArgs_WithTwoSegmentsAndTwoAudioStreams_GeneratesFilterComplexWithAmix()
    {
        var builder = new FfmpegCommandBuilder();
        var segments = new List<TimeRange>
        {
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
            new(TimeSpan.FromSeconds(35.5), TimeSpan.FromSeconds(45))
        };

        string args = builder.BuildTrimAndConcatArgs(
            @"D:\clips\raw.mp4",
            segments,
            audioStreamCount: 2,
            mixAudioTracks: true,
            GpuVendor.Nvidia,
            @"C:\Temp\cut.mp4");

        Assert.Contains("amix=inputs=2:duration=longest", args);
        Assert.Contains("-ss 10.000000 -to 20.000000", args);
        Assert.Contains("-ss 35.500000 -to 45.000000", args);
        Assert.Contains("concat=n=2:v=1:a=1", args);
        Assert.Contains("h264_nvenc", args);
    }

    [Fact]
    public void BuildTrimAndConcatArgs_WithThreeSegmentsMiddleDeleted_ConcatenatesAllThree()
    {
        var builder = new FfmpegCommandBuilder();
        var segments = new List<TimeRange>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10)),
            new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
            new(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(60))
        };

        string args = builder.BuildTrimAndConcatArgs(
            @"D:\clips\gameplay.mp4",
            segments,
            audioStreamCount: 1,
            mixAudioTracks: true,
            GpuVendor.Nvidia,
            @"C:\Temp\cut.mp4");

        Assert.Contains("-ss 0.000000 -to 10.000000", args);
        Assert.Contains("-ss 20.000000 -to 30.000000", args);
        Assert.Contains("-ss 45.000000 -to 60.000000", args);
        Assert.Contains("concat=n=3:v=1:a=1", args);
        Assert.Contains("[0:v:0][0:a:0][1:v:0][1:a:0][2:v:0][2:a:0]", args);
    }

    [Fact]
    public void BuildTrimAndConcatArgs_WithSingleSegment_GeneratesFastInputSeekWithoutConcat()
    {
        var builder = new FfmpegCommandBuilder();
        var segments = new List<TimeRange> { new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)) };

        string args = builder.BuildTrimAndConcatArgs(
            @"D:\clips\silent.mp4",
            segments,
            audioStreamCount: 0,
            mixAudioTracks: true,
            GpuVendor.Cpu,
            @"C:\Temp\cut.mp4");

        Assert.Contains("-ss 5.000000 -to 15.000000 -i", args);
        Assert.DoesNotContain("concat", args);
        Assert.Contains("-map 0:v:0", args);
        Assert.Contains("libx264", args);
    }

    [Theory]
    [InlineData(GpuVendor.Nvidia, "h264_nvenc")]
    [InlineData(GpuVendor.Amd, "h264_amf")]
    [InlineData(GpuVendor.Intel, "h264_qsv")]
    [InlineData(GpuVendor.Cpu, "libx264")]
    public void BuildCompressionArgs_SelectsProperHardwareEncoderAndBt709Color(GpuVendor vendor, string expectedEncoder)
    {
        var builder = new FfmpegCommandBuilder();
        var preset = new CompressionPreset
        {
            Id = "1080p60",
            Name = "1080p60",
            Enabled = true,
            TargetHeight = 1080,
            TargetFps = 60,
            Cq = 22
        };

        string args = builder.BuildCompressionArgs(@"C:\Temp\blur.mp4", preset, vendor, @"D:\out.mp4");

        Assert.Contains(expectedEncoder, args);
        Assert.Contains("scale=-2:1080", args);
        Assert.Contains("-colorspace bt709", args);
        Assert.Contains("-r 60", args);
    }

    [Fact]
    public void ResolveNonCollidingOutputPath_AppendsIncrementWhenFileExists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"qb_out_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string input = Path.Combine(tempDir, "clip.mp4");
            string first = Path.Combine(tempDir, "clip_blur.mp4");
            File.WriteAllText(first, "dummy");

            var builder = new FfmpegCommandBuilder();
            string resolved = builder.ResolveNonCollidingOutputPath(input);

            Assert.Equal(Path.Combine(tempDir, "clip_blur_1.mp4"), resolved);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
