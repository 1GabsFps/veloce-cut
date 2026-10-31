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
}
