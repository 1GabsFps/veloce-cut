using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Veloce.Core.Models;

namespace Veloce.Core.Services;

public class FfmpegCommandBuilder
{
    public string BuildTrimAndConcatArgs(
        string inputVideoPath,
        IReadOnlyList<TimeRange> segments,
        int audioStreamCount,
        bool mixAudioTracks,
        GpuVendor gpu,
        string outputTempFile)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("Segments list cannot be empty.", nameof(segments));
        }

        var sb = new StringBuilder();
        sb.Append("-y ");

        bool hasAudio = audioStreamCount > 0;
        int activeAudioTracks = (hasAudio && mixAudioTracks && audioStreamCount >= 2) ? audioStreamCount : (hasAudio ? 1 : 0);

        if (segments.Count == 1)
        {
            var seg = segments[0];
            var start = seg.Start.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
            var to = seg.End.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);

            sb.Append($"-ss {start} -to {to} -i \"{inputVideoPath}\" ");

            if (activeAudioTracks > 1)
            {
                var mixInputs = new StringBuilder();
                for (int a = 0; a < activeAudioTracks; a++)
                {
                    mixInputs.Append($"[0:a:{a}]");
                }
                sb.Append($"-filter_complex \"{mixInputs}amix=inputs={activeAudioTracks}:duration=longest[outa]\" ");
                sb.Append("-map 0:v:0 -map \"[outa]\" ");
            }
            else
            {
                sb.Append("-map 0:v:0 ");
                if (hasAudio)
                {
                    sb.Append("-map 0:a:0 ");
                }
            }

            sb.Append(GetFastEncoderArgs(gpu));
            if (hasAudio)
            {
                sb.Append("-c:a aac -b:a 192k ");
            }

            sb.Append($"\"{outputTempFile}\"");
            return sb.ToString().Trim();
        }

        // Multi-segment concat using fast input seeking per segment
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var start = seg.Start.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
            var to = seg.End.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
            sb.Append($"-ss {start} -to {to} -i \"{inputVideoPath}\" ");
        }

        var filterComplex = new StringBuilder();
        var concatInputs = new StringBuilder();

        for (int i = 0; i < segments.Count; i++)
        {
            concatInputs.Append($"[{i}:v:0]");
            if (activeAudioTracks > 1)
            {
                var mixInputs = new StringBuilder();
                for (int a = 0; a < activeAudioTracks; a++)
                {
                    mixInputs.Append($"[{i}:a:{a}]");
                }
                filterComplex.Append($"{mixInputs}amix=inputs={activeAudioTracks}:duration=longest[a{i}];");
                concatInputs.Append($"[a{i}]");
            }
            else if (activeAudioTracks == 1)
            {
                concatInputs.Append($"[{i}:a:0]");
            }
        }

        int audioOut = hasAudio ? 1 : 0;
        filterComplex.Append($"{concatInputs}concat=n={segments.Count}:v=1:a={audioOut}[outv]");
        if (hasAudio)
        {
            filterComplex.Append("[outa]");
        }

        sb.Append($"-filter_complex \"{filterComplex}\" ");
        sb.Append("-map \"[outv]\" ");
        if (hasAudio)
        {
            sb.Append("-map \"[outa]\" ");
        }

        sb.Append(GetFastEncoderArgs(gpu));
        if (hasAudio)
        {
            sb.Append("-c:a aac -b:a 192k ");
        }

        sb.Append($"\"{outputTempFile}\"");
        return sb.ToString().Trim();
    }

    public string BuildCompressionArgs(
        string inputBlurredPath,
        CompressionPreset preset,
        GpuVendor gpu,
        string outputFinalFile)
    {
        var sb = new StringBuilder();
        sb.Append("-y ");
        sb.Append($"-i \"{inputBlurredPath}\" ");

        var vfList = new List<string>();
        if (preset.TargetHeight > 0)
        {
            vfList.Add($"scale=-2:{preset.TargetHeight}");
        }

        if (vfList.Count > 0)
        {
            sb.Append($"-vf \"{string.Join(",", vfList)}\" ");
        }

        if (preset.TargetFps > 0)
        {
            sb.Append($"-r {preset.TargetFps} ");
        }

        sb.Append(GetCompressionEncoderArgs(gpu, preset.Cq > 0 ? preset.Cq : 22));
        sb.Append("-pix_fmt yuv420p -colorspace bt709 -color_primaries bt709 -color_trc bt709 ");
        sb.Append("-c:a aac -b:a 192k ");
        sb.Append($"\"{outputFinalFile}\"");

        return sb.ToString().Trim();
    }

    public string ResolveNonCollidingOutputPath(string inputVideoPath, string suffix = "_blur")
    {
        var dir = Path.GetDirectoryName(inputVideoPath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputVideoPath);
        var ext = Path.GetExtension(inputVideoPath);
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";

        var baseCandidate = Path.Combine(dir, $"{nameWithoutExt}{suffix}{ext}");
        if (!File.Exists(baseCandidate))
        {
            return baseCandidate;
        }

        int counter = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{nameWithoutExt}{suffix}_{counter}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }

    private static string GetFastEncoderArgs(GpuVendor gpu) => gpu switch
    {
        GpuVendor.Nvidia => "-c:v h264_nvenc -preset p1 -cq 10 ",
        GpuVendor.Amd => "-c:v h264_amf -rc cqp -qp_i 10 -qp_p 10 -qp_b 10 ",
        GpuVendor.Intel => "-c:v h264_qsv -global_quality 10 -preset veryfast ",
        _ => "-c:v libx264 -preset ultrafast -crf 10 "
    };

    private static string GetCompressionEncoderArgs(GpuVendor gpu, int cq) => gpu switch
    {
        GpuVendor.Nvidia => $"-c:v h264_nvenc -preset p5 -rc vbr -cq {cq} ",
        GpuVendor.Amd => $"-c:v h264_amf -rc cqp -qp_i {cq} -qp_p {cq} -qp_b {cq} ",
        GpuVendor.Intel => $"-c:v h264_qsv -global_quality {cq} -preset veryfast ",
        _ => $"-c:v libx264 -preset veryfast -crf {cq} "
    };
}
