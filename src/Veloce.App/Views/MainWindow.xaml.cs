using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComboBox = System.Windows.Controls.ComboBox;
using Veloce.App.Interop;
using Veloce.App.ViewModels;

namespace Veloce.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IVideoPlayerService _playerService;
    private bool _isDraggingScrubber = false;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _playerService = new MpvVideoPlayerService();

        Loaded += OnMainWindowLoaded;
        Closing += OnMainWindowClosing;

        _playerService.PositionChanged += OnPlayerPositionChanged;
        _playerService.MediaOpened += OnPlayerMediaOpened;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwndHost = IntPtr.Zero;
        try
        {
            hwndHost = MpvHostPanel.Handle;
        }
        catch
        {
            // Fallback
        }

        _playerService.Initialize(FallbackMediaElement, hwndHost);

        if (LibMpvInterop.IsMpvAvailable())
        {
            HwndHostContainer.Visibility = Visibility.Visible;
            FallbackMediaElement.Visibility = Visibility.Collapsed;
        }
        else
        {
            HwndHostContainer.Visibility = Visibility.Collapsed;
            FallbackMediaElement.Visibility = Visibility.Visible;
        }

        UpdateTimelineVisuals();

        // Check if opened via command line argument or Windows "Open with..."
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 4 && args[1] == "--record-demo")
        {
            _ = RunAutomatedDemoCaptureAsync(args[2], args[3]);
            return;
        }

        if (args.Length > 1 && File.Exists(args[1]))
        {
            LoadVideoFile(args[1]);
        }
    }

    private void OnPlayerPositionChanged(TimeSpan pos)
    {
        if (!_isDraggingScrubber)
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.CurrentTime = pos;
                UpdateTimelineVisuals();
            });
        }
    }

    private void OnPlayerMediaOpened(TimeSpan duration)
    {
        Dispatcher.Invoke(() =>
        {
            // Only refine duration on initial load if never detected before
            if (_viewModel.HasVideoLoaded && _viewModel.TimelineManager.TotalOriginalDuration <= TimeSpan.Zero && duration > TimeSpan.Zero)
            {
                _viewModel.OnVideoLoaded(_viewModel.LoadedVideoPath, duration);
                UpdateTimelineVisuals();
            }
        });
    }

    private void LoadVideoFile(string path)
    {
        if (!File.Exists(path)) return;

        // 1. Immediately probe video metadata with ffprobe for zero-delay UI unlock
        var env = _viewModel.EnvDetector.Detect();
        var (duration, fps) = _viewModel.EnvDetector.ProbeVideoMetadata(env.FfprobePath, path);

        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromSeconds(60);
        }

        // 2. Unlock ViewModel immediately
        _viewModel.OnVideoLoaded(path, duration);

        // 3. Open in video player
        _playerService.Open(path, fps);
        PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20;
        PlayPauseText.Text = "Play";

        UpdateTimelineVisuals();
    }

    private void OnOpenVideoClicked(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar Vídeo para Corte e Blur",
            Filter = "Vídeos de Gameplay (*.mp4;*.mkv;*.mov;*.avi)|*.mp4;*.mkv;*.mov;*.avi|Todos os arquivos (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            LoadVideoFile(ofd.FileName);
        }
    }

    private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                LoadVideoFile(files[0]);
            }
        }
    }

    private DateTime _lastSeekTime = DateTime.MinValue;

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_viewModel.HasVideoLoaded) return;

        // Space: Play / Pause
        if (e.Key == Key.Space)
        {
            OnPlayPauseClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Left / Right: Frame a Frame
        else if (e.Key == Key.Left)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                OnStepBack60SecClicked(this, new RoutedEventArgs());
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                OnStepBack1SecClicked(this, new RoutedEventArgs());
            }
            else
            {
                OnStepBackClicked(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                OnStepForward60SecClicked(this, new RoutedEventArgs());
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                OnStepForward1SecClicked(this, new RoutedEventArgs());
            }
            else
            {
                OnStepForwardClicked(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        // Up / Down: Keyframes
        else if (e.Key == Key.Up)
        {
            OnNextKeyframeClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            OnPrevKeyframeClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Home / End: Start / End of video
        else if (e.Key == Key.Home)
        {
            OnJumpToStartClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            OnJumpToEndClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // PageDown / PageUp: -60s / +60s
        else if (e.Key == Key.PageDown)
        {
            OnStepBack60SecClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            OnStepForward60SecClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Marker A: I, [, or A (when no modifier)
        else if (e.Key == Key.I || e.Key == Key.OemOpenBrackets || (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None))
        {
            OnSetMarkerAClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Marker B: O, ], or B (when no modifier)
        else if (e.Key == Key.O || e.Key == Key.OemCloseBrackets || (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None))
        {
            OnSetMarkerBClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Reset selection: X
        else if (e.Key == Key.X)
        {
            OnClearSelectionClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Delete: Remove Selection
        else if (e.Key == Key.Delete)
        {
            OnDeleteSelectionClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Ctrl + Z: Undo
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnUndoClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        // Ctrl + Enter: Render
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnStartRenderClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        _playerService.PlayPause();
        if (_playerService.IsPlaying)
        {
            PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause20;
            PlayPauseText.Text = "Pausar";
        }
        else
        {
            PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20;
            PlayPauseText.Text = "Play";
        }
    }

    private void OnStepBackClicked(object sender, RoutedEventArgs e)
    {
        _playerService.StepFrame(-1);
    }

    private void OnStepForwardClicked(object sender, RoutedEventArgs e)
    {
        _playerService.StepFrame(1);
    }

    private void OnPrevKeyframeClicked(object sender, RoutedEventArgs e)
    {
        _playerService.StepKeyframe(-1);
    }

    private void OnNextKeyframeClicked(object sender, RoutedEventArgs e)
    {
        _playerService.StepKeyframe(1);
    }

    private void OnStepBack1SecClicked(object sender, RoutedEventArgs e)
    {
        _playerService.SeekRelative(-1.0);
    }

    private void OnStepForward1SecClicked(object sender, RoutedEventArgs e)
    {
        _playerService.SeekRelative(1.0);
    }

    private void OnStepBack60SecClicked(object sender, RoutedEventArgs e)
    {
        _playerService.SeekRelative(-60.0);
    }

    private void OnStepForward60SecClicked(object sender, RoutedEventArgs e)
    {
        _playerService.SeekRelative(60.0);
    }

    private void OnJumpToStartClicked(object sender, RoutedEventArgs e)
    {
        _playerService.Seek(TimeSpan.Zero, exact: true);
        _viewModel.CurrentTime = TimeSpan.Zero;
        UpdateTimelineVisuals();
    }

    private void OnJumpToEndClicked(object sender, RoutedEventArgs e)
    {
        var end = _viewModel.TimelineManager.RemainingDuration;
        _playerService.Seek(end, exact: true);
        _viewModel.CurrentTime = end;
        UpdateTimelineVisuals();
    }

    private void OnSetMarkerAClicked(object sender, RoutedEventArgs e)
    {
        var pos = _playerService.IsPlaying ? _playerService.QueryExactPosition() : _viewModel.CurrentTime;
        _viewModel.CurrentTime = pos;
        _viewModel.SetMarkerA(pos);
        UpdateTimelineVisuals();
    }

    private void OnSetMarkerBClicked(object sender, RoutedEventArgs e)
    {
        var pos = _playerService.IsPlaying ? _playerService.QueryExactPosition() : _viewModel.CurrentTime;
        _viewModel.CurrentTime = pos;
        _viewModel.SetMarkerB(pos);
        UpdateTimelineVisuals();
    }

    private void OnClearSelectionClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearSelection();
        UpdateTimelineVisuals();
    }

    private void OnJumpToMarkerAClicked(object sender, RoutedEventArgs e)
    {
        _playerService.Seek(_viewModel.TimelineManager.MarkerA, exact: true);
        _viewModel.CurrentTime = _viewModel.TimelineManager.MarkerA;
        UpdateTimelineVisuals();
    }

    private void OnJumpToMarkerBClicked(object sender, RoutedEventArgs e)
    {
        _playerService.Seek(_viewModel.TimelineManager.MarkerB, exact: true);
        _viewModel.CurrentTime = _viewModel.TimelineManager.MarkerB;
        UpdateTimelineVisuals();
    }

    private void OnDeleteSelectionClicked(object sender, RoutedEventArgs e)
    {
        bool deleted = _viewModel.DeleteSelection(out var cutPoint);
        if (deleted)
        {
            var edl = _viewModel.TimelineManager.BuildMpvEdlUri(_viewModel.LoadedVideoPath);
            _playerService.ApplyEdl(edl, cutPoint);
            _playerService.Seek(cutPoint, exact: true);
            UpdateTimelineVisuals();
        }
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        bool undone = _viewModel.Undo();
        if (undone)
        {
            var edl = _viewModel.TimelineManager.BuildMpvEdlUri(_viewModel.LoadedVideoPath);
            _playerService.ApplyEdl(edl, _viewModel.CurrentTime);
            _playerService.Seek(_viewModel.CurrentTime, exact: true);
            UpdateTimelineVisuals();
        }
    }

    private void OnTimelineTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.HasVideoLoaded) return;

        TimelineTrackBorder.CaptureMouse();
        _isDraggingScrubber = true;
        SeekFromMouse(e.GetPosition(TimelineCanvas).X, isFinal: false);
    }

    private void OnTimelineTrackMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingScrubber)
        {
            SeekFromMouse(e.GetPosition(TimelineCanvas).X, isFinal: false);
        }
    }

    private void OnTimelineTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingScrubber)
        {
            _isDraggingScrubber = false;
            TimelineTrackBorder.ReleaseMouseCapture();
            SeekFromMouse(e.GetPosition(TimelineCanvas).X, isFinal: true);
        }
    }

    private void OnTimelineTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTimelineVisuals();
    }

    private void SeekFromMouse(double mouseX, bool isFinal)
    {
        double trackWidth = TimelineCanvas.ActualWidth;
        if (trackWidth <= 0) return;

        double percent = Math.Clamp(mouseX / trackWidth, 0.0, 1.0) * 100.0;
        _viewModel.SeekToPercent(percent);
        UpdateTimelineVisuals();

        if (isFinal)
        {
            _playerService.Seek(_viewModel.CurrentTime, exact: true);
        }
        else
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSeekTime).TotalMilliseconds >= 35)
            {
                _lastSeekTime = now;
                _playerService.Seek(_viewModel.CurrentTime, exact: false);
            }
        }
    }

    private void UpdateTimelineVisuals()
    {
        double width = TimelineCanvas.ActualWidth;
        if (width <= 0) return;

        if (!_viewModel.HasVideoLoaded)
        {
            TrackProgressRect.Visibility = Visibility.Collapsed;
            SelectionHighlightBorder.Visibility = Visibility.Collapsed;
            PlayheadLine.Visibility = Visibility.Collapsed;
            PlayheadThumb.Visibility = Visibility.Collapsed;
            return;
        }

        TrackProgressRect.Visibility = Visibility.Visible;
        SelectionHighlightBorder.Visibility = Visibility.Visible;
        PlayheadLine.Visibility = Visibility.Visible;
        PlayheadThumb.Visibility = Visibility.Visible;

        // 1. Playhead position (2px vertical line traversing 36px + top marker)
        double playheadX = Math.Clamp(width * (_viewModel.PlayheadPercent / 100.0), 0, width);
        PlayheadLine.X1 = playheadX;
        PlayheadLine.X2 = playheadX;

        // Blue track progress
        TrackProgressRect.Width = Math.Max(0, playheadX);

        Canvas.SetLeft(PlayheadThumb, playheadX);

        // 2. Selection range highlight [A, B] (Translucent band with 3px side handles)
        double selLeft = width * (_viewModel.SelectionLeftPercent / 100.0);
        double selWidth = width * (_viewModel.SelectionWidthPercent / 100.0);

        Canvas.SetLeft(SelectionHighlightBorder, selLeft);
        SelectionHighlightBorder.Width = Math.Max(3, selWidth);
    }

    private void OnEditPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBlurPreset == null) return;

        var editor = new PresetEditorWindow(
            _viewModel.SelectedBlurPreset,
            _viewModel.GetPresetsFilePath(),
            canDelete: _viewModel.BlurPresets.Count > 1,
            onSave: (preset, isNew) => _viewModel.AddOrUpdateBlurPreset(preset, isNew),
            onDelete: (preset) => _viewModel.DeleteBlurPreset(preset)
        )
        {
            Owner = this
        };

        editor.ShowDialog();
        _viewModel.LoadPresets();
        _viewModel.RefreshTimelineProperties();
    }

    private void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox cb && cb.IsDropDownOpen)
        {
            if (cb.Template.FindName("DropDownScrollViewer", cb) is ScrollViewer sv)
            {
                if (e.Delta < 0)
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + 38);
                else
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - 38);
                e.Handled = true;
            }
        }
    }

    private async void OnStartRenderClicked(object sender, RoutedEventArgs e)
    {
        _playerService.Pause();
        PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20;
        PlayPauseText.Text = "Play";
        await _viewModel.StartRenderAsync();
    }

    private void OnCancelRenderClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelRender();
    }

    private async void OnBootstrapEngineClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.BootstrapEngineAsync();
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenOutputFolder();
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsRendering)
        {
            var result = System.Windows.MessageBox.Show(
                "Uma renderização com Motion Blur está em andamento.\n\nDeseja cancelar o processo e sair do VELOCE?",
                "VELOCE - Confirmação de Saída",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            _viewModel.CancelRender();
        }

        try
        {
            _playerService.Dispose();
        }
        catch
        {
            // Ignore disposal errors on shutdown
        }
    }

    private async System.Threading.Tasks.Task RunAutomatedDemoCaptureAsync(string vidFramesDir, string outFramesDir)
    {
        Directory.CreateDirectory(outFramesDir);
        HwndHostContainer.Visibility = Visibility.Collapsed;
        FallbackMediaElement.Visibility = Visibility.Collapsed;
        DemoPreviewImage.Visibility = Visibility.Visible;

        var vidFiles = Directory.Exists(vidFramesDir)
            ? Directory.GetFiles(vidFramesDir, "vid_*.png")
            : Array.Empty<string>();
        Array.Sort(vidFiles);

        _viewModel.OnVideoLoaded(@"C:\Videos\Valorant 2026.10.05 - 20.54.44.138.DVR.mp4", TimeSpan.FromSeconds(20.65));
        if (_viewModel.BlurPresets.Count > 0)
        {
            var valPreset = _viewModel.BlurPresets.FirstOrDefault(p => p.Name.Contains("valorant", StringComparison.OrdinalIgnoreCase));
            if (valPreset != null) _viewModel.SelectedBlurPreset = valPreset;
        }
        UpdateTimelineVisuals();

        int frameCounter = 0;

        void SetGameplayFrame(int idx)
        {
            if (vidFiles.Length == 0) return;
            var path = vidFiles[Math.Clamp(idx, 0, vidFiles.Length - 1)];
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            DemoPreviewImage.Source = bmp;
        }

        async System.Threading.Tasks.Task CaptureUiFrameAsync()
        {
            UpdateLayout();
            await System.Threading.Tasks.Task.Delay(25);
            UpdateTimelineVisuals();
            UpdateLayout();

            int w = (int)ActualWidth;
            int h = (int)ActualHeight;
            if (w <= 0) w = 1220;
            if (h <= 0) h = 820;

            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18)), null, new Rect(0, 0, w, h));
                if (Content is System.Windows.Media.Visual rootVisual)
                {
                    var vb = new System.Windows.Media.VisualBrush(rootVisual) { Stretch = System.Windows.Media.Stretch.None };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
                }
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            frameCounter++;
            var outFile = Path.Combine(outFramesDir, $"ui_{frameCounter:D4}.png");
            using var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }

        // Phase 1: Playing video & scrubbing to start point [A] (14 frames: ~1.1s)
        PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause20;
        PlayPauseText.Text = "Pause";
        for (int i = 0; i < 14; i++)
        {
            SetGameplayFrame(i * 2);
            _viewModel.CurrentTime = TimeSpan.FromSeconds(1.0 + i * 0.25);
            await CaptureUiFrameAsync();
        }

        // Phase 2: Pause & Set Marker [A] at 00:04.250 (10 frames: ~0.8s)
        PlayPauseIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Play20;
        PlayPauseText.Text = "Play";
        _viewModel.SetMarkerA(_viewModel.CurrentTime);
        for (int i = 0; i < 10; i++)
        {
            SetGameplayFrame(28);
            await CaptureUiFrameAsync();
        }

        // Phase 3: Scrub forward to 00:08.500 and Set Marker [B] (12 frames: ~1.0s)
        for (int i = 0; i < 12; i++)
        {
            SetGameplayFrame(28 + i * 2);
            _viewModel.CurrentTime = TimeSpan.FromSeconds(4.25 + (i + 1) * 0.35);
            if (i == 6)
            {
                _viewModel.SetMarkerB(_viewModel.CurrentTime);
            }
            await CaptureUiFrameAsync();
        }

        // Phase 4: Press Recortar (Del) -> virtual timeline cut via EDL! (14 frames: ~1.1s)
        _viewModel.DeleteSelection(out _);
        _viewModel.RenderStatus = "Trecho removido! Timeline virtual EDL atualizada (Frame-Accurate).";
        for (int i = 0; i < 14; i++)
        {
            SetGameplayFrame(56 + i * 2);
            _viewModel.CurrentTime = TimeSpan.FromSeconds(4.30 + i * 0.20);
            await CaptureUiFrameAsync();
        }

        // Phase 5: Click CORTAR & APLICAR BLUR -> Render Progress 0% -> 100% (22 frames: ~1.8s)
        var stages = new (double pct, string msg)[]
        {
            (18, "Cortando segmentos (FFmpeg Fast-Seek -ss/-to)..."),
            (42, "Renderizando Motion Blur (42%) - SVP 1440 FPS..."),
            (72, "Renderizando Motion Blur (72%) - Ponderação Gaussiana..."),
            (88, "Comprimindo vídeo final (NVENC CQ 14)..."),
            (100, "Renderização concluída! Salvo em: Valorant_Highlight_blur.mp4")
        };

        foreach (var st in stages)
        {
            _viewModel.RenderProgress = st.pct;
            _viewModel.RenderStatus = st.msg;
            if (st.pct >= 100)
            {
                _viewModel.LastRenderedPath = @"C:\Videos\Valorant_Highlight_blur.mp4";
            }
            int reps = st.pct >= 100 ? 6 : 4;
            for (int f = 0; f < reps; f++)
            {
                await CaptureUiFrameAsync();
            }
        }

        Close();
    }
}
