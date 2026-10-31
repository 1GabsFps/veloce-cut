using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Veloce.Core.Models;
using Veloce.Core.Services;

namespace Veloce.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly TimelineManager _timelineManager;
    private readonly PresetManager _presetManager;
    private readonly EnvironmentDetectorService _envDetector;
    private readonly RenderPipelineService _pipelineService;
    private EnvironmentPaths _currentEnv;

    private CancellationTokenSource? _renderCts;
    private string _presetsFilePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimelineManager TimelineManager => _timelineManager;
    public EnvironmentDetectorService EnvDetector => _envDetector;
    public EnvironmentPaths CurrentEnv => _currentEnv;

    public bool IsEngineMissing => !IsBootstrapping && (string.IsNullOrEmpty(_currentEnv?.BlurCliPath) || !File.Exists(_currentEnv.BlurCliPath));

    private bool _isBootstrapping = false;
    public bool IsBootstrapping
    {
        get => _isBootstrapping;
        set
        {
            _isBootstrapping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEngineMissing));
            OnPropertyChanged(nameof(CanEditTimeline));
        }
    }

    private double _bootstrapProgress = 0;
    public double BootstrapProgress
    {
        get => _bootstrapProgress;
        set { _bootstrapProgress = value; OnPropertyChanged(); }
    }

    private string _bootstrapStatus = string.Empty;
    public string BootstrapStatus
    {
        get => _bootstrapStatus;
        set { _bootstrapStatus = value; OnPropertyChanged(); }
    }

    public ObservableCollection<BlurPreset> BlurPresets { get; } = new();
    public ObservableCollection<CompressionPreset> CompressionPresets { get; } = new();

    private BlurPreset? _selectedBlurPreset;
    public BlurPreset? SelectedBlurPreset
    {
        get => _selectedBlurPreset;
        set
        {
            _selectedBlurPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPresetSummary));
        }
    }

    public string SelectedPresetSummary => SelectedBlurPreset != null
        ? $"{SelectedBlurPreset.OutputFps} FPS • {SelectedBlurPreset.BlurWeighting} • Blur {SelectedBlurPreset.BlurAmount:0.00} • Interp {SelectedBlurPreset.InterpolatedFps} • CQ {SelectedBlurPreset.Quality}"
        : "Nenhum preset selecionado";

    private CompressionPreset? _selectedCompressionPreset;
    public CompressionPreset? SelectedCompressionPreset
    {
        get => _selectedCompressionPreset;
        set { _selectedCompressionPreset = value; OnPropertyChanged(); }
    }

    private string _loadedVideoPath = string.Empty;
    public string LoadedVideoPath
    {
        get => _loadedVideoPath;
        set
        {
            _loadedVideoPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVideoLoaded));
            OnPropertyChanged(nameof(VideoTitle));
            OnPropertyChanged(nameof(CanEditTimeline));
        }
    }

    public bool HasVideoLoaded => !string.IsNullOrEmpty(LoadedVideoPath);
    public string VideoTitle => HasVideoLoaded ? Path.GetFileName(LoadedVideoPath) : "Arraste um vídeo aqui ou clique em Abrir";

    private bool _mixAudioTracks = true;
    public bool MixAudioTracks
    {
        get => _mixAudioTracks;
        set { _mixAudioTracks = value; OnPropertyChanged(); }
    }

    private bool _isRendering = false;
    public bool IsRendering
    {
        get => _isRendering;
        set
        {
            _isRendering = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditTimeline));
        }
    }

    public bool CanEditTimeline => HasVideoLoaded && !IsRendering;

    private double _renderProgress = 0;
    public double RenderProgress
    {
        get => _renderProgress;
        set { _renderProgress = value; OnPropertyChanged(); }
    }

    private string _renderStatus = "Pronto";
    public string RenderStatus
    {
        get => _renderStatus;
        set { _renderStatus = value; OnPropertyChanged(); }
    }

    private string _lastRenderedPath = string.Empty;
    public string LastRenderedPath
    {
        get => _lastRenderedPath;
        set { _lastRenderedPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCompletedRender)); }
    }

    public bool HasCompletedRender => !string.IsNullOrEmpty(LastRenderedPath) && File.Exists(LastRenderedPath);

    private TimeSpan _currentTime = TimeSpan.Zero;
    public TimeSpan CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(PlayheadPercent));
        }
    }

    public string CurrentTimeText => $"{FormatTime(CurrentTime)} / {FormatTime(_timelineManager.RemainingDuration)}";
    public string MarkerAText => $"[A] {FormatTime(_timelineManager.MarkerA)}";
    public string MarkerBText => $"[B] {FormatTime(_timelineManager.MarkerB)}";

    public string SelectionDurationText
    {
        get
        {
            var diff = _timelineManager.MarkerB - _timelineManager.MarkerA;
            if (diff < TimeSpan.Zero) diff = TimeSpan.Zero;
            return $"Seleção: {diff.TotalSeconds:F2}s";
        }
    }

    public double PlayheadPercent
    {
        get => _timelineManager.RemainingDuration.TotalMilliseconds > 0
            ? Math.Clamp((CurrentTime.TotalMilliseconds / _timelineManager.RemainingDuration.TotalMilliseconds) * 100.0, 0, 100)
            : 0;
    }

    public double MarkerAPercent
    {
        get => _timelineManager.RemainingDuration.TotalMilliseconds > 0
            ? Math.Clamp((_timelineManager.MarkerA.TotalMilliseconds / _timelineManager.RemainingDuration.TotalMilliseconds) * 100.0, 0, 100)
            : 0;
    }

    public double MarkerBPercent
    {
        get => _timelineManager.RemainingDuration.TotalMilliseconds > 0
            ? Math.Clamp((_timelineManager.MarkerB.TotalMilliseconds / _timelineManager.RemainingDuration.TotalMilliseconds) * 100.0, 0, 100)
            : 100;
    }

    public double SelectionLeftPercent => Math.Min(MarkerAPercent, MarkerBPercent);
    public double SelectionWidthPercent => Math.Max(0.5, Math.Abs(MarkerBPercent - MarkerAPercent));

    public bool CanUndo => _timelineManager.CanUndo;

    public MainViewModel()
    {
        _timelineManager = new TimelineManager();
        _presetManager = new PresetManager();
        _envDetector = new EnvironmentDetectorService();
        _currentEnv = _envDetector.Detect();
        _pipelineService = new RenderPipelineService(_presetManager, new FfmpegCommandBuilder());

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var velocePresets = Path.Combine(appData, "VELOCE", "presets.json");
        var legacyPresets = Path.Combine(appData, "VELOCE", "presets.json");
        _presetsFilePath = File.Exists(velocePresets) || !File.Exists(legacyPresets) ? velocePresets : legacyPresets;

        LoadPresets();
    }

    public async Task BootstrapEngineAsync()
    {
        if (IsBootstrapping) return;

        IsBootstrapping = true;
        BootstrapProgress = 0;
        BootstrapStatus = "Iniciando configuração do motor...";

        try
        {
            var progress = new Progress<double>(p => BootstrapProgress = p);
            var status = new Progress<string>(s => BootstrapStatus = s);

            _currentEnv = await BootstrapperService.AutoBootstrapAsync(progress, status);
            RenderStatus = "Motor Tekno Blur configurado e pronto para renderização!";
        }
        catch (Exception ex)
        {
            BootstrapStatus = $"Erro ao configurar: {ex.Message}";
        }
        finally
        {
            _currentEnv = _envDetector.Detect();
            IsBootstrapping = false;
            OnPropertyChanged(nameof(IsEngineMissing));
            OnPropertyChanged(nameof(CurrentEnv));
        }
    }

    public void LoadPresets(string? preserveSelectedId = null)
    {
        var targetId = preserveSelectedId ?? SelectedBlurPreset?.Id;
        var config = _presetManager.LoadOrCreateDefault(_presetsFilePath);

        BlurPresets.Clear();
        foreach (var bp in config.BlurPresets) BlurPresets.Add(bp);

        if (!string.IsNullOrEmpty(targetId))
        {
            var found = BlurPresets.FirstOrDefault(p => p.Id == targetId || p.Name == targetId);
            SelectedBlurPreset = found ?? (BlurPresets.Count > 0 ? BlurPresets[0] : null);
        }
        else if (BlurPresets.Count > 0)
        {
            SelectedBlurPreset = BlurPresets[0];
        }

        var prevCpId = SelectedCompressionPreset?.Id;
        CompressionPresets.Clear();
        foreach (var cp in config.CompressionPresets) CompressionPresets.Add(cp);

        if (!string.IsNullOrEmpty(prevCpId))
        {
            var foundCp = CompressionPresets.FirstOrDefault(cp => cp.Id == prevCpId);
            SelectedCompressionPreset = foundCp ?? (CompressionPresets.Count > 0 ? CompressionPresets[0] : null);
        }
        else if (CompressionPresets.Count > 1)
        {
            SelectedCompressionPreset = CompressionPresets[1]; // 1080p60 by default
        }
        else if (CompressionPresets.Count > 0)
        {
            SelectedCompressionPreset = CompressionPresets[0];
        }
    }

    public void AddOrUpdateBlurPreset(BlurPreset preset, bool isNew)
    {
        if (isNew)
        {
            preset.Id = "preset_" + Guid.NewGuid().ToString("N")[..8];
            BlurPresets.Add(preset);
            SelectedBlurPreset = preset;
        }
        else
        {
            var existing = BlurPresets.FirstOrDefault(p => p.Id == preset.Id);
            if (existing != null)
            {
                existing.Name = preset.Name;
                existing.BlurAmount = preset.BlurAmount;
                existing.BlurGamma = preset.BlurGamma;
                existing.BlurWeighting = preset.BlurWeighting;
                existing.OutputFps = preset.OutputFps;
                existing.Interpolate = preset.Interpolate;
                existing.InterpolatedFps = preset.InterpolatedFps;
                existing.InterpolationMethod = preset.InterpolationMethod;
                existing.RifeModel = preset.RifeModel;
                existing.Quality = preset.Quality;
                SelectedBlurPreset = existing;
            }
        }
        SavePresets();
    }

    public void DeleteBlurPreset(BlurPreset preset)
    {
        if (BlurPresets.Count <= 1) return;
        BlurPresets.Remove(preset);
        SelectedBlurPreset = BlurPresets[0];
        SavePresets();
    }

    public void SavePresets()
    {
        var config = new PresetsConfig
        {
            BlurPresets = new System.Collections.Generic.List<BlurPreset>(BlurPresets),
            CompressionPresets = new System.Collections.Generic.List<CompressionPreset>(CompressionPresets)
        };
        _presetManager.Save(_presetsFilePath, config);
    }

    public string GetPresetsFilePath() => _presetsFilePath;

    public void OnVideoLoaded(string filePath, TimeSpan duration)
    {
        LoadedVideoPath = filePath;
        _timelineManager.LoadVideo(duration);
        CurrentTime = TimeSpan.Zero;
        LastRenderedPath = string.Empty;
        RenderStatus = "Vídeo pronto para edição.";
        RefreshTimelineProperties();
    }

    public void SetMarkerA(TimeSpan virtualTime)
    {
        _timelineManager.SetMarkerA(virtualTime);
        RefreshTimelineProperties();
    }

    public void SetMarkerB(TimeSpan virtualTime)
    {
        _timelineManager.SetMarkerB(virtualTime);
        RefreshTimelineProperties();
    }

    public void ClearSelection()
    {
        _timelineManager.ClearSelection();
        RefreshTimelineProperties();
    }

    public bool DeleteSelection(out TimeSpan cutPoint)
    {
        var selStart = _timelineManager.MarkerA < _timelineManager.MarkerB
            ? _timelineManager.MarkerA
            : _timelineManager.MarkerB;

        bool success = _timelineManager.DeleteSelection();
        if (success)
        {
            var remaining = _timelineManager.RemainingDuration;
            cutPoint = selStart > remaining ? remaining : selStart;
            CurrentTime = cutPoint;
            RefreshTimelineProperties();
        }
        else
        {
            cutPoint = TimeSpan.Zero;
        }
        return success;
    }

    public bool DeleteSelection() => DeleteSelection(out _);

    public bool Undo()
    {
        bool success = _timelineManager.Undo();
        if (success)
        {
            CurrentTime = TimeSpan.Zero;
            RefreshTimelineProperties();
        }
        return success;
    }

    public void SeekToPercent(double percent)
    {
        if (_timelineManager.RemainingDuration.TotalMilliseconds > 0)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            var targetMs = (clamped / 100.0) * _timelineManager.RemainingDuration.TotalMilliseconds;
            CurrentTime = TimeSpan.FromMilliseconds(targetMs);
        }
    }

    public async Task StartRenderAsync()
    {
        if (!HasVideoLoaded || SelectedBlurPreset == null || SelectedCompressionPreset == null)
        {
            return;
        }

        IsRendering = true;
        RenderProgress = 0;
        RenderStatus = "Iniciando renderização...";
        LastRenderedPath = string.Empty;
        _renderCts = new CancellationTokenSource();

        try
        {
            _currentEnv = _envDetector.Detect();
            if (string.IsNullOrEmpty(_currentEnv.BlurCliPath) || !File.Exists(_currentEnv.BlurCliPath))
            {
                RenderStatus = "Erro: blur-cli.exe não encontrado. Instale o motor de blur pelo banner acima.";
                IsRendering = false;
                OnPropertyChanged(nameof(IsEngineMissing));
                return;
            }

            var env = _currentEnv;
            var request = new RenderRequest(
                LoadedVideoPath,
                _timelineManager.Segments,
                SelectedBlurPreset,
                SelectedCompressionPreset,
                MixAudioTracks
            );

            var progress = new Progress<RenderProgress>(p =>
            {
                RenderProgress = p.Percentage;
                RenderStatus = $"{p.StageName} ({p.Percentage:0}%) - {p.DetailMessage}";
            });

            var outputPath = await _pipelineService.ExecuteAsync(request, env, progress, _renderCts.Token);
            LastRenderedPath = outputPath;
            RenderStatus = $"Renderização concluída! Salvo em: {Path.GetFileName(outputPath)}";
            RenderProgress = 100;
        }
        catch (OperationCanceledException)
        {
            RenderStatus = "Renderização cancelada.";
            RenderProgress = 0;
        }
        catch (Exception ex)
        {
            RenderStatus = $"Erro no processamento: {ex.Message}";
        }
        finally
        {
            IsRendering = false;
            _renderCts?.Dispose();
            _renderCts = null;
        }
    }

    public void CancelRender()
    {
        _renderCts?.Cancel();
    }

    public void OpenOutputFolder()
    {
        if (HasCompletedRender)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{LastRenderedPath}\"");
            }
            catch
            {
                var dir = Path.GetDirectoryName(LastRenderedPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", dir);
                }
            }
        }
    }

    public void RefreshTimelineProperties()
    {
        OnPropertyChanged(nameof(CurrentTimeText));
        OnPropertyChanged(nameof(MarkerAText));
        OnPropertyChanged(nameof(MarkerBText));
        OnPropertyChanged(nameof(SelectionDurationText));
        OnPropertyChanged(nameof(PlayheadPercent));
        OnPropertyChanged(nameof(MarkerAPercent));
        OnPropertyChanged(nameof(MarkerBPercent));
        OnPropertyChanged(nameof(SelectionLeftPercent));
        OnPropertyChanged(nameof(SelectionWidthPercent));
        OnPropertyChanged(nameof(CanUndo));
    }

    private static string FormatTime(TimeSpan ts)
    {
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
