using System;
using System.Collections.Generic;
using System.Globalization;
using Veloce.Core.Models;
using Veloce.Core.Services;
using Xunit;

namespace Veloce.Tests;

public class TimelineManagerTests
{
    [Fact]
    public void LoadVideo_SetsDefaultMarkerA_AtZero_AndMarkerB_AtEnd()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.Zero, tm.MarkerA);
        Assert.Equal(TimeSpan.FromSeconds(60), tm.MarkerB);
        Assert.Equal(TimeSpan.FromSeconds(60), tm.RemainingDuration);
        Assert.Single(tm.Segments);
    }

    [Fact]
    public void SetMarkerB_Only_SelectsFromStart_AndDeleteRemovesIntro()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(60));

        tm.SetMarkerB(TimeSpan.FromSeconds(15));
        bool deleted = tm.DeleteSelection();

        Assert.True(deleted);
        Assert.Equal(TimeSpan.FromSeconds(45), tm.RemainingDuration);
        Assert.Single(tm.Segments);
        Assert.Equal(TimeSpan.FromSeconds(15), tm.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(60), tm.Segments[0].End);
        Assert.Equal(TimeSpan.Zero, tm.MarkerA);
        Assert.Equal(TimeSpan.FromSeconds(45), tm.MarkerB);
    }

    [Fact]
    public void SetMarkerA_Only_SelectsToEnd_AndDeleteRemovesOutro()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(60));

        tm.SetMarkerA(TimeSpan.FromSeconds(40));
        bool deleted = tm.DeleteSelection();

        Assert.True(deleted);
        Assert.Equal(TimeSpan.FromSeconds(40), tm.RemainingDuration);
        Assert.Single(tm.Segments);
        Assert.Equal(TimeSpan.Zero, tm.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(40), tm.Segments[0].End);
    }

    [Fact]
    public void SetMarkers_InReverseOrder_PreservesBothMarkersAndDeletesBetweenThem()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(60));

        // Set A at 40s first, then B at 20s (B before A)
        tm.SetMarkerA(TimeSpan.FromSeconds(40));
        tm.SetMarkerB(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(40), tm.MarkerA);
        Assert.Equal(TimeSpan.FromSeconds(20), tm.MarkerB);

        bool deleted = tm.DeleteSelection();
        Assert.True(deleted);

        // Remainder should be [0s, 20s] and [40s, 60s] -> Total 40s
        Assert.Equal(TimeSpan.FromSeconds(40), tm.RemainingDuration);
        Assert.Equal(2, tm.Segments.Count);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(20)), tm.Segments[0]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)), tm.Segments[1]);
    }

    [Fact]
    public void DeleteAcrossExistingSplice_TrimsBothNeighborSegments_AndUndoRestores()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(60));

        // First cut: remove [20s, 30s] -> leaves [0, 20] and [30, 60] (virtual length 50s, splice at virtual 20s)
        tm.SetMarkerA(TimeSpan.FromSeconds(20));
        tm.SetMarkerB(TimeSpan.FromSeconds(30));
        Assert.True(tm.DeleteSelection());
        Assert.Equal(2, tm.Segments.Count);

        // Second cut across splice: virtual [15s, 25s] -> leaves [0, 15] and [35, 60] (virtual length 40s)
        tm.SetMarkerA(TimeSpan.FromSeconds(15));
        tm.SetMarkerB(TimeSpan.FromSeconds(25));
        Assert.True(tm.DeleteSelection());

        Assert.Equal(TimeSpan.FromSeconds(40), tm.RemainingDuration);
        Assert.Equal(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(15)), tm.Segments[0]);
        Assert.Equal(new TimeRange(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(60)), tm.Segments[1]);

        Assert.True(tm.Undo());
        Assert.Equal(TimeSpan.FromSeconds(50), tm.RemainingDuration);
    }

    [Fact]
    public void DeleteEntireTimeline_IsRejected()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(30));

        bool deleted = tm.DeleteSelection();
        Assert.False(deleted);
        Assert.Equal(TimeSpan.FromSeconds(30), tm.RemainingDuration);
    }

    [Fact]
    public void ClearSelection_ResetsMarkersToFullTimeline()
    {
        var tm = new TimelineManager();
        tm.LoadVideo(TimeSpan.FromSeconds(40));

        tm.SetMarkerA(TimeSpan.FromSeconds(10));
        tm.SetMarkerB(TimeSpan.FromSeconds(25));
        Assert.Equal(TimeSpan.FromSeconds(10), tm.MarkerA);
        Assert.Equal(TimeSpan.FromSeconds(25), tm.MarkerB);

        tm.ClearSelection();
        Assert.Equal(TimeSpan.Zero, tm.MarkerA);
        Assert.Equal(TimeSpan.FromSeconds(40), tm.MarkerB);
    }

    [Fact]
    public void BuildMpvEdlUri_UsesUtf8ByteCountPrefix_WithAccentedFoldersUnderPtBr()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var tm = new TimelineManager();
            tm.LoadVideo(TimeSpan.FromSeconds(178.3));
            tm.SetMarkerB(TimeSpan.FromSeconds(95.62));
            bool deleted = tm.DeleteSelection();

            Assert.True(deleted);
            Assert.Single(tm.Segments);
            Assert.Equal(TimeSpan.FromSeconds(95.62), tm.Segments[0].Start);
            Assert.Equal(TimeSpan.FromSeconds(178.3), tm.Segments[0].End);
            Assert.Equal(TimeSpan.FromSeconds(82.68), tm.RemainingDuration);

            string path = @"C:\Users\gbr\Vídeos\Valorant 2026.10.21 - 00.53.40.02.DVR.mp4";
            int expectedByteCount = System.Text.Encoding.UTF8.GetByteCount(path);
            // Verify UTF-8 byte count is greater than string char count due to 'í'
            Assert.True(expectedByteCount > path.Length);

            string edl = tm.BuildMpvEdlUri(path);
            Assert.StartsWith($"edl://!no_chapters;%{expectedByteCount}%{path},start=95.620000,length=82.680000", edl);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void BuildMpvEdlUri_UsesLengthPrefixAndInvariantCultureUnderPtBr()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var tm = new TimelineManager();
            tm.LoadVideo(TimeSpan.FromSeconds(30));
            tm.SetMarkerB(TimeSpan.FromSeconds(5.5));
            tm.DeleteSelection();

            string path = @"D:\videos\clip, test;1.mp4";
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(path);
            string edl = tm.BuildMpvEdlUri(path);

            Assert.StartsWith($"edl://!no_chapters;%{byteCount}%{path},start=5.500000,length=24.500000", edl);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
