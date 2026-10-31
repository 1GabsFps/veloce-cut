using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public class TimelineManager
{
    private readonly List<TimeRange> _segments = new();
    private readonly Stack<List<TimeRange>> _undoStack = new();

    public IReadOnlyList<TimeRange> Segments => _segments;
    public TimeSpan MarkerA { get; private set; } = TimeSpan.Zero;
    public TimeSpan MarkerB { get; private set; } = TimeSpan.Zero;
    public TimeSpan TotalOriginalDuration { get; private set; } = TimeSpan.Zero;
    public TimeSpan RemainingDuration => TimeSpan.FromTicks(_segments.Sum(s => s.Duration.Ticks));
    public bool CanUndo => _undoStack.Count > 0;

    public void LoadVideo(TimeSpan totalDuration)
    {
        TotalOriginalDuration = totalDuration;
        _segments.Clear();
        _undoStack.Clear();

        if (totalDuration > TimeSpan.Zero)
        {
            _segments.Add(new TimeRange(TimeSpan.Zero, totalDuration));
            MarkerA = TimeSpan.Zero;
            MarkerB = totalDuration;
        }
        else
        {
            MarkerA = TimeSpan.Zero;
            MarkerB = TimeSpan.Zero;
        }
    }

    public void SetMarkerA(TimeSpan virtualTime)
    {
        var clamped = Clamp(virtualTime, TimeSpan.Zero, RemainingDuration);
        MarkerA = clamped;

        if (MarkerA > MarkerB)
        {
            MarkerB = RemainingDuration;
        }
    }

    public void SetMarkerB(TimeSpan virtualTime)
    {
        var clamped = Clamp(virtualTime, TimeSpan.Zero, RemainingDuration);
        MarkerB = clamped;

        if (MarkerB < MarkerA)
        {
            MarkerA = TimeSpan.Zero;
        }
    }

    public void ClearSelection()
    {
        MarkerA = TimeSpan.Zero;
        MarkerB = RemainingDuration;
    }

    public bool DeleteSelection()
    {
        if (_segments.Count == 0) return false;

        var selStart = MarkerA < MarkerB ? MarkerA : MarkerB;
        var selEnd = MarkerA > MarkerB ? MarkerA : MarkerB;

        if (selEnd <= selStart) return false;

        // Reject deleting the entire timeline
        if (selStart == TimeSpan.Zero && selEnd >= RemainingDuration)
        {
            return false;
        }

        // Save snapshot for undo
        _undoStack.Push(new List<TimeRange>(_segments));

        var newSegments = new List<TimeRange>();
        var curVirtualStart = TimeSpan.Zero;

        foreach (var seg in _segments)
        {
            var curVirtualEnd = curVirtualStart + seg.Duration;

            // Check if selection overlaps this segment's virtual span
            if (selEnd <= curVirtualStart || selStart >= curVirtualEnd)
            {
                // No overlap, retain segment completely
                newSegments.Add(seg);
            }
            else
            {
                // Prefix: portion before selStart
                if (selStart > curVirtualStart)
                {
                    var prefixDuration = selStart - curVirtualStart;
                    newSegments.Add(new TimeRange(seg.Start, seg.Start + prefixDuration));
                }

                // Suffix: portion after selEnd
                if (selEnd < curVirtualEnd)
                {
                    var suffixOffset = selEnd - curVirtualStart;
                    newSegments.Add(new TimeRange(seg.Start + suffixOffset, seg.End));
                }
            }

            curVirtualStart = curVirtualEnd;
        }

        _segments.Clear();
        _segments.AddRange(newSegments.Where(s => s.Duration > TimeSpan.FromMilliseconds(5)));

        MarkerA = TimeSpan.Zero;
        MarkerB = RemainingDuration;
        return true;
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;

        _segments.Clear();
        _segments.AddRange(_undoStack.Pop());

        MarkerA = TimeSpan.Zero;
        MarkerB = RemainingDuration;
        return true;
    }

    public TimeSpan VirtualToSourceTime(TimeSpan virtualTime)
    {
        var clamped = Clamp(virtualTime, TimeSpan.Zero, RemainingDuration);
        var curVirtualStart = TimeSpan.Zero;

        foreach (var seg in _segments)
        {
            var curVirtualEnd = curVirtualStart + seg.Duration;
            if (clamped <= curVirtualEnd)
            {
                var offsetInSeg = clamped - curVirtualStart;
                return seg.Start + offsetInSeg;
            }
            curVirtualStart = curVirtualEnd;
        }

        return _segments.Count > 0 ? _segments.Last().End : TimeSpan.Zero;
    }

    public string BuildMpvEdlUri(string videoFilePath)
    {
        if (_segments.Count == 0) return videoFilePath;

        var sb = new StringBuilder();
        sb.Append("edl://!no_chapters;");

        int byteCount = Encoding.UTF8.GetByteCount(videoFilePath);

        for (int i = 0; i < _segments.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var seg = _segments[i];
            var startSec = seg.Start.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
            var lenSec = seg.Duration.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
            sb.Append($"%{byteCount}%{videoFilePath},start={startSec},length={lenSec}");
        }

        return sb.ToString();
    }

    private static TimeSpan Clamp(TimeSpan val, TimeSpan min, TimeSpan max)
    {
        if (val < min) return min;
        if (val > max) return max;
        return val;
    }
}
