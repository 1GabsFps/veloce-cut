using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Veloce.App.Interop;

public interface IVideoPlayerService : IDisposable
{
    event Action<TimeSpan>? PositionChanged;
    event Action<TimeSpan>? MediaOpened;

    TimeSpan CurrentPosition { get; }
    TimeSpan Duration { get; }
    bool IsPlaying { get; }
    double VideoFps { get; }

    void Initialize(MediaElement mediaElement, IntPtr hwndHost);
    void Open(string filePath, double fps = 60.0);
    void Play();
    void Pause();
    void PlayPause();
    void Seek(TimeSpan position, bool exact = true);
    void StepFrame(int delta);
    void StepKeyframe(int delta);
    void SeekRelative(double seconds);
    TimeSpan QueryExactPosition();
    void ApplyEdl(string edlUri);
}

public class MpvVideoPlayerService : IVideoPlayerService
{
    private MediaElement? _mediaElement;
    private IntPtr _mpvHandle = IntPtr.Zero;
    private DispatcherTimer? _pollTimer;
    private bool _isMpvActive = false;

    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? MediaOpened;

    public TimeSpan CurrentPosition { get; private set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;
    public bool IsPlaying { get; private set; } = false;
    public double VideoFps { get; private set; } = 60.0;

    public void Initialize(MediaElement mediaElement, IntPtr hwndHost)
    {
        _mediaElement = mediaElement;

        if (LibMpvInterop.IsMpvAvailable() && hwndHost != IntPtr.Zero)
        {
            try
            {
                _mpvHandle = LibMpvInterop.mpv_create();
                if (_mpvHandle != IntPtr.Zero)
                {
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "wid", hwndHost.ToInt64().ToString(CultureInfo.InvariantCulture));
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "vo", "gpu");
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "gpu-api", "d3d11");
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "hwdec", "auto");
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "keep-open", "yes");
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "hr-seek", "always");
                    LibMpvInterop.mpv_set_option_string(_mpvHandle, "hr-seek-framedrop", "no");
                    LibMpvInterop.mpv_initialize(_mpvHandle);
                    _isMpvActive = true;
                }
            }
            catch
            {
                _isMpvActive = false;
            }
        }

        // Setup polling timer for position synchronization
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS sync
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // Wire media element fallback
        if (!_isMpvActive && _mediaElement != null)
        {
            _mediaElement.LoadedBehavior = MediaState.Manual;
            _mediaElement.UnloadedBehavior = MediaState.Manual;
            _mediaElement.MediaOpened += (_, _) =>
            {
                if (_mediaElement.NaturalDuration.HasTimeSpan)
                {
                    Duration = _mediaElement.NaturalDuration.TimeSpan;
                    MediaOpened?.Invoke(Duration);
                }
            };
        }
    }

    public void Open(string filePath, double fps = 60.0)
    {
        VideoFps = fps > 0 ? fps : 60.0;
        Duration = TimeSpan.Zero;
        CurrentPosition = TimeSpan.Zero;

        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                LibMpvInterop.MpvCommand(_mpvHandle, "loadfile", filePath);
                LibMpvInterop.mpv_set_property_string(_mpvHandle, "pause", "yes");
                IsPlaying = false;
            }
            catch
            {
                // Fallback
            }
        }
        else if (_mediaElement != null)
        {
            try
            {
                _mediaElement.Source = new Uri(filePath, UriKind.Absolute);
                _mediaElement.Play();
                _mediaElement.Pause();
                IsPlaying = false;
            }
            catch
            {
                // Fallback
            }
        }
    }

    public void Play()
    {
        IsPlaying = true;
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                LibMpvInterop.mpv_set_property_string(_mpvHandle, "pause", "no");
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            _mediaElement?.Play();
        }
    }

    public void Pause()
    {
        IsPlaying = false;
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                LibMpvInterop.mpv_set_property_string(_mpvHandle, "pause", "yes");
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            _mediaElement?.Pause();
        }
    }

    public void PlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(TimeSpan position, bool exact = true)
    {
        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        CurrentPosition = clamped;

        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                var secStr = clamped.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture);
                LibMpvInterop.MpvCommand(_mpvHandle, "seek", secStr, "absolute+exact");
            }
            catch
            {
                // Fallback
            }
        }
        else if (_mediaElement != null)
        {
            try
            {
                _mediaElement.Position = clamped;
            }
            catch
            {
                // Fallback
            }
        }

        PositionChanged?.Invoke(CurrentPosition);
    }

    public void StepKeyframe(int delta)
    {
        if (IsPlaying) Pause();

        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                string sec = delta > 0 ? "1" : "-1";
                LibMpvInterop.MpvCommand(_mpvHandle, "seek", sec, "relative+keyframes");
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            Seek(CurrentPosition + TimeSpan.FromSeconds(delta * 2));
        }
    }

    public void SeekRelative(double seconds)
    {
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                var secStr = seconds.ToString("F6", CultureInfo.InvariantCulture);
                LibMpvInterop.MpvCommand(_mpvHandle, "seek", secStr, "relative+exact");
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            Seek(CurrentPosition + TimeSpan.FromSeconds(seconds));
        }
    }

    public TimeSpan QueryExactPosition()
    {
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                var posPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, "time-pos");
                if (posPtr != IntPtr.Zero)
                {
                    var str = Marshal.PtrToStringUTF8(posPtr);
                    LibMpvInterop.mpv_free(posPtr);
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    {
                        CurrentPosition = TimeSpan.FromSeconds(sec);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }
        return CurrentPosition;
    }

    public void StepFrame(int delta)
    {
        if (IsPlaying) Pause();

        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                string cmd = delta > 0 ? "frame-step" : "frame-back-step";
                LibMpvInterop.MpvCommand(_mpvHandle, cmd);
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            double frameTimeMs = 1000.0 / (VideoFps > 0 ? VideoFps : 60.0);
            Seek(CurrentPosition + TimeSpan.FromMilliseconds(delta * frameTimeMs));
        }
    }

    public void ApplyEdl(string edlUri)
    {
        Duration = TimeSpan.Zero;
        CurrentPosition = TimeSpan.Zero;

        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                LibMpvInterop.MpvCommand(_mpvHandle, "loadfile", edlUri);
                LibMpvInterop.mpv_set_property_string(_mpvHandle, "pause", "yes");
                IsPlaying = false;
            }
            catch
            {
                // Fallback
            }
        }
        else if (_mediaElement != null)
        {
            Seek(TimeSpan.Zero);
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                var posPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, "time-pos");
                if (posPtr != IntPtr.Zero)
                {
                    var str = Marshal.PtrToStringUTF8(posPtr);
                    LibMpvInterop.mpv_free(posPtr);
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    {
                        CurrentPosition = TimeSpan.FromSeconds(sec);
                        PositionChanged?.Invoke(CurrentPosition);
                    }
                }

                var durPtr = LibMpvInterop.mpv_get_property_string(_mpvHandle, "duration");
                if (durPtr != IntPtr.Zero)
                {
                    var str = Marshal.PtrToStringUTF8(durPtr);
                    LibMpvInterop.mpv_free(durPtr);
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
                    {
                        var dur = TimeSpan.FromSeconds(sec);
                        if (dur != Duration)
                        {
                            Duration = dur;
                            MediaOpened?.Invoke(Duration);
                        }
                    }
                }
            }
            catch
            {
                // Ignore polling errors
            }
        }
        else if (_mediaElement != null && IsPlaying)
        {
            try
            {
                CurrentPosition = _mediaElement.Position;
                PositionChanged?.Invoke(CurrentPosition);
            }
            catch
            {
                // Ignore
            }
        }
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        if (_isMpvActive && _mpvHandle != IntPtr.Zero)
        {
            try
            {
                LibMpvInterop.mpv_terminate_destroy(_mpvHandle);
            }
            catch
            {
                // Ignore cleanup errors
            }
            _mpvHandle = IntPtr.Zero;
        }
    }
}
