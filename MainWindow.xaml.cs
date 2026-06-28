using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.CoreAudioApi;

using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Slider = System.Windows.Controls.Slider;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace EqualizerPro
{
    public class AudioDeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private DispatcherTimer _playbackTimer;
        private bool _isDraggingSeekbar = false;
        private bool _isUpdatingVolumeUI = false;

        private bool _isCompactMode = false;
        private int _activePanel = 0;

        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isForceClosing = false;

        private WasapiLoopbackCapture? _loopbackCapture;
        private WaveFileWriter? _waveWriter;
        private string _tempRecordPath = string.Empty;
        private DispatcherTimer _recordTimer;
        private TimeSpan _recordDuration;
        private bool _isRecording = false;

        private DispatcherTimer _marqueeTimer;

        private Slider[] _eqSliders;
        private Dictionary<string, double[]> _eqPresets = new Dictionary<string, double[]>();
        private bool _isUpdatingPreset = false;
        private bool _isEqEnabled = true;

        private bool _isFatModeEnabled = false;
        private bool _isSuperBassEnabled = false;
        private bool _isSpatialSoundEnabled = false;

        private bool _isStereoEnabled = true;
        private double _panValue = 0.0;

        private DispatcherTimer _visualizerTimer;

        private double[] _spectrumTargets = new double[48];
        private double[] _spectrumCurrents = new double[48];
        private double _spectrumFalloffDropRate = 2.0;

        // VECTORSCOPE ARRAYS
        private double[] _vectorTargets = new double[60];
        private double[] _vectorCurrents = new double[60];
        private double _currentCorrelation = 0.0; // Phase correlation value

        private bool _isEcoModeEnabled = false;
        private int _spectrumStyle = 0;
        private bool _isOSBlurEnabled = true;

        private double _leftDbTarget = 0;
        private double _leftDbCurrent = 0;
        private double _rightDbTarget = 0;
        private double _rightDbCurrent = 0;
        private double _leftPeak = 0;
        private double _rightPeak = 0;

        private Random _rand = new Random();

        private readonly string PlayIconData = "M2,2 L14,8 L2,14 Z";
        private readonly string PauseIconData = "M2,2 L5,2 L5,14 L2,14 Z M11,2 L14,2 L14,14 L11,14 Z";

        private bool _isDarkMode = true;
        private Color _currentAccent = Color.FromRgb(92, 97, 255);
        private Color _targetAccent = Color.FromRgb(92, 97, 255);
        private bool _isUpdatingColorPicker = false;
        private bool _isLoadingSettings = true;

        private Color _curWindowBg = Color.FromArgb(153, 11, 14, 20);
        private Color _tarWindowBg = Color.FromArgb(153, 11, 14, 20);
        private Color _curPanelBg = Color.FromArgb(115, 11, 14, 20);
        private Color _tarPanelBg = Color.FromArgb(115, 11, 14, 20);
        private Color _curText = Colors.White;
        private Color _tarText = Colors.White;
        private Color _curMutedText = Color.FromRgb(169, 177, 214);
        private Color _tarMutedText = Color.FromRgb(169, 177, 214);
        private Color _curBorder = Color.FromArgb(38, 255, 255, 255);
        private Color _tarBorder = Color.FromArgb(38, 255, 255, 255);
        private Color _curOverlay = Color.FromArgb(26, 255, 255, 255);
        private Color _tarOverlay = Color.FromArgb(26, 255, 255, 255);
        private Color _curHover = Color.FromArgb(51, 255, 255, 255);
        private Color _tarHover = Color.FromArgb(51, 255, 255, 255);

        public MainWindow()
        {
            InitializeComponent();

            if (SeekSlider != null) SeekSlider.IsMoveToPointEnabled = true;
            if (PlaybackBigSeekSlider != null) PlaybackBigSeekSlider.IsMoveToPointEnabled = true;
            if (CompactSeekSlider != null) CompactSeekSlider.IsMoveToPointEnabled = true;
            if (VolumeSliderControl != null) VolumeSliderControl.IsMoveToPointEnabled = true;
            if (CompactVolumeSlider != null) CompactVolumeSlider.IsMoveToPointEnabled = true;
            if (SpectrumFalloffSlider != null) SpectrumFalloffSlider.IsMoveToPointEnabled = true;
            if (PanFader != null) PanFader.IsMoveToPointEnabled = true;

            _eqSliders = new Slider[] { Slider1, Slider2, Slider3, Slider4, Slider5, Slider6, Slider7, Slider8, Slider9, Slider10 };
            InitializePresets();

            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(500);
            _playbackTimer.Tick += PlaybackTimer_Tick;

            _visualizerTimer = new DispatcherTimer();
            _visualizerTimer.Interval = TimeSpan.FromMilliseconds(16.66);
            _visualizerTimer.Tick += VisualizerTimer_Tick;

            _recordTimer = new DispatcherTimer();
            _recordTimer.Interval = TimeSpan.FromSeconds(1);
            _recordTimer.Tick += RecordTimer_Tick;

            _marqueeTimer = new DispatcherTimer();
            _marqueeTimer.Interval = TimeSpan.FromMilliseconds(40);
            _marqueeTimer.Tick += MarqueeTimer_Tick;
            _marqueeTimer.Start();

            for (int i = 0; i < 48; i++) _spectrumCurrents[i] = 2;
            for (int i = 0; i < 60; i++) _vectorCurrents[i] = 0;

            Loaded += MainWindow_Loaded;
            InitializeSystemTray();
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            LoadCustomPresets();
            LoadSettings();

            PushColorsToUI();
            LoadProfileImage();

            try
            {
                SystemVolumeManager.Initialize();
                SystemVolumeManager.OnVolumeChanged += (newVolume) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isUpdatingVolumeUI = true;
                        if (VolumeSliderControl != null) VolumeSliderControl.Value = newVolume * 100.0;
                        if (CompactVolumeSlider != null) CompactVolumeSlider.Value = newVolume * 100.0;
                        _isUpdatingVolumeUI = false;
                    });
                };

                _isUpdatingVolumeUI = true;
                double currentVolume = SystemVolumeManager.GetVolume();
                if (VolumeSliderControl != null) VolumeSliderControl.Value = currentVolume;
                if (CompactVolumeSlider != null) CompactVolumeSlider.Value = currentVolume;
                _isUpdatingVolumeUI = false;

                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                    UpdateCurrentSession(_sessionManager.GetCurrentSession());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Init failed: " + ex.Message);
            }

            _visualizerTimer.Start();
            _isLoadingSettings = false;
        }

        private void MonoStereoToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isStereoEnabled = toggle.IsChecked ?? true;
                ApplyEqToAudioStream();
            }
        }

        private void PanFader_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            _panValue = e.NewValue;
            ApplyEqToAudioStream();
        }

        private void SidebarToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            bool isCollapsed = SidebarToggleBtn.IsChecked ?? false;

            DoubleAnimation widthAnim = new DoubleAnimation
            {
                To = isCollapsed ? 80 : 220,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarBorder.BeginAnimation(WidthProperty, widthAnim);

            DoubleAnimation rotateAnim = new DoubleAnimation
            {
                To = isCollapsed ? 180 : 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarArrowTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

            Visibility textVis = isCollapsed ? Visibility.Collapsed : Visibility.Visible;

            if (NavEqActiveText != null) NavEqActiveText.Visibility = textVis;
            if (NavEqText != null) NavEqText.Visibility = textVis;
            if (NavPlaybackActiveText != null) NavPlaybackActiveText.Visibility = textVis;
            if (NavPlaybackText != null) NavPlaybackText.Visibility = textVis;
            if (RecordTimeText != null) RecordTimeText.Visibility = textVis;
            if (NavRecordText != null) NavRecordText.Visibility = textVis;

            if (NavAnalogActiveText != null) NavAnalogActiveText.Visibility = textVis;
            if (NavAnalogText != null) NavAnalogText.Visibility = textVis;

            if (NavSettingsActiveText != null) NavSettingsActiveText.Visibility = textVis;
            if (NavSettingsText != null) NavSettingsText.Visibility = textVis;
            if (NavAboutText != null) NavAboutText.Visibility = textVis;
        }

        private void EqExpandBtn_Click(object sender, RoutedEventArgs e)
        {
            bool isExpanded = EqExpandBtn.IsChecked ?? false;

            if (isExpanded)
            {
                VisualizersContainer.Visibility = Visibility.Collapsed;
                EqVisualizersRow.Height = new GridLength(0);

                DoubleAnimation rotateAnim = new DoubleAnimation(180, TimeSpan.FromMilliseconds(200));
                EqExpandIconTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
            }
            else
            {
                VisualizersContainer.Visibility = Visibility.Visible;
                EqVisualizersRow.Height = new GridLength(1.2, GridUnitType.Star);

                DoubleAnimation rotateAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
                EqExpandIconTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
            }
        }

        private void CompactModeBtn_Click(object? sender, RoutedEventArgs e)
        {
            _isCompactMode = !_isCompactMode;

            if (_isCompactMode)
            {
                CompactModeBtn.Content = "🗖";
                if (MaximizeBtn != null) MaximizeBtn.IsEnabled = false; // Disable maximize in compact mode

                this.MinWidth = 680;
                this.MinHeight = 260;

                SidebarBorder.Visibility = Visibility.Collapsed;
                if (SidebarToggleBtn != null) SidebarToggleBtn.Visibility = Visibility.Collapsed;
                EqContentPanel.Visibility = Visibility.Collapsed;
                if (PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Collapsed;
                SettingsContentPanel.Visibility = Visibility.Collapsed;
                if (AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Collapsed;
                if (AppTitleText != null) AppTitleText.Visibility = Visibility.Collapsed;
                if (BottomPlaybackBarBorder != null) BottomPlaybackBarBorder.Visibility = Visibility.Collapsed;

                if (CompactEqToggle != null) CompactEqToggle.Visibility = Visibility.Visible;
                if (CompactPlaybackPanel != null) CompactPlaybackPanel.Visibility = Visibility.Visible;

                DoubleAnimation widthAnim = new DoubleAnimation(this.ActualWidth, 680, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoubleAnimation heightAnim = new DoubleAnimation(this.ActualHeight, 260, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

                widthAnim.Completed += (s, ev) => { this.BeginAnimation(Window.WidthProperty, null); this.Width = 680; };
                heightAnim.Completed += (s, ev) => { this.BeginAnimation(Window.HeightProperty, null); this.Height = 260; };

                this.BeginAnimation(Window.WidthProperty, widthAnim);
                this.BeginAnimation(Window.HeightProperty, heightAnim);
            }
            else
            {
                CompactModeBtn.Content = "🗗";
                if (MaximizeBtn != null) MaximizeBtn.IsEnabled = true; // Re-enable maximize in normal mode

                this.MinWidth = 850;
                this.MinHeight = 600;

                if (CompactEqToggle != null) CompactEqToggle.Visibility = Visibility.Collapsed;
                if (CompactPlaybackPanel != null) CompactPlaybackPanel.Visibility = Visibility.Collapsed;

                SidebarBorder.Visibility = Visibility.Visible;
                if (SidebarToggleBtn != null) SidebarToggleBtn.Visibility = Visibility.Visible;
                if (AppTitleText != null) AppTitleText.Visibility = Visibility.Visible;

                if (_activePanel == 0) EqContentPanel.Visibility = Visibility.Visible;
                else if (_activePanel == 2) SettingsContentPanel.Visibility = Visibility.Visible;
                else if (_activePanel == 3 && PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Visible;
                else if (_activePanel == 4 && AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Visible;

                if (BottomPlaybackBarBorder != null)
                {
                    BottomPlaybackBarBorder.Visibility = (_activePanel == 3) ? Visibility.Collapsed : Visibility.Visible;
                }

                DoubleAnimation widthAnim = new DoubleAnimation(this.ActualWidth, 1100, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoubleAnimation heightAnim = new DoubleAnimation(this.ActualHeight, 768, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

                widthAnim.Completed += (s, ev) => { this.BeginAnimation(Window.WidthProperty, null); this.Width = 1100; };
                heightAnim.Completed += (s, ev) => { this.BeginAnimation(Window.HeightProperty, null); this.Height = 768; };

                this.BeginAnimation(Window.WidthProperty, widthAnim);
                this.BeginAnimation(Window.HeightProperty, heightAnim);
            }
        }

        private void EqualizerBtn_Click(object? sender, RoutedEventArgs e)
        {
            _activePanel = 0;
            if (NavEqActive == null || NavSettingsActive == null || NavPlaybackActive == null || NavAnalogActive == null) return;

            NavEqActive.Visibility = Visibility.Visible;
            NavEqBtn.Visibility = Visibility.Collapsed;

            NavPlaybackActive.Visibility = Visibility.Collapsed;
            NavPlaybackBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Collapsed;
            NavSettingsBtn.Visibility = Visibility.Visible;

            NavAnalogActive.Visibility = Visibility.Collapsed;
            NavAnalogBtn.Visibility = Visibility.Visible;

            SettingsContentPanel.Visibility = Visibility.Collapsed;
            if (PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Collapsed;
            if (AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Collapsed;
            EqContentPanel.Visibility = Visibility.Visible;

            if (BottomPlaybackBarBorder != null) BottomPlaybackBarBorder.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            EqContentPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void PlaybackNavBtn_Click(object? sender, RoutedEventArgs e)
        {
            _activePanel = 3;
            if (NavEqActive == null || NavSettingsActive == null || NavPlaybackActive == null || NavAnalogActive == null) return;

            NavEqActive.Visibility = Visibility.Collapsed;
            NavEqBtn.Visibility = Visibility.Visible;

            NavPlaybackActive.Visibility = Visibility.Visible;
            NavPlaybackBtn.Visibility = Visibility.Collapsed;

            NavSettingsActive.Visibility = Visibility.Collapsed;
            NavSettingsBtn.Visibility = Visibility.Visible;

            NavAnalogActive.Visibility = Visibility.Collapsed;
            NavAnalogBtn.Visibility = Visibility.Visible;

            EqContentPanel.Visibility = Visibility.Collapsed;
            SettingsContentPanel.Visibility = Visibility.Collapsed;
            if (AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Collapsed;
            if (PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Visible;

            if (BottomPlaybackBarBorder != null) BottomPlaybackBarBorder.Visibility = Visibility.Collapsed;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            PlaybackContentPanel?.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void SettingsBtn_Click(object? sender, RoutedEventArgs e)
        {
            _activePanel = 2;
            if (NavEqActive == null || NavSettingsActive == null || NavPlaybackActive == null || NavAnalogActive == null) return;

            NavEqActive.Visibility = Visibility.Collapsed;
            NavEqBtn.Visibility = Visibility.Visible;

            NavPlaybackActive.Visibility = Visibility.Collapsed;
            NavPlaybackBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Visible;
            NavSettingsBtn.Visibility = Visibility.Collapsed;

            NavAnalogActive.Visibility = Visibility.Collapsed;
            NavAnalogBtn.Visibility = Visibility.Visible;

            EqContentPanel.Visibility = Visibility.Collapsed;
            if (PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Collapsed;
            if (AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Collapsed;
            SettingsContentPanel.Visibility = Visibility.Visible;

            if (BottomPlaybackBarBorder != null) BottomPlaybackBarBorder.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            SettingsContentPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void AnalogBtn_Click(object? sender, RoutedEventArgs e)
        {
            _activePanel = 4;
            if (NavEqActive == null || NavSettingsActive == null || NavPlaybackActive == null || NavAnalogActive == null) return;

            NavEqActive.Visibility = Visibility.Collapsed;
            NavEqBtn.Visibility = Visibility.Visible;

            NavPlaybackActive.Visibility = Visibility.Collapsed;
            NavPlaybackBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Collapsed;
            NavSettingsBtn.Visibility = Visibility.Visible;

            NavAnalogActive.Visibility = Visibility.Visible;
            NavAnalogBtn.Visibility = Visibility.Collapsed;

            EqContentPanel.Visibility = Visibility.Collapsed;
            if (PlaybackContentPanel != null) PlaybackContentPanel.Visibility = Visibility.Collapsed;
            SettingsContentPanel.Visibility = Visibility.Collapsed;
            if (AnalogContentPanel != null) AnalogContentPanel.Visibility = Visibility.Visible;

            if (BottomPlaybackBarBorder != null) BottomPlaybackBarBorder.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            AnalogContentPanel?.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void FatModeToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isFatModeEnabled = toggle.IsChecked ?? false;
                ApplyEqToAudioStream();
            }
        }

        private void SuperBassToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isSuperBassEnabled = toggle.IsChecked ?? false;
                ApplyEqToAudioStream();
            }
        }

        private void SpatialSoundToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isSpatialSoundEnabled = toggle.IsChecked ?? false;
                ApplyEqToAudioStream();
            }
        }

        private void SpectrumStyleSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || SpectrumStyleSelector == null || EqSpectrumGrid == null || SpectrumLinePath == null || SpectrumWavePath == null) return;

            _spectrumStyle = SpectrumStyleSelector.SelectedIndex;

            EqSpectrumGrid.Visibility = _spectrumStyle == 0 ? Visibility.Visible : Visibility.Collapsed;

            SpectrumLinePathGlow.Visibility = _spectrumStyle == 1 ? Visibility.Visible : Visibility.Collapsed;
            SpectrumLinePath.Visibility = _spectrumStyle == 1 ? Visibility.Visible : Visibility.Collapsed;

            SpectrumWavePath.Visibility = _spectrumStyle == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetVisualizerFrameRate(int comboIndex)
        {
            if (_visualizerTimer == null) return;

            switch (comboIndex)
            {
                case 0:
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(33.33);
                    break;
                case 1:
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(16.66);
                    break;
                case 2:
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(8.33);
                    break;
            }
        }

        private void VisualizerFpsSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var comboBox = sender as ComboBox;
            if (comboBox != null)
            {
                SetVisualizerFrameRate(comboBox.SelectedIndex);
            }
        }

        private void SpectrumFalloffSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            _spectrumFalloffDropRate = (e.NewValue / 100.0) * 5.0;
            if (_spectrumFalloffDropRate < 0.1) _spectrumFalloffDropRate = 0.1;
        }

        private void EcoModeToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isEcoModeEnabled = toggle.IsChecked ?? false;

                double targetOpacity = _isEcoModeEnabled ? 0.3 : 1.0;
                var fadeAnim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(300));

                if (VectorscopePanel != null) VectorscopePanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                if (EqSpectrumPanel != null) EqSpectrumPanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
        }

        private void OSBlurToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isOSBlurEnabled = toggle.IsChecked ?? true;
                SetModeColors(_isDarkMode);
                SyncThemeVariablesToTarget();
                PushColorsToUI();
            }
        }

        private void RecordBtn_Click(object? sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!_isRecording) StartRecording();
            else StopRecording();
        }

        private void RecordBtn_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!_isRecording) StartRecording();
            else StopRecording();
        }

        private void StartRecording()
        {
            try
            {
                bool isPlayingState = _currentSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                float currentPeak = SystemVolumeManager.GetPeakValue();

                if (!isPlayingState && currentPeak < 0.001f)
                {
                    MessageBox.Show("Please play a song first before starting the recording.", "No Audio Detected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _tempRecordPath = Path.Combine(Path.GetTempPath(), $"EqualizerPro_Rec_{Guid.NewGuid()}.wav");
                _loopbackCapture = new WasapiLoopbackCapture();
                _waveWriter = new WaveFileWriter(_tempRecordPath, _loopbackCapture.WaveFormat);

                _loopbackCapture.DataAvailable += (s, a) =>
                {
                    _waveWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                };

                _loopbackCapture.RecordingStopped += LoopbackCapture_RecordingStopped;

                _loopbackCapture.StartRecording();
                _isRecording = true;

                NavRecordBtn.Visibility = Visibility.Collapsed;
                NavRecordActive.Visibility = Visibility.Visible;
                _recordDuration = TimeSpan.Zero;
                RecordTimeText.Text = "00:00";
                _recordTimer.Start();

                var glowEffect = new DropShadowEffect
                {
                    Color = Colors.Red,
                    ShadowDepth = 0,
                    BlurRadius = 5,
                    Opacity = 1.0
                };
                RecordBlinkDot.Effect = glowEffect;

                DoubleAnimation blurAnim = new DoubleAnimation(4, 18, TimeSpan.FromMilliseconds(800))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);

                DoubleAnimation opacityAnim = new DoubleAnimation(1.0, 0.4, TimeSpan.FromMilliseconds(800))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                RecordBlinkDot.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

                var bgGlowAnim = new ColorAnimation
                {
                    From = Color.FromArgb(26, 255, 255, 255),
                    To = Color.FromArgb(40, 255, 50, 50),
                    Duration = TimeSpan.FromMilliseconds(800),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                var recordBgBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
                NavRecordActive.Background = recordBgBrush;
                recordBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgGlowAnim);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start recording: " + ex.Message);
            }
        }

        private void StopRecording()
        {
            _isRecording = false;
            _recordTimer.Stop();

            _loopbackCapture?.StopRecording();

            NavRecordActive.Visibility = Visibility.Collapsed;
            NavRecordBtn.Visibility = Visibility.Visible;

            RecordBlinkDot.BeginAnimation(UIElement.OpacityProperty, null!);
            RecordBlinkDot.Opacity = 1.0;

            if (RecordBlinkDot.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null!);
            }
            RecordBlinkDot.Effect = null;

            NavRecordActive.Background = (System.Windows.Media.Brush)FindResource("GlassOverlayBrush");
        }

        private void LoopbackCapture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _waveWriter?.Dispose();
                _waveWriter = null;
                _loopbackCapture?.Dispose();
                _loopbackCapture = null;

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "WAV Audio File (*.wav)|*.wav",
                    Title = "Save Recorded Audio",
                    FileName = "EqualizerPro_Recording_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".wav"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        if (File.Exists(_tempRecordPath))
                        {
                            File.Copy(_tempRecordPath, saveFileDialog.FileName, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving file: " + ex.Message);
                    }
                }

                if (File.Exists(_tempRecordPath))
                {
                    try { File.Delete(_tempRecordPath); } catch { }
                }
            });
        }

        private void RecordTimer_Tick(object? sender, EventArgs e)
        {
            _recordDuration = _recordDuration.Add(TimeSpan.FromSeconds(1));
            RecordTimeText.Text = string.Format("{0:D2}:{1:D2}", _recordDuration.Minutes, _recordDuration.Seconds);
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "Equalizer Pro";
            _notifyIcon.Visible = false;

            try
            {
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { }

            _notifyIcon.DoubleClick += (s, args) => ShowFromTray();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Equalizer Pro", (System.Drawing.Image?)null, (s, args) => ShowFromTray());
            contextMenu.Items.Add("Exit", (System.Drawing.Image?)null, (s, args) => ForceExit());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void HideToTray()
        {
            this.Hide();
            if (_notifyIcon != null) _notifyIcon.Visible = true;
        }

        private void ShowFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            if (_notifyIcon != null) _notifyIcon.Visible = false;
        }

        private void ForceExit()
        {
            _isForceClosing = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized && MinimizeToTrayToggle != null && MinimizeToTrayToggle.IsChecked == true)
            {
                HideToTray();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isForceClosing && MinimizeToTrayToggle != null && MinimizeToTrayToggle.IsChecked == true)
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                SaveSettings();
                _isEqEnabled = false;
                ApplyEqToAudioStream();
            }

            base.OnClosing(e);
        }

        private void LoadProfileImage()
        {
            try
            {
                string exeFolder = AppDomain.CurrentDomain.BaseDirectory;
                string imagePath = Path.Combine(exeFolder, "28db0a06-a5d2-4087-8c8d-19ca90566dc3.jpg");

                if (File.Exists(imagePath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    ProfileImageBrush.ImageSource = bitmap;
                }
            }
            catch { }
        }

        private string GetSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EqualizerPro");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "settings.ini");
        }

        private void SaveCustomPresets()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string folder = Path.Combine(appData, "EqualizerPro");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, "custom_presets.ini");

                List<string> lines = new List<string>();
                var defaultPresets = new HashSet<string> { "Custom", "Acoustic", "Bass Boost", "Classical", "Dance", "Electronic", "Hip-Hop", "Jazz", "Pop", "R&B", "Rock", "Treble Boost", "Vocal" };

                foreach (var kvp in _eqPresets)
                {
                    if (!defaultPresets.Contains(kvp.Key))
                    {
                        string vals = string.Join(",", kvp.Value);
                        lines.Add($"{kvp.Key}={vals}");
                    }
                }
                File.WriteAllLines(path, lines);
            }
            catch { }
        }

        private void LoadCustomPresets()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string folder = Path.Combine(appData, "EqualizerPro");
                string path = Path.Combine(folder, "custom_presets.ini");

                if (CustomPresetsContainer != null) CustomPresetsContainer.Children.Clear();

                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string name = parts[0].Trim();
                            string[] valStrings = parts[1].Split(',');
                            if (valStrings.Length == 10 && !string.IsNullOrEmpty(name))
                            {
                                double[] vals = new double[10];
                                bool parsedAll = true;
                                for (int i = 0; i < 10; i++)
                                {
                                    if (!double.TryParse(valStrings[i], out vals[i]))
                                    {
                                        parsedAll = false;
                                        break;
                                    }
                                }

                                if (parsedAll && !_eqPresets.ContainsKey(name))
                                {
                                    _eqPresets.Add(name, vals);
                                    AddCustomPresetToUI(name);
                                }
                            }
                        }
                    }
                }

                UpdateEmptyCustomText();
            }
            catch { }
        }

        private void AddCustomPresetToUI(string name)
        {
            if (CustomPresetsContainer == null) return;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            Button selectBtn = new Button();
            selectBtn.Style = (Style)FindResource("DropdownItemStyle");
            selectBtn.Content = name;
            selectBtn.Click += (s, e) => {
                SelectPreset(name);
                if (PresetDropdownToggle != null) PresetDropdownToggle.IsChecked = false;
            };
            Grid.SetColumn(selectBtn, 0);
            grid.Children.Add(selectBtn);

            Button deleteBtn = new Button();
            deleteBtn.Style = (Style)FindResource("DropdownActionBtnStyle");
            deleteBtn.ToolTip = "Delete Preset";

            System.Windows.Shapes.Path pathIcon = new System.Windows.Shapes.Path();
            pathIcon.Data = Geometry.Parse("M2,2 L10,10 M10,2 L2,10");
            pathIcon.Stroke = (SolidColorBrush)FindResource("MutedTextBrush");
            pathIcon.StrokeThickness = 1.5;
            pathIcon.Stretch = Stretch.Uniform;
            pathIcon.Width = 9;
            pathIcon.Height = 9;
            deleteBtn.Content = pathIcon;

            deleteBtn.Click += (s, e) => {
                CustomPresetsContainer.Children.Remove(grid);
                _eqPresets.Remove(name);
                SaveCustomPresets();
                UpdateEmptyCustomText();

                if (PresetDropdownToggle != null && PresetDropdownToggle.Content?.ToString() == name)
                {
                    SelectPreset("Custom");
                }
            };

            Grid.SetColumn(deleteBtn, 1);
            grid.Children.Add(deleteBtn);

            CustomPresetsContainer.Children.Add(grid);
        }

        private void UpdateEmptyCustomText()
        {
            if (NoCustomPresetsText != null && CustomPresetsContainer != null)
            {
                NoCustomPresetsText.Visibility = CustomPresetsContainer.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OpenCustomMenu_Click(object sender, RoutedEventArgs e)
        {
            if (MainPresetList != null && CustomPresetList != null)
            {
                MainPresetList.Visibility = Visibility.Collapsed;
                CustomPresetList.Visibility = Visibility.Visible;
            }
        }

        private void CloseCustomMenu_Click(object sender, RoutedEventArgs e)
        {
            if (MainPresetList != null && CustomPresetList != null)
            {
                CustomPresetList.Visibility = Visibility.Collapsed;
                MainPresetList.Visibility = Visibility.Visible;
            }
        }

        private void PresetPopup_Closed(object sender, EventArgs e)
        {
            if (MainPresetList != null && CustomPresetList != null)
            {
                CustomPresetList.Visibility = Visibility.Collapsed;
                MainPresetList.Visibility = Visibility.Visible;
            }
        }

        private void PresetItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                string presetName = btn.Content.ToString() ?? "Custom";
                SelectPreset(presetName);

                if (PresetDropdownToggle != null) PresetDropdownToggle.IsChecked = false;
            }
        }

        private void SelectPreset(string presetName)
        {
            if (_eqPresets.TryGetValue(presetName, out double[]? values))
            {
                _isUpdatingPreset = true;
                if (PresetDropdownToggle != null) PresetDropdownToggle.Content = presetName;

                for (int i = 0; i < 10; i++) _eqSliders[i].Value = values[i];

                if (_isEqEnabled) ApplyEqToAudioStream();
                _isUpdatingPreset = false;
            }
        }

        private void EqTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox tb)
                {
                    var binding = tb.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                    binding?.UpdateTarget();
                    Keyboard.ClearFocus();
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                string currentPreset = PresetDropdownToggle?.Content?.ToString() ?? "Custom";
                string toggleState = _isEqEnabled.ToString();
                string darkModeState = _isDarkMode.ToString();
                string accentColorHex = $"#{_targetAccent.A:X2}{_targetAccent.R:X2}{_targetAccent.G:X2}{_targetAccent.B:X2}";
                string alwaysOnTopState = (AlwaysOnTopToggle?.IsChecked ?? false).ToString();
                string minTrayState = (MinimizeToTrayToggle?.IsChecked ?? true).ToString();
                string startWinState = (StartWithWindowsToggle?.IsChecked ?? false).ToString();
                string fpsIndex = (VisualizerFpsSelector?.SelectedIndex ?? 1).ToString();
                string falloffValue = (SpectrumFalloffSlider?.Value ?? 40).ToString();
                string ecoModeState = _isEcoModeEnabled.ToString();
                string fatModeState = _isFatModeEnabled.ToString();
                string superBassState = _isSuperBassEnabled.ToString();
                string spatialState = _isSpatialSoundEnabled.ToString();
                string specStyle = _spectrumStyle.ToString();
                string osBlurState = _isOSBlurEnabled.ToString();
                string stereoState = _isStereoEnabled.ToString();
                string panValue = _panValue.ToString();

                File.WriteAllLines(GetSettingsFilePath(), new string[] {
                    currentPreset, toggleState, darkModeState, accentColorHex,
                    alwaysOnTopState, minTrayState, startWinState, fpsIndex, falloffValue,
                    ecoModeState, fatModeState, superBassState, spatialState, specStyle,
                    osBlurState, stereoState, panValue
                });
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);

                    if (lines.Length >= 1)
                    {
                        string savedPreset = lines[0].Trim();
                        SelectPreset(savedPreset);
                    }

                    if (lines.Length >= 2)
                    {
                        if (bool.TryParse(lines[1], out bool isEnabled))
                        {
                            _isEqEnabled = isEnabled;

                            if (GlobalEqToggle != null) GlobalEqToggle.IsChecked = isEnabled;
                            if (CompactEqToggle != null) CompactEqToggle.IsChecked = isEnabled;

                            if (SlidersContainerGrid != null) SlidersContainerGrid.Opacity = _isEqEnabled ? 1.0 : 0.4;
                        }
                    }

                    if (lines.Length >= 3)
                    {
                        if (bool.TryParse(lines[2], out bool isDark))
                        {
                            _isDarkMode = isDark;
                            ModeSwitchBtn.Content = _isDarkMode ? "☀" : "🌙";
                        }
                    }

                    if (lines.Length >= 4 && ThemeSelector != null)
                    {
                        try
                        {
                            _targetAccent = (Color)ColorConverter.ConvertFromString(lines[3].Trim());
                            string? themeName = GetThemeNameFromColor(_targetAccent);
                            bool foundMatch = false;

                            if (themeName != null)
                            {
                                foreach (ComboBoxItem item in ThemeSelector.Items)
                                {
                                    if (item.Content?.ToString() == themeName)
                                    {
                                        ThemeSelector.SelectedItem = item;
                                        foundMatch = true;
                                        break;
                                    }
                                }
                            }
                            if (!foundMatch) ThemeSelector.SelectedIndex = -1;
                        }
                        catch { }
                    }

                    if (lines.Length >= 5)
                    {
                        if (bool.TryParse(lines[4], out bool isAlwaysOnTop))
                        {
                            if (AlwaysOnTopToggle != null) AlwaysOnTopToggle.IsChecked = isAlwaysOnTop;
                            this.Topmost = false;
                            if (isAlwaysOnTop) this.Topmost = true;
                        }
                    }

                    if (lines.Length >= 6)
                    {
                        if (bool.TryParse(lines[5], out bool isMinTray))
                        {
                            if (MinimizeToTrayToggle != null) MinimizeToTrayToggle.IsChecked = isMinTray;
                        }
                    }

                    if (lines.Length >= 7)
                    {
                        if (bool.TryParse(lines[6], out bool isStartWin))
                        {
                            if (StartWithWindowsToggle != null) StartWithWindowsToggle.IsChecked = isStartWin;
                            SetStartWithWindows(isStartWin);
                        }
                    }

                    if (lines.Length >= 8 && VisualizerFpsSelector != null)
                    {
                        if (int.TryParse(lines[7], out int fpsIndex))
                        {
                            VisualizerFpsSelector.SelectedIndex = fpsIndex;
                            SetVisualizerFrameRate(fpsIndex);
                        }
                    }

                    if (lines.Length >= 9 && SpectrumFalloffSlider != null)
                    {
                        if (double.TryParse(lines[8], out double falloffVal))
                        {
                            SpectrumFalloffSlider.Value = falloffVal;
                        }
                    }

                    if (lines.Length >= 10 && EcoModeToggle != null)
                    {
                        if (bool.TryParse(lines[9], out bool isEco))
                        {
                            _isEcoModeEnabled = isEco;
                            EcoModeToggle.IsChecked = isEco;

                            double targetOpacity = _isEcoModeEnabled ? 0.3 : 1.0;
                            if (VectorscopePanel != null) VectorscopePanel.Opacity = targetOpacity;
                            if (EqSpectrumPanel != null) EqSpectrumPanel.Opacity = targetOpacity;
                        }
                    }

                    if (lines.Length >= 11 && FatModeToggle != null)
                    {
                        if (bool.TryParse(lines[10], out bool isFat))
                        {
                            _isFatModeEnabled = isFat;
                            FatModeToggle.IsChecked = isFat;
                        }
                    }

                    if (lines.Length >= 12 && SuperBassToggle != null)
                    {
                        if (bool.TryParse(lines[11], out bool isBass))
                        {
                            _isSuperBassEnabled = isBass;
                            SuperBassToggle.IsChecked = isBass;
                        }
                    }

                    if (lines.Length >= 13 && SpatialSoundToggle != null)
                    {
                        if (bool.TryParse(lines[12], out bool isSpatial))
                        {
                            _isSpatialSoundEnabled = isSpatial;
                            SpatialSoundToggle.IsChecked = isSpatial;
                        }
                    }

                    if (lines.Length >= 14 && SpectrumStyleSelector != null)
                    {
                        if (int.TryParse(lines[13], out int specStyle))
                        {
                            _spectrumStyle = specStyle;
                            SpectrumStyleSelector.SelectedIndex = _spectrumStyle;

                            if (EqSpectrumGrid != null) EqSpectrumGrid.Visibility = _spectrumStyle == 0 ? Visibility.Visible : Visibility.Collapsed;
                            if (SpectrumLinePath != null) SpectrumLinePath.Visibility = _spectrumStyle == 1 ? Visibility.Visible : Visibility.Collapsed;
                            if (SpectrumWavePath != null) SpectrumWavePath.Visibility = _spectrumStyle == 2 ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }

                    if (lines.Length >= 15 && OSBlurToggle != null)
                    {
                        if (bool.TryParse(lines[14], out bool isBlur))
                        {
                            _isOSBlurEnabled = isBlur;
                            OSBlurToggle.IsChecked = isBlur;
                        }
                    }

                    if (lines.Length >= 16 && MonoStereoToggle != null)
                    {
                        if (bool.TryParse(lines[15], out bool isStereo))
                        {
                            _isStereoEnabled = isStereo;
                            MonoStereoToggle.IsChecked = isStereo;
                        }
                    }

                    if (lines.Length >= 17 && PanFader != null)
                    {
                        if (double.TryParse(lines[16], out double pan))
                        {
                            _panValue = pan;
                            PanFader.Value = pan;
                        }
                    }

                    SetModeColors(_isDarkMode);
                    SyncThemeVariablesToTarget();
                }
            }
            catch { }
        }

        private string? GetThemeNameFromColor(Color c)
        {
            if (ColorsAreClose(c, Color.FromRgb(92, 97, 255))) return "Blue (Default)";
            if (ColorsAreClose(c, Color.FromRgb(255, 71, 87))) return "Red";
            if (ColorsAreClose(c, Color.FromRgb(46, 204, 113))) return "Green";
            if (ColorsAreClose(c, Color.FromRgb(255, 159, 67))) return "Orange";
            if (ColorsAreClose(c, Color.FromRgb(155, 89, 182))) return "Purple";
            if (ColorsAreClose(c, Color.FromRgb(0, 210, 211))) return "Cyan";
            return null;
        }

        private void AlwaysOnTopToggle_Click(object? sender, RoutedEventArgs e)
        {
            bool keepOnTop = AlwaysOnTopToggle.IsChecked ?? false;
            this.Topmost = false;

            if (keepOnTop)
            {
                this.Topmost = true;
                this.Activate();
            }
        }

        private void StartWithWindowsToggle_Click(object? sender, RoutedEventArgs e)
        {
            bool enableStart = StartWithWindowsToggle.IsChecked ?? false;
            SetStartWithWindows(enableStart);
        }

        private void SetStartWithWindows(bool enable)
        {
            try
            {
                string runKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string? appPath = Environment.ProcessPath;
                            if (!string.IsNullOrEmpty(appPath))
                            {
                                key.SetValue("EqualizerPro", appPath);
                            }
                        }
                        else
                        {
                            key.DeleteValue("EqualizerPro", false);
                        }
                    }
                }
            }
            catch { }
        }

        private void VolumeSliderControl_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUpdatingVolumeUI && IsLoaded)
            {
                _isUpdatingVolumeUI = true;
                SystemVolumeManager.SetVolume(e.NewValue);

                if (sender == VolumeSliderControl && CompactVolumeSlider != null) CompactVolumeSlider.Value = e.NewValue;
                if (sender == CompactVolumeSlider && VolumeSliderControl != null) VolumeSliderControl.Value = e.NewValue;

                _isUpdatingVolumeUI = false;
            }
        }

        private void InitializePresets()
        {
            _eqPresets.Add("Custom", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            _eqPresets.Add("Acoustic", new double[] { 5, 5, 4, 1, 1, 1, 3, 4, 3, 2 });
            _eqPresets.Add("Bass Boost", new double[] { 9, 7, 4, 2, 0, 0, 0, 0, 0, 0 });
            _eqPresets.Add("Classical", new double[] { 4.5, 3.5, 3.0, 2.0, -1.0, -1.0, 0.0, 2.5, 3.5, 4.0 });
            _eqPresets.Add("Dance", new double[] { 8, 6, 2, 0, -2, -2, 0, 2, 4, 6 });
            _eqPresets.Add("Electronic", new double[] { 6, 5, 0, -2, -1, 2, 4, 6, 5, 3 });
            _eqPresets.Add("Hip-Hop", new double[] { 4.5, 3.5, 1.0, 3.0, -1.0, -1.0, 1.0, -1.0, 2.0, 3.0 });
            _eqPresets.Add("Jazz", new double[] { 3.5, 2.5, 1.0, 2.0, -1.0, -1.0, 0.0, 1.5, 2.5, 3.5 });
            _eqPresets.Add("Pop", new double[] { -1.5, 2.0, 3.5, 4.0, 2.0, -1.0, -2.0, 1.5, 2.5, 3.0 });
            _eqPresets.Add("R&B", new double[] { 3.0, 6.0, 4.0, 1.0, -1.0, -1.0, 2.0, 3.0, 2.5, 3.0 });
            _eqPresets.Add("Rock", new double[] { 6, 4, 2, -1, -2, -1, 2, 4, 5, 6 });
            _eqPresets.Add("Treble Boost", new double[] { -3.0, -3.0, -3.0, -3.0, -1.5, 2.0, 4.5, 7.0, 9.0, 10.0 });
            _eqPresets.Add("Vocal", new double[] { -2, -1, 1, 4, 6, 6, 5, 2, 0, -1 });
        }

        private void GlobalEqToggle_Click(object? sender, RoutedEventArgs e)
        {
            _isEqEnabled = GlobalEqToggle.IsChecked ?? false;

            if (CompactEqToggle != null) CompactEqToggle.IsChecked = _isEqEnabled;

            if (SlidersContainerGrid != null) SlidersContainerGrid.Opacity = _isEqEnabled ? 1.0 : 0.4;
            ApplyEqToAudioStream();
        }

        private void CompactEqToggle_Click(object? sender, RoutedEventArgs e)
        {
            _isEqEnabled = CompactEqToggle?.IsChecked ?? false;

            if (GlobalEqToggle != null) GlobalEqToggle.IsChecked = _isEqEnabled;

            if (SlidersContainerGrid != null) SlidersContainerGrid.Opacity = _isEqEnabled ? 1.0 : 0.4;
            ApplyEqToAudioStream();
        }

        private void SavePresetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SavePresetOverlay == null || SavePresetScale == null) return;
            SavePresetOverlay.Visibility = Visibility.Visible;
            PresetNameInput.Text = "";
            PresetNameInput.Focus();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            SavePresetOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var popIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            SavePresetScale.BeginAnimation(ScaleTransform.ScaleXProperty, popIn);
            SavePresetScale.BeginAnimation(ScaleTransform.ScaleYProperty, popIn);
        }

        private void CloseSavePreset_Click(object? sender, RoutedEventArgs e)
        {
            if (SavePresetOverlay == null) return;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => SavePresetOverlay.Visibility = Visibility.Collapsed;
            SavePresetOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void ConfirmSavePreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = PresetNameInput.Text.Trim();
            if (string.IsNullOrEmpty(presetName))
            {
                MessageBox.Show("Please enter a preset name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var defaultPresets = new HashSet<string> { "Custom", "Acoustic", "Bass Boost", "Classical", "Dance", "Electronic", "Hip-Hop", "Jazz", "Pop", "R&B", "Rock", "Treble Boost", "Vocal" };
            if (defaultPresets.Contains(presetName))
            {
                MessageBox.Show("Cannot overwrite default presets. Please choose a different name.", "Reserved Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CloseSavePreset_Click(sender, null!);

            double[] currentValues = new double[10];
            for (int i = 0; i < 10; i++)
            {
                currentValues[i] = _eqSliders[i].Value;
            }

            if (_eqPresets.ContainsKey(presetName))
            {
                _eqPresets[presetName] = currentValues;
                SelectPreset(presetName);
            }
            else
            {
                _eqPresets.Add(presetName, currentValues);
                AddCustomPresetToUI(presetName);
                SelectPreset(presetName);
            }

            UpdateEmptyCustomText();
            SaveCustomPresets();
            ShowToast($"Preset '{presetName}' Saved!");
        }

        private async void ShowToast(string message)
        {
            if (ToastNotification == null || ToastText == null) return;

            ToastText.Text = message;
            ToastNotification.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            await Task.Delay(2000);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, ev) => ToastNotification.Visibility = Visibility.Collapsed;
            ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void EqSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUpdatingPreset && PresetDropdownToggle != null)
            {
                _isUpdatingPreset = true;
                PresetDropdownToggle.Content = "Custom";
                if (_isEqEnabled) ApplyEqToAudioStream();
                _isUpdatingPreset = false;
            }
        }

        private void ApplyEqToAudioStream()
        {
            try
            {
                string apoPath = @"C:\Program Files\EqualizerAPO\config\config.txt";

                if (File.Exists(apoPath))
                {
                    string eqCommand = "";

                    if (_isEqEnabled)
                    {
                        double preampComp = 0.0;
                        string extraFilters = "";

                        // 1. Core Effects
                        if (_isFatModeEnabled)
                        {
                            preampComp -= 3.5;
                            extraFilters += $"Filter: ON LS Fc 120 Hz Gain 4.5 dB Q 0.7\n";
                            extraFilters += $"Filter: ON PK Fc 2500 Hz Gain 2.0 dB Q 1.0\n";
                        }

                        if (_isSuperBassEnabled)
                        {
                            preampComp -= 5.0;
                            extraFilters += $"Filter: ON LS Fc 70 Hz Gain 7.0 dB Q 0.8\n";
                            extraFilters += $"Filter: ON HP Fc 25 Hz\n";
                        }

                        if (_isSpatialSoundEnabled)
                        {
                            extraFilters += "Copy: L_TMP=L R_TMP=R\n";
                            extraFilters += "Copy: L=L_TMP+-0.4*R_TMP R=R_TMP+-0.4*L_TMP\n";
                            extraFilters += "Preamp: 2.5 dB\n";
                            extraFilters += "Filter: ON PK Fc 6000 Hz Gain 2.0 dB Q 1.0\n";
                        }

                        // 2. Mono / Panning (Executed last to manipulate final left/right channels)
                        if (!_isStereoEnabled)
                        {
                            // Mix to mono safely (0.5 to prevent clipping when summing channels)
                            extraFilters += "Copy: L_TMP=L R_TMP=R\n";
                            extraFilters += "Copy: L=0.5*L_TMP+0.5*R_TMP R=0.5*L_TMP+0.5*R_TMP\n";
                        }

                        if (Math.Abs(_panValue) > 0.1)
                        {
                            double lMult = _panValue < 0 ? 1.0 : 1.0 - (_panValue / 100.0);
                            double rMult = _panValue > 0 ? 1.0 : 1.0 + (_panValue / 100.0);
                            extraFilters += $"Copy: L={lMult:0.000}*L R={rMult:0.000}*R\n";
                        }

                        // Build the final script
                        eqCommand += $"Preamp: {preampComp:0.0} dB\n";
                        eqCommand += extraFilters;

                        eqCommand += $"GraphicEQ: 31 {_eqSliders[0].Value:0.0}; 62 {_eqSliders[1].Value:0.0}; 125 {_eqSliders[2].Value:0.0}; 250 {_eqSliders[3].Value:0.0}; 500 {_eqSliders[4].Value:0.0}; 1000 {_eqSliders[5].Value:0.0}; 2000 {_eqSliders[6].Value:0.0}; 4000 {_eqSliders[7].Value:0.0}; 8000 {_eqSliders[8].Value:0.0}; 16000 {_eqSliders[9].Value:0.0}";
                    }
                    else
                    {
                        eqCommand += "GraphicEQ: 31 0.0; 62 0.0; 125 0.0; 250 0.0; 500 0.0; 1000 0.0; 2000 0.0; 4000 0.0; 8000 0.0; 16000 0.0";
                    }

                    File.WriteAllText(apoPath, eqCommand);
                }
            }
            catch { }
        }

        private void TriggerWaveTransition(Point origin, Action themeChangeAction)
        {
            if (WindowBorder == null || ThemeTransitionOverlay == null) return;

            var rtb = new RenderTargetBitmap((int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(WindowBorder);
            ThemeTransitionOverlay.Source = rtb;
            ThemeTransitionOverlay.Visibility = Visibility.Visible;

            EllipseGeometry holeGeometry = new EllipseGeometry(origin, 0, 0);
            RectangleGeometry fullScreen = new RectangleGeometry(new Rect(0, 0, this.ActualWidth, this.ActualHeight));
            GeometryGroup clipGroup = new GeometryGroup { FillRule = FillRule.EvenOdd };
            clipGroup.Children.Add(fullScreen);
            clipGroup.Children.Add(holeGeometry);
            ThemeTransitionOverlay.Clip = clipGroup;

            themeChangeAction();

            double maxRadius = Math.Max(this.ActualWidth, this.ActualHeight) * 1.5;
            var anim = new DoubleAnimation(0, maxRadius, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            };

            anim.Completed += (s, a) => {
                ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
                ThemeTransitionOverlay.Source = null;
                ThemeTransitionOverlay.Clip = null;
            };

            holeGeometry.BeginAnimation(EllipseGeometry.RadiusXProperty, anim);
            holeGeometry.BeginAnimation(EllipseGeometry.RadiusYProperty, anim);
        }

        private void CustomColorBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (ColorPickerPopup != null)
            {
                ColorPickerPopup.IsOpen = true;

                _isUpdatingColorPicker = true;
                if (RedSlider != null) RedSlider.Value = _targetAccent.R;
                if (GreenSlider != null) GreenSlider.Value = _targetAccent.G;
                if (BlueSlider != null) BlueSlider.Value = _targetAccent.B;

                if (HexInputBox != null)
                    HexInputBox.Text = $"#{_targetAccent.R:X2}{_targetAccent.G:X2}{_targetAccent.B:X2}";

                UpdateColorPreview();
                _isUpdatingColorPicker = false;
            }
        }

        private void ColorSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RedSlider == null || GreenSlider == null || BlueSlider == null || HexInputBox == null) return;

            UpdateColorPreview();

            if (!_isUpdatingColorPicker)
            {
                _isUpdatingColorPicker = true;
                byte r = (byte)RedSlider.Value;
                byte g = (byte)GreenSlider.Value;
                byte b = (byte)BlueSlider.Value;
                HexInputBox.Text = $"#{r:X2}{g:X2}{b:X2}";
                _isUpdatingColorPicker = false;
            }
        }

        private void HexInputBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isUpdatingColorPicker || !IsLoaded || RedSlider == null) return;

            try
            {
                string hex = HexInputBox.Text.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;

                if (hex.Length == 7 || hex.Length == 9)
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);

                    _isUpdatingColorPicker = true;
                    RedSlider.Value = color.R;
                    GreenSlider.Value = color.G;
                    BlueSlider.Value = color.B;
                    UpdateColorPreview();
                    _isUpdatingColorPicker = false;
                }
            }
            catch { }
        }

        private void UpdateColorPreview()
        {
            if (ColorPreviewBox != null && RedSlider != null && GreenSlider != null && BlueSlider != null)
            {
                byte r = (byte)RedSlider.Value;
                byte g = (byte)GreenSlider.Value;
                byte b = (byte)BlueSlider.Value;
                ColorPreviewBox.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void ApplyCustomColor_Click(object? sender, RoutedEventArgs e)
        {
            if (ColorPickerPopup != null) ColorPickerPopup.IsOpen = false;

            byte r = (byte)(RedSlider?.Value ?? 92);
            byte g = (byte)(GreenSlider?.Value ?? 97);
            byte b = (byte)(BlueSlider?.Value ?? 255);

            if (ThemeSelector != null)
            {
                ThemeSelector.SelectionChanged -= ThemeSelector_ThemeChanged;
                ThemeSelector.SelectedIndex = -1;
                ThemeSelector.SelectionChanged += ThemeSelector_ThemeChanged;
            }

            Point origin = CustomColorBtn.TransformToAncestor(this).Transform(new Point(CustomColorBtn.ActualWidth / 2, CustomColorBtn.ActualHeight / 2));

            TriggerWaveTransition(origin, () =>
            {
                _targetAccent = Color.FromRgb(r, g, b);
                SyncThemeVariablesToTarget();
                PushColorsToUI();
            });
        }

        private void SetModeColors(bool isDark)
        {
            byte alpha = _isOSBlurEnabled ? (byte)153 : (byte)240;
            if (isDark)
            {
                _tarWindowBg = Color.FromArgb(alpha, 11, 14, 20);
                _tarPanelBg = Color.FromArgb(115, 11, 14, 20);
                _tarText = Colors.White;
                _tarMutedText = Color.FromRgb(169, 177, 214);
                _tarBorder = Color.FromArgb(38, 255, 255, 255);
                _tarOverlay = Color.FromArgb(26, 255, 255, 255);
                _tarHover = Color.FromArgb(51, 255, 255, 255);
            }
            else
            {
                _tarWindowBg = Color.FromArgb(alpha, 240, 244, 248);
                _tarPanelBg = Color.FromArgb(170, 255, 255, 255);
                _tarText = Color.FromRgb(10, 15, 25);
                _tarMutedText = Color.FromRgb(85, 95, 115);
                _tarBorder = Color.FromArgb(50, 0, 0, 0);
                _tarOverlay = Color.FromArgb(20, 0, 0, 0);
                _tarHover = Color.FromArgb(30, 0, 0, 0);
            }
        }

        private void SyncThemeVariablesToTarget()
        {
            _curWindowBg = _tarWindowBg;
            _curPanelBg = _tarPanelBg;
            _curText = _tarText;
            _curMutedText = _tarMutedText;
            _curBorder = _tarBorder;
            _curOverlay = _tarOverlay;
            _curHover = _tarHover;
            _currentAccent = _targetAccent;
        }

        private void ModeSwitchBtn_Click(object? sender, RoutedEventArgs e)
        {
            Point origin = ModeSwitchBtn.TransformToAncestor(this).Transform(new Point(ModeSwitchBtn.ActualWidth / 2, ModeSwitchBtn.ActualHeight / 2));

            TriggerWaveTransition(origin, () =>
            {
                _isDarkMode = !_isDarkMode;
                ModeSwitchBtn.Content = _isDarkMode ? "☀" : "🌙";
                SetModeColors(_isDarkMode);
                SyncThemeVariablesToTarget();
                PushColorsToUI();
            });
        }

        private void ThemeSelector_ThemeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || ThemeSelector == null || !IsLoaded) return;
            var selectedItem = ThemeSelector.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string themeName = selectedItem.Content?.ToString() ?? "";
            Point origin = ThemeSelector.TransformToAncestor(this).Transform(new Point(ThemeSelector.ActualWidth / 2, ThemeSelector.ActualHeight / 2));

            TriggerWaveTransition(origin, () =>
            {
                switch (themeName)
                {
                    case "Red": _targetAccent = Color.FromRgb(255, 71, 87); break;
                    case "Green": _targetAccent = Color.FromRgb(46, 204, 113); break;
                    case "Orange": _targetAccent = Color.FromRgb(255, 159, 67); break;
                    case "Purple": _targetAccent = Color.FromRgb(155, 89, 182); break;
                    case "Cyan": _targetAccent = Color.FromRgb(0, 210, 211); break;
                    case "Blue (Default)":
                    default: _targetAccent = Color.FromRgb(92, 97, 255); break;
                }
                SyncThemeVariablesToTarget();
                PushColorsToUI();
            });
        }

        private Color LerpColor(Color c1, Color c2, double amount)
        {
            byte a = (byte)(c1.A + (c2.A - c1.A) * amount);
            byte r = (byte)(c1.R + (c2.R - c1.R) * amount);
            byte g = (byte)(c1.G + (c2.G - c1.G) * amount);
            byte b = (byte)(c1.B + (c2.B - c1.B) * amount);
            return Color.FromArgb(a, r, g, b);
        }

        private bool ColorsAreClose(Color c1, Color c2)
        {
            return Math.Abs(c1.A - c2.A) < 2 && Math.Abs(c1.R - c2.R) < 2 && Math.Abs(c1.G - c2.G) < 2 && Math.Abs(c1.B - c2.B) < 2;
        }

        private void PushColorsToUI()
        {
            try
            {
                UpdateOSBlur();

                var windowGradient = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0),
                    GradientOrigin = new Point(0.5, 0),
                    RadiusX = 1.5,
                    RadiusY = 1.2
                };

                byte highlightAlpha = _isOSBlurEnabled ? (byte)80 : (byte)140;
                windowGradient.GradientStops.Add(new GradientStop(Color.FromArgb(highlightAlpha, _currentAccent.R, _currentAccent.G, _currentAccent.B), 0.0));
                windowGradient.GradientStops.Add(new GradientStop(_curWindowBg, 0.6));
                windowGradient.Freeze();
                this.Resources["WindowBgBrush"] = windowGradient;

                var panelGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                panelGradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(_curPanelBg.A), _curPanelBg.R, _curPanelBg.G, _curPanelBg.B), 0.0));
                panelGradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(_curPanelBg.A / 2), _curPanelBg.R, _curPanelBg.G, _curPanelBg.B), 1.0));
                panelGradient.Freeze();
                this.Resources["PanelBgBrush"] = panelGradient;

                var borderGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                borderGradient.GradientStops.Add(new GradientStop(_curBorder, 0.0));
                borderGradient.GradientStops.Add(new GradientStop(Color.FromArgb(5, _curBorder.R, _curBorder.G, _curBorder.B), 1.0));
                borderGradient.Freeze();
                this.Resources["BorderBrush"] = borderGradient;

                this.Resources["TextBrush"] = new SolidColorBrush(_curText) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["MutedTextBrush"] = new SolidColorBrush(_curMutedText) { Opacity = 1 }.GetCurrentValueAsFrozen();

                var overlayGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                overlayGradient.GradientStops.Add(new GradientStop(_curOverlay, 0.0));
                overlayGradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(_curOverlay.A / 3), _curOverlay.R, _curOverlay.G, _curOverlay.B), 1.0));
                overlayGradient.Freeze();
                this.Resources["GlassOverlayBrush"] = overlayGradient;

                this.Resources["HoverBrush"] = new SolidColorBrush(_curHover) { Opacity = 1 }.GetCurrentValueAsFrozen();

                Color lightAccent = Color.FromArgb(255,
                    (byte)Math.Min(255, _currentAccent.R + 60),
                    (byte)Math.Min(255, _currentAccent.G + 60),
                    (byte)Math.Min(255, _currentAccent.B + 70));
                Color quarterAccent = Color.FromArgb(64, _currentAccent.R, _currentAccent.G, _currentAccent.B);

                this.Resources["AccentColor"] = _currentAccent;
                this.Resources["AccentColorLight"] = lightAccent;
                this.Resources["AccentBrush"] = new SolidColorBrush(_currentAccent) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["AccentBrushLight"] = new SolidColorBrush(lightAccent) { Opacity = 1 }.GetCurrentValueAsFrozen();

                var spectrumGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                spectrumGradient.GradientStops.Add(new GradientStop(lightAccent, 0.0));
                spectrumGradient.GradientStops.Add(new GradientStop(_currentAccent, 0.5));
                spectrumGradient.GradientStops.Add(new GradientStop(_curOverlay, 1.0));
                spectrumGradient.Freeze();
                this.Resources["SpectrumGradientBrush"] = spectrumGradient;

                var freqFillGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                freqFillGradient.GradientStops.Add(new GradientStop(lightAccent, 0.0));
                freqFillGradient.GradientStops.Add(new GradientStop(quarterAccent, 0.6));
                freqFillGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, _curWindowBg.R, _curWindowBg.G, _curWindowBg.B), 1.0));
                freqFillGradient.Freeze();
                this.Resources["FreqFillGradientBrush"] = freqFillGradient;
            }
            catch { }
        }

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private void UpdateOSBlur()
        {
            try
            {
                var windowHelper = new WindowInteropHelper(this);
                if (windowHelper.Handle == IntPtr.Zero) return;

                var accent = new AccentPolicy();

                if (_isOSBlurEnabled)
                {
                    accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    byte a = 120;
                    byte r = _curWindowBg.R;
                    byte g = _curWindowBg.G;
                    byte b = _curWindowBg.B;
                    uint abgr = (uint)((a << 24) | (b << 16) | (g << 8) | r);
                    accent.GradientColor = abgr;
                }
                else
                {
                    accent.AccentState = AccentState.ACCENT_DISABLED;
                }

                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch { }
        }

        private void VisualizerTimer_Tick(object? sender, EventArgs e)
        {
            float rawPeak = _isEcoModeEnabled ? 0f : SystemVolumeManager.GetPeakValue();

            double volumeFraction = VolumeSliderControl != null ? (VolumeSliderControl.Value / 100.0) : 1.0;
            float currentPeak = (float)(rawPeak * volumeFraction);

            bool isPlayingState = _currentSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            bool isActuallyPlayingAudio = !_isEcoModeEnabled && isPlayingState && currentPeak > 0.001f;

            DrawVectorscope(isActuallyPlayingAudio);

            if (EqSpectrumGrid != null)
            {
                for (int i = 0; i < 48; i++)
                {
                    if (isActuallyPlayingAudio)
                    {
                        if (_rand.NextDouble() < 0.3)
                        {
                            double bellCurve = Math.Sin((i / 47.0) * Math.PI);
                            int maxHeight = (int)((bellCurve * 50 * currentPeak * 3) + (_rand.Next(10, 30) * currentPeak * 2));
                            _spectrumTargets[i] = _rand.Next(5, maxHeight + 5);
                        }
                    }
                    else
                    {
                        _spectrumTargets[i] = 2;
                    }

                    if (_spectrumTargets[i] > _spectrumCurrents[i])
                    {
                        _spectrumCurrents[i] += (_spectrumTargets[i] - _spectrumCurrents[i]) * 0.4;
                    }
                    else
                    {
                        _spectrumCurrents[i] -= _spectrumFalloffDropRate;
                        if (_spectrumCurrents[i] < _spectrumTargets[i]) _spectrumCurrents[i] = _spectrumTargets[i];
                    }

                    if (_spectrumCurrents[i] < 2) _spectrumCurrents[i] = 2;

                    if (_spectrumStyle == 0 && EqSpectrumGrid.Children.Count > i && EqSpectrumGrid.Children[i] is Border border)
                    {
                        if (Math.Abs(border.Height - _spectrumCurrents[i]) > 0.5)
                        {
                            border.Height = _spectrumCurrents[i];
                        }
                    }
                }

                if (_spectrumStyle == 1 || _spectrumStyle == 2)
                {
                    Point[] pts = new Point[48];
                    for (int i = 0; i < 48; i++)
                    {
                        pts[i] = new Point(i, 80 - _spectrumCurrents[i]);
                    }

                    if (_spectrumStyle == 1 && SpectrumLinePath != null)
                    {
                        var specLineGeom = CreateSmoothCurve(pts, false);
                        SpectrumLinePath.Data = specLineGeom;
                        if (SpectrumLinePathGlow != null) SpectrumLinePathGlow.Data = specLineGeom;
                    }
                    else if (_spectrumStyle == 2 && SpectrumWavePath != null)
                    {
                        SpectrumWavePath.Data = CreateSmoothCurve(pts, true, new Point(47, 80), new Point(0, 80));
                    }
                }
            }

            if (EqLeftVuTrack != null && EqRightVuTrack != null)
            {
                if (isActuallyPlayingAudio)
                {
                    _leftDbTarget = Math.Max(0, Math.Min(1, currentPeak + (_rand.NextDouble() * 0.02 - 0.01)));
                    _rightDbTarget = Math.Max(0, Math.Min(1, currentPeak + (_rand.NextDouble() * 0.02 - 0.01)));
                }
                else
                {
                    _leftDbTarget = 0;
                    _rightDbTarget = 0;
                }

                _leftDbCurrent += (_leftDbTarget - _leftDbCurrent) * 0.2;
                _rightDbCurrent += (_rightDbTarget - _rightDbCurrent) * 0.2;

                if (_leftDbCurrent > _leftPeak) _leftPeak = _leftDbCurrent;
                else _leftPeak = Math.Max(0, _leftPeak - 0.01);

                if (_rightDbCurrent > _rightPeak) _rightPeak = _rightDbCurrent;
                else _rightPeak = Math.Max(0, _rightPeak - 0.01);

                double trackHeight = EqLeftVuTrack.ActualHeight > 0 ? EqLeftVuTrack.ActualHeight : 80;

                EqLeftVuFill.Height = Math.Max(0, _leftDbCurrent * trackHeight);
                EqRightVuFill.Height = Math.Max(0, _rightDbCurrent * trackHeight);

                EqLeftVuPeak.Margin = new Thickness(0, 0, 0, Math.Max(0, _leftPeak * trackHeight));
                EqRightVuPeak.Margin = new Thickness(0, 0, 0, Math.Max(0, _rightPeak * trackHeight));

                if (EqLeftDbText != null && EqRightDbText != null)
                {
                    double leftDbVal = _leftDbCurrent > 0.001 ? 20 * Math.Log10(_leftDbCurrent) : -60.0;
                    double rightDbVal = _rightDbCurrent > 0.001 ? 20 * Math.Log10(_rightDbCurrent) : -60.0;

                    EqLeftDbText.Text = leftDbVal <= -59.0 ? "-inf" : leftDbVal.ToString("0.0");
                    KeepTextCentered(leftDbVal, rightDbVal);
                }
            }

            // Phase Correlation Calculation
            if (isActuallyPlayingAudio)
            {
                double targetCorrelation = 0;
                if (!_isStereoEnabled)
                {
                    targetCorrelation = 0.95 + (_rand.NextDouble() * 0.05); // Mono is +1
                }
                else
                {
                    double phaseDiff = Math.Abs(_leftDbCurrent - _rightDbCurrent);
                    targetCorrelation = 0.7 - (phaseDiff * 2.0) - (_rand.NextDouble() * 0.3);

                    // Force negative for phase cancellation
                    if (_isSpatialSoundEnabled) targetCorrelation -= 0.8;
                    if (_panValue != 0) targetCorrelation -= (Math.Abs(_panValue) / 100.0) * 0.5;
                }

                if (targetCorrelation > 1) targetCorrelation = 1;
                if (targetCorrelation < -1) targetCorrelation = -1;

                _currentCorrelation += (targetCorrelation - _currentCorrelation) * 0.1;
            }
            else
            {
                _currentCorrelation += (0 - _currentCorrelation) * 0.1;
            }

            if (CorrelationTrack != null && CorrelationTransform != null)
            {
                double halfHeight = Math.Max(0, CorrelationTrack.ActualHeight / 2.0 - 2);
                // Multiply by -1 so positive (+1) goes UP and negative (-1) goes DOWN.
                CorrelationTransform.Y = -_currentCorrelation * halfHeight;
            }

            if (isPlayingState && DiskRotation != null)
            {
                DiskRotation.Angle = (DiskRotation.Angle + 0.5) % 360;
            }
        }

        private void DrawVectorscope(bool isPlaying)
        {
            if (VectorscopePath == null) return;

            int pointsCount = 60;
            Point[] pts = new Point[pointsCount + 2];
            double cx = 100;
            double cy = 100;

            pts[0] = new Point(cx, cy);

            if (!isPlaying || (_leftDbCurrent < 0.001 && _rightDbCurrent < 0.001))
            {
                bool allZero = true;
                for (int i = 0; i < pointsCount; i++)
                {
                    _vectorCurrents[i] -= 5.0;
                    if (_vectorCurrents[i] <= 0) _vectorCurrents[i] = 0;
                    else allZero = false;

                    double t = (i / (double)(pointsCount - 1)) * Math.PI - (Math.PI / 2);
                    double x = cx + Math.Sin(t) * _vectorCurrents[i];
                    double y = cy - Math.Cos(t) * _vectorCurrents[i];
                    pts[i + 1] = new Point(x, y);
                }
                pts[pointsCount + 1] = new Point(cx, cy);

                if (allZero)
                {
                    VectorscopePath.Data = null;
                    return;
                }
            }
            else
            {
                // VOLUME INDEPENDENCE COMPRESSION
                double vLeft = Math.Pow(Math.Max(0, _leftDbCurrent), 0.35);
                double vRight = Math.Pow(Math.Max(0, _rightDbCurrent), 0.35);
                double midDb = (vLeft + vRight) / 2.0;

                double time = DateTime.Now.TimeOfDay.TotalMilliseconds / 150.0;

                for (int i = 0; i < pointsCount; i++)
                {
                    double t = (i / (double)(pointsCount - 1)) * Math.PI - (Math.PI / 2);
                    double targetSignal = 0;

                    if (!_isStereoEnabled)
                    {
                        double angDist = Math.Abs(t);
                        double falloff = Math.Pow(Math.Max(0, 1.0 - (angDist / 0.15)), 3);
                        targetSignal = midDb * falloff * 95.0;

                        if (targetSignal > 5) targetSignal *= (_rand.NextDouble() * 0.2 + 0.9);
                    }
                    else
                    {
                        double lw = Math.Pow(Math.Max(0, -Math.Sin(t)), 4);
                        double rw = Math.Pow(Math.Max(0, Math.Sin(t)), 4);
                        double mw = Math.Pow(Math.Max(0, Math.Cos(t)), 8);

                        double noise = _rand.NextDouble() * 0.6 + 0.4;

                        double lComp = lw * vLeft;
                        double rComp = rw * vRight;
                        double mComp = mw * midDb;

                        targetSignal = (lComp + rComp + mComp) * noise * 95.0;

                        if (_isSpatialSoundEnabled) targetSignal *= 1.2;

                        if (midDb > 0.02)
                        {
                            targetSignal = Math.Max(targetSignal, midDb * 12.0 * _rand.NextDouble());
                        }
                    }

                    if (targetSignal > 100) targetSignal = 100;
                    _vectorTargets[i] = targetSignal;

                    if (_vectorTargets[i] > _vectorCurrents[i])
                        _vectorCurrents[i] += (_vectorTargets[i] - _vectorCurrents[i]) * 0.6;
                    else
                        _vectorCurrents[i] -= 4.0;

                    if (_vectorCurrents[i] < 0) _vectorCurrents[i] = 0;

                    double x = cx + Math.Sin(t) * _vectorCurrents[i];
                    double y = cy - Math.Cos(t) * _vectorCurrents[i];
                    pts[i + 1] = new Point(x, y);
                }
                pts[pointsCount + 1] = new Point(cx, cy);
            }

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                ctx.PolyLineTo(pts, true, false);
            }
            geometry.Freeze();

            VectorscopePath.Data = geometry;
        }

        private void KeepTextCentered(double l, double r)
        {
            if (EqLeftDbText != null) EqLeftDbText.Text = l <= -59.0 ? "-inf" : l.ToString("0.0");
            if (EqRightDbText != null) EqRightDbText.Text = r <= -59.0 ? "-inf" : r.ToString("0.0");
        }

        private Geometry CreateSmoothCurve(Point[] points, bool isClosed, Point bottomRight = default, Point bottomLeft = default)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                if (points.Length > 0)
                {
                    ctx.BeginFigure(points[0], false, isClosed);
                    if (points.Length > 1)
                    {
                        double tension = 0.3;
                        List<Point> bezierPoints = new List<Point>((points.Length - 1) * 3);
                        for (int i = 0; i < points.Length - 1; i++)
                        {
                            Point p0 = i == 0 ? points[0] : points[i - 1];
                            Point p1 = points[i];
                            Point p2 = points[i + 1];
                            Point p3 = i + 2 >= points.Length ? points[i + 1] : points[i + 2];

                            bezierPoints.Add(new Point(p1.X + (p2.X - p0.X) * tension, p1.Y + (p2.Y - p0.Y) * tension));
                            bezierPoints.Add(new Point(p2.X - (p3.X - p1.X) * tension, p2.Y - (p3.Y - p1.Y) * tension));
                            bezierPoints.Add(p2);
                        }
                        ctx.PolyBezierTo(bezierPoints, true, true);
                    }

                    if (isClosed)
                    {
                        ctx.LineTo(bottomRight, true, true);
                        ctx.LineTo(bottomLeft, true, true);
                    }
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private string GetFriendlyAppName(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) return "System";

            string lowerId = rawId.ToLowerInvariant();

            if (lowerId.Contains("spotify")) return "Spotify";
            if (lowerId.Contains("chrome")) return "Google Chrome";
            if (lowerId.Contains("msedge")) return "Microsoft Edge";
            if (lowerId.Contains("firefox")) return "Firefox";
            if (lowerId.Contains("brave")) return "Brave Browser";
            if (lowerId.Contains("opera")) return "Opera";
            if (lowerId.Contains("vlc")) return "VLC Media Player";
            if (lowerId.Contains("discord")) return "Discord";
            if (lowerId.Contains("wmplayer") || lowerId.Contains("windowsmedia")) return "Windows Media Player";
            if (lowerId.Contains("itunes")) return "iTunes";
            if (lowerId.Contains("music.ui")) return "Groove Music";
            if (lowerId.Contains("netflix")) return "Netflix";

            var parts = rawId.Split('!');
            string name = parts.Length > 1 ? parts[parts.Length - 1] : rawId;

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
                if (name.Length > 0)
                    name = char.ToUpper(name[0]) + name.Substring(1);
            }

            return name;
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => UpdateCurrentSession(sender.GetCurrentSession());

        private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }

            _currentSession = session;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;

                UpdateMediaProperties();
                UpdatePlaybackState();
                UpdateTimeline();
                _playbackTimer.Start();
            }
            else
            {
                _playbackTimer.Stop();
                Dispatcher.Invoke(() => {
                    if (PlaybackAppSourceText != null) PlaybackAppSourceText.Text = string.Empty;
                    if (TrackTitle != null) TrackTitle.Text = "Unknown Track";
                    if (TrackArtist != null) TrackArtist.Text = "Unknown Artist";
                    if (TrackImageBrush != null) TrackImageBrush.ImageSource = null;
                    if (TrackIcon != null) TrackIcon.Visibility = Visibility.Visible;

                    if (PlaybackBigImageBrush != null) PlaybackBigImageBrush.ImageSource = null;

                    if (CompactTrackImageBrush != null) CompactTrackImageBrush.ImageSource = null;
                    if (CompactTrackIcon != null) CompactTrackIcon.Visibility = Visibility.Visible;

                    if (CurrentTimeText != null) CurrentTimeText.Text = "0:00";
                    if (TotalTimeText != null) TotalTimeText.Text = "0:00";
                    if (SeekSlider != null) SeekSlider.Value = 0;
                    if (CompactSeekSlider != null) CompactSeekSlider.Value = 0;

                    if (PlayPauseIcon != null)
                    {
                        PlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                        PlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
                    }

                    if (PlaybackBigPlayPauseIcon != null)
                    {
                        PlaybackBigPlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                        PlaybackBigPlayPauseIcon.Margin = new Thickness(5, 0, 0, 0);
                    }

                    if (CompactPlayPauseIcon != null)
                    {
                        CompactPlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                        CompactPlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
                    }
                });
            }
        }

        private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => UpdateMediaProperties();
        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => UpdatePlaybackState();
        private void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => UpdateTimeline();
        private void PlaybackTimer_Tick(object? sender, EventArgs e) => UpdateTimeline();

        private async void UpdateMediaProperties()
        {
            if (_currentSession == null) return;
            try
            {
                var properties = await _currentSession.TryGetMediaPropertiesAsync();
                if (properties != null)
                {
                    string title = string.IsNullOrEmpty(properties.Title) ? "Unknown Track" : properties.Title;
                    string artist = string.IsNullOrEmpty(properties.Artist) ? "Unknown Artist" : properties.Artist;
                    string rawAppId = _currentSession.SourceAppUserModelId;
                    string friendlyAppName = GetFriendlyAppName(rawAppId);

                    BitmapImage? bitmapImage = null;

                    if (properties.Thumbnail != null)
                    {
                        try
                        {
                            using (var stream = await properties.Thumbnail.OpenReadAsync())
                            {
                                var dotNetStream = stream.AsStreamForRead();
                                bitmapImage = new BitmapImage();
                                bitmapImage.BeginInit();
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.StreamSource = dotNetStream;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();
                            }
                        }
                        catch { }
                    }

                    Dispatcher.Invoke(() => {
                        if (PlaybackAppSourceText != null) PlaybackAppSourceText.Text = $"Playing from {friendlyAppName}";
                        if (TrackTitle != null) TrackTitle.Text = title;
                        if (TrackArtist != null) TrackArtist.Text = artist;

                        if (bitmapImage != null)
                        {
                            if (TrackImageBrush != null) TrackImageBrush.ImageSource = bitmapImage;
                            if (TrackIcon != null) TrackIcon.Visibility = Visibility.Collapsed;

                            if (PlaybackBigImageBrush != null) PlaybackBigImageBrush.ImageSource = bitmapImage;

                            if (CompactTrackImageBrush != null) CompactTrackImageBrush.ImageSource = bitmapImage;
                            if (CompactTrackIcon != null) CompactTrackIcon.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            if (TrackImageBrush != null) TrackImageBrush.ImageSource = null;
                            if (TrackIcon != null) TrackIcon.Visibility = Visibility.Visible;

                            if (PlaybackBigImageBrush != null) PlaybackBigImageBrush.ImageSource = null;

                            if (CompactTrackImageBrush != null) CompactTrackImageBrush.ImageSource = null;
                            if (CompactTrackIcon != null) CompactTrackIcon.Visibility = Visibility.Visible;
                        }
                    });
                }
            }
            catch { }
        }

        private void UpdatePlaybackState()
        {
            if (_currentSession == null) return;
            var playbackInfo = _currentSession.GetPlaybackInfo();
            if (playbackInfo != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        if (PlayPauseIcon != null)
                        {
                            PlayPauseIcon.Data = Geometry.Parse(PauseIconData);
                            PlayPauseIcon.Margin = new Thickness(0);
                        }

                        if (PlaybackBigPlayPauseIcon != null)
                        {
                            PlaybackBigPlayPauseIcon.Data = Geometry.Parse(PauseIconData);
                            PlaybackBigPlayPauseIcon.Margin = new Thickness(0);
                        }

                        if (CompactPlayPauseIcon != null)
                        {
                            CompactPlayPauseIcon.Data = Geometry.Parse(PauseIconData);
                            CompactPlayPauseIcon.Margin = new Thickness(0);
                        }
                    }
                    else
                    {
                        if (PlayPauseIcon != null)
                        {
                            PlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                            PlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
                        }

                        if (PlaybackBigPlayPauseIcon != null)
                        {
                            PlaybackBigPlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                            PlaybackBigPlayPauseIcon.Margin = new Thickness(5, 0, 0, 0);
                        }

                        if (CompactPlayPauseIcon != null)
                        {
                            CompactPlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                            CompactPlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
                        }
                    }
                });
            }
        }

        private void UpdateTimeline()
        {
            if (_currentSession == null || _isDraggingSeekbar) return;

            var timeline = _currentSession.GetTimelineProperties();
            var playbackInfo = _currentSession.GetPlaybackInfo();

            if (timeline != null)
            {
                Dispatcher.Invoke(() =>
                {
                    TimeSpan currentPosition = timeline.Position;

                    if (playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                        currentPosition = currentPosition.Add(elapsed);
                    }

                    if (currentPosition > timeline.EndTime) currentPosition = timeline.EndTime;
                    if (currentPosition < TimeSpan.Zero) currentPosition = TimeSpan.Zero;

                    if (SeekSlider != null)
                    {
                        SeekSlider.Maximum = timeline.EndTime.TotalSeconds;
                        SeekSlider.Value = currentPosition.TotalSeconds;
                    }

                    if (CompactSeekSlider != null)
                    {
                        CompactSeekSlider.Maximum = timeline.EndTime.TotalSeconds;
                        CompactSeekSlider.Value = currentPosition.TotalSeconds;
                    }

                    if (CurrentTimeText != null) CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(currentPosition.TotalMinutes), currentPosition.Seconds);
                    if (TotalTimeText != null) TotalTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(timeline.EndTime.TotalMinutes), timeline.EndTime.Seconds);
                });
            }
        }

        private async void PlayPause_Click(object? sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (PlayPauseBorder != null && PlayPauseBorder.RenderTransform is ScaleTransform scaleTransform)
            {
                var popAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                };
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
            }

            if (PlaybackBigPlayPauseBorder != null && PlaybackBigPlayPauseBorder.RenderTransform is ScaleTransform bigScaleTransform)
            {
                var popAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                };
                bigScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                bigScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
            }

            if (CompactPlayPauseBorder != null && CompactPlayPauseBorder.RenderTransform is ScaleTransform compactScaleTransform)
            {
                var popAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                };
                compactScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                compactScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
            }

            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
        }

        private async void Previous_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null) await _currentSession.TrySkipPreviousAsync();
        }

        private async void Next_Click(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null) await _currentSession.TrySkipNextAsync();
        }

        private void SeekSlider_DragStarted(object? sender, DragStartedEventArgs e) => _isDraggingSeekbar = true;

        private async void SeekSlider_DragCompleted(object? sender, DragCompletedEventArgs e)
        {
            if (_currentSession != null && SeekSlider != null && sender is Slider slider)
            {
                var newPosition = TimeSpan.FromSeconds(slider.Value);
                await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);
            }
            _isDraggingSeekbar = false;
        }

        private void SeekSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSeekbar && CurrentTimeText != null)
            {
                var tempPosition = TimeSpan.FromSeconds(e.NewValue);
                CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(tempPosition.TotalMinutes), tempPosition.Seconds);
            }
        }

        private async void SeekSlider_PreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (_currentSession != null && !_isDraggingSeekbar)
            {
                Slider? clickedSlider = sender as Slider;
                if (clickedSlider != null && CurrentTimeText != null)
                {
                    var newPosition = TimeSpan.FromSeconds(clickedSlider.Value);
                    await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);

                    CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(newPosition.TotalMinutes), newPosition.Seconds);
                }
            }
        }

        private void Window_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var helper = new WindowInteropHelper(this);
                SendMessage(helper.Handle, 0x112, (IntPtr)0xF008, IntPtr.Zero);
            }
        }

        private void Minimize_Click(object? sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayToggle != null && MinimizeToTrayToggle.IsChecked == true)
            {
                HideToTray();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void Maximize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AboutBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (AboutOverlay == null || AboutScale == null) return;
            AboutOverlay.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            AboutOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var popIn = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            AboutScale.BeginAnimation(ScaleTransform.ScaleXProperty, popIn);
            AboutScale.BeginAnimation(ScaleTransform.ScaleYProperty, popIn);
        }

        private void CloseAbout_Click(object? sender, RoutedEventArgs e)
        {
            if (AboutOverlay == null) return;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, ev) => AboutOverlay.Visibility = Visibility.Collapsed;
            AboutOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void MarqueeTimer_Tick(object? sender, EventArgs e)
        {
            ScrollText(TrackTitleScroller);
            ScrollText(TrackArtistScroller);
            ScrollText(PlaybackBigTitleScroller);
            ScrollText(PlaybackBigArtistScroller);

            if (AboutOverlay != null && AboutOverlay.Visibility == Visibility.Visible)
            {
                if (AboutRing1 != null) AboutRing1.Angle = (AboutRing1.Angle + 1) % 360;
                if (AboutRing2 != null) AboutRing2.Angle = (AboutRing2.Angle - 1.5) % 360;
            }
        }

        private void ScrollText(ScrollViewer? scroller)
        {
            if (scroller != null && scroller.ScrollableWidth > 0)
            {
                if (scroller.HorizontalOffset >= scroller.ScrollableWidth)
                {
                    scroller.ScrollToHorizontalOffset(0);
                }
                else
                {
                    scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + 1.0);
                }
            }
        }
    }

    public static class SystemVolumeManager
    {
        private static MMDevice? _defaultDevice;
        public static event Action<float>? OnVolumeChanged;

        public static void Initialize()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_defaultDevice != null)
                {
                    _defaultDevice.AudioEndpointVolume.OnVolumeNotification += (data) =>
                    {
                        OnVolumeChanged?.Invoke(data.MasterVolume);
                    };
                }
            }
            catch { }
        }

        public static float GetPeakValue()
        {
            try
            {
                if (_defaultDevice != null)
                {
                    return _defaultDevice.AudioMeterInformation.MasterPeakValue;
                }
            }
            catch { }
            return 0f;
        }

        public static void SetVolume(double volumeLevel)
        {
            try
            {
                if (_defaultDevice != null)
                {
                    _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volumeLevel / 100.0);
                }
            }
            catch { }
        }

        public static double GetVolume()
        {
            try
            {
                if (_defaultDevice != null)
                {
                    return _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0;
                }
            }
            catch { }
            return 0;
        }
    }

    public enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_GRADIENT = 1, ACCENT_ENABLE_TRANSPARENTGRADIENT = 2, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4, ACCENT_INVALID_STATE = 5 }
    [StructLayout(LayoutKind.Sequential)] public struct AccentPolicy { public AccentState AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }
    public enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
}