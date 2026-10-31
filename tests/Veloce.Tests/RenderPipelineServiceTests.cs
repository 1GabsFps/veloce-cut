using System;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class RenderPipelineServiceTests
{
    [Theory]
    [InlineData("frame=  300 fps= 60 q=14.0 size=   10240kB time=00:00:05.00 bitrate=16777.2kbits/s", 10, 50.0)]
    [InlineData(" [50%] 600/1200 frames", 10, 50.0)]
    public void TryParseProgressPercent_ExtractsFfmpegTimeAndVapourSynthPercentage(string line, double durationSec, double expected)
    {
        double? pct = RenderPipelineService.TryParseProgressPercent(line, TimeSpan.FromSeconds(durationSec));
        Assert.NotNull(pct);
        Assert.Equal(expected, pct!.Value, 1);
    }
}
