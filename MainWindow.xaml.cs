using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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

// --- AMBIGUITY FIXES ---
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
// -----------------------

namespace EqualizerPro
{
    public class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private DispatcherTimer _playbackTimer;
        private bool _isDraggingSeekbar = false;
        private bool _isUpdatingVolumeUI = false;

        // UI State Variables
        private bool _isCompactMode = false;
        private int _activePanel = 0; // 0 = Equalizer, 1 = Studio FX, 2 = Settings

        // System Tray variables
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _isForceClosing = false;

        // Audio Recording Variables
        private WasapiLoopbackCapture _loopbackCapture;
        private WaveFileWriter _waveWriter;
        private string _tempRecordPath;
        private DispatcherTimer _recordTimer;
        private TimeSpan _recordDuration;
        private bool _isRecording = false;

        // Equalizer Variables
        private Slider[] _eqSliders;
        private Dictionary<string, double[]> _eqPresets = new Dictionary<string, double[]>();
        private bool _isUpdatingPreset = false;
        private bool _isEqEnabled = true;

        // Fat Mode Variables
        private bool _isFatModeEnabled = false;

        // Visualizer & DB Meter Variables
        private DispatcherTimer _visualizerTimer;
        private double[] _freqTargets = new double[10];
        private double[] _freqCurrents = new double[10];
        private double[] _spectrumTargets = new double[48];
        private double[] _spectrumCurrents = new double[48];
        private double _spectrumFalloffDropRate = 2.0;
        private bool _isEcoModeEnabled = false;

        private double _leftDbTarget = 0;
        private double _leftDbCurrent = 0;
        private double _rightDbTarget = 0;
        private double _rightDbCurrent = 0;
        private double _leftPeak = 0;
        private double _rightPeak = 0;

        private Random _rand = new Random();

        private readonly string PlayIconData = "M2,2 L14,8 L2,14 Z";
        private readonly string PauseIconData = "M2,2 L5,2 L5,14 L2,14 Z M11,2 L14,2 L14,14 L11,14 Z";

        // Theme Variables
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
            if (VolumeSliderControl != null) VolumeSliderControl.IsMoveToPointEnabled = true;
            if (SpectrumFalloffSlider != null) SpectrumFalloffSlider.IsMoveToPointEnabled = true;

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

            for (int i = 0; i < 10; i++) _freqCurrents[i] = 50;
            for (int i = 0; i < 48; i++) _spectrumCurrents[i] = 2;

            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            InitializeSystemTray();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnableGlassmorphismBlur();

            LoadSettings();
            PushColorsToUI();

            LoadProfileImage();

            try
            {
                _isUpdatingVolumeUI = true;
                if (VolumeSliderControl != null) VolumeSliderControl.Value = SystemVolumeManager.GetVolume();
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

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWaveformMask();
        }

        // ==========================================
        // UI Navigation & Compact Mode Logic
        // ==========================================
        private void CompactModeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isCompactMode = !_isCompactMode;

            if (_isCompactMode)
            {
                CompactModeBtn.Content = "🗖";

                SidebarBorder.Visibility = Visibility.Collapsed;
                EqContentPanel.Visibility = Visibility.Collapsed;
                if (FxContentPanel != null) FxContentPanel.Visibility = Visibility.Collapsed;
                SettingsContentPanel.Visibility = Visibility.Collapsed;
                if (AppTitleText != null) AppTitleText.Visibility = Visibility.Collapsed;

                if (CompactEqToggle != null) CompactEqToggle.Visibility = Visibility.Visible;

                if (TrackTitle != null) { TrackTitle.MaxWidth = 85; TrackTitle.TextTrimming = TextTrimming.CharacterEllipsis; }
                if (TrackArtist != null) { TrackArtist.MaxWidth = 85; TrackArtist.TextTrimming = TextTrimming.CharacterEllipsis; }
                if (VolumeSliderControl != null) VolumeSliderControl.Width = 60;

                DoubleAnimation widthAnim = new DoubleAnimation(this.ActualWidth, 600, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoubleAnimation heightAnim = new DoubleAnimation(this.ActualHeight, 180, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

                this.BeginAnimation(Window.WidthProperty, widthAnim);
                this.BeginAnimation(Window.HeightProperty, heightAnim);
            }
            else
            {
                CompactModeBtn.Content = "🗗";

                if (CompactEqToggle != null) CompactEqToggle.Visibility = Visibility.Collapsed;

                SidebarBorder.Visibility = Visibility.Visible;
                if (AppTitleText != null) AppTitleText.Visibility = Visibility.Visible;

                if (TrackTitle != null) TrackTitle.MaxWidth = Double.PositiveInfinity;
                if (TrackArtist != null) TrackArtist.MaxWidth = Double.PositiveInfinity;
                if (VolumeSliderControl != null) VolumeSliderControl.Width = 110;

                if (_activePanel == 0) EqContentPanel.Visibility = Visibility.Visible;
                else if (_activePanel == 1 && FxContentPanel != null) FxContentPanel.Visibility = Visibility.Visible;
                else if (_activePanel == 2) SettingsContentPanel.Visibility = Visibility.Visible;

                DoubleAnimation widthAnim = new DoubleAnimation(this.ActualWidth, 1100, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoubleAnimation heightAnim = new DoubleAnimation(this.ActualHeight, 768, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

                this.BeginAnimation(Window.WidthProperty, widthAnim);
                this.BeginAnimation(Window.HeightProperty, heightAnim);
            }
        }

        private void EqualizerBtn_Click(object sender, RoutedEventArgs e)
        {
            _activePanel = 0;
            if (NavEqActive == null || NavSettingsActive == null || NavFxActive == null) return;

            NavEqActive.Visibility = Visibility.Visible;
            NavEqBtn.Visibility = Visibility.Collapsed;

            NavFxActive.Visibility = Visibility.Collapsed;
            NavFxBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Collapsed;
            NavSettingsBtn.Visibility = Visibility.Visible;

            SettingsContentPanel.Visibility = Visibility.Collapsed;
            if (FxContentPanel != null) FxContentPanel.Visibility = Visibility.Collapsed;
            EqContentPanel.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            EqContentPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void FxBtn_Click(object sender, RoutedEventArgs e)
        {
            _activePanel = 1;
            if (NavEqActive == null || NavSettingsActive == null || NavFxActive == null) return;

            NavEqActive.Visibility = Visibility.Collapsed;
            NavEqBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Collapsed;
            NavSettingsBtn.Visibility = Visibility.Visible;

            NavFxActive.Visibility = Visibility.Visible;
            NavFxBtn.Visibility = Visibility.Collapsed;

            EqContentPanel.Visibility = Visibility.Collapsed;
            SettingsContentPanel.Visibility = Visibility.Collapsed;
            if (FxContentPanel != null) FxContentPanel.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            FxContentPanel?.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            _activePanel = 2;
            if (NavEqActive == null || NavSettingsActive == null || NavFxActive == null) return;

            NavEqActive.Visibility = Visibility.Collapsed;
            NavEqBtn.Visibility = Visibility.Visible;

            NavFxActive.Visibility = Visibility.Collapsed;
            NavFxBtn.Visibility = Visibility.Visible;

            NavSettingsActive.Visibility = Visibility.Visible;
            NavSettingsBtn.Visibility = Visibility.Collapsed;

            EqContentPanel.Visibility = Visibility.Collapsed;
            if (FxContentPanel != null) FxContentPanel.Visibility = Visibility.Collapsed;
            SettingsContentPanel.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            SettingsContentPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        // ==========================================
        // Fat Mode Switch Logic
        // ==========================================
        private void FatModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isFatModeEnabled = toggle.IsChecked ?? false;
                ApplyEqToAudioStream();
            }
        }

        // ==========================================
        // Settings: Visualizer Graphics Logic
        // ==========================================
        private void SetVisualizerFrameRate(int comboIndex)
        {
            if (_visualizerTimer == null) return;

            switch (comboIndex)
            {
                case 0: // 30 FPS (Eco)
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(33.33);
                    break;
                case 1: // 60 FPS (Default)
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(16.66);
                    break;
                case 2: // 120 FPS (Ultra)
                    _visualizerTimer.Interval = TimeSpan.FromMilliseconds(8.33);
                    break;
            }
        }

        private void VisualizerFpsSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var comboBox = sender as ComboBox;
            if (comboBox != null)
            {
                SetVisualizerFrameRate(comboBox.SelectedIndex);
            }
        }

        private void SpectrumFalloffSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            _spectrumFalloffDropRate = (e.NewValue / 100.0) * 5.0;
            if (_spectrumFalloffDropRate < 0.1) _spectrumFalloffDropRate = 0.1;
        }

        private void EcoModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _isEcoModeEnabled = toggle.IsChecked ?? false;

                double targetOpacity = _isEcoModeEnabled ? 0.3 : 1.0;
                var fadeAnim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(300));

                if (EqFreqResponsePanel != null) EqFreqResponsePanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                if (EqSpectrumPanel != null) EqSpectrumPanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
        }

        // ==========================================
        // Audio Recording Engine & UX Animations
        // ==========================================
        private void RecordBtn_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!_isRecording) StartRecording();
            else StopRecording();
        }

        private void RecordBtn_Click(object sender, RoutedEventArgs e)
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
                    _waveWriter.Write(a.Buffer, 0, a.BytesRecorded);
                };

                _loopbackCapture.RecordingStopped += LoopbackCapture_RecordingStopped;

                _loopbackCapture.StartRecording();
                _isRecording = true;

                // UI Updates
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

            // Reset UI
            NavRecordActive.Visibility = Visibility.Collapsed;
            NavRecordBtn.Visibility = Visibility.Visible;

            RecordBlinkDot.BeginAnimation(UIElement.OpacityProperty, null);
            RecordBlinkDot.Opacity = 1.0;

            if (RecordBlinkDot.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            }
            RecordBlinkDot.Effect = null;

            NavRecordActive.Background = (SolidColorBrush)FindResource("GlassOverlayBrush");
        }

        private void LoopbackCapture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _waveWriter?.Dispose();
                _waveWriter = null;
                _loopbackCapture?.Dispose();
                _loopbackCapture = null;

                SaveFileDialog saveFileDialog = new SaveFileDialog
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

        private void RecordTimer_Tick(object sender, EventArgs e)
        {
            _recordDuration = _recordDuration.Add(TimeSpan.FromSeconds(1));
            RecordTimeText.Text = string.Format("{0:D2}:{1:D2}", _recordDuration.Minutes, _recordDuration.Seconds);
        }

        // ==========================================
        // System Tray & Window State Logic
        // ==========================================
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
            contextMenu.Items.Add("Open Equalizer Pro", null, (s, args) => ShowFromTray());
            contextMenu.Items.Add("Exit", null, (s, args) => ForceExit());
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

        // ==========================================
        // App Save/Load Settings
        // ==========================================
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

        private void SaveSettings()
        {
            try
            {
                string currentPreset = (PresetSelector.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Custom";
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

                File.WriteAllLines(GetSettingsFilePath(), new string[] {
                    currentPreset, toggleState, darkModeState, accentColorHex,
                    alwaysOnTopState, minTrayState, startWinState, fpsIndex, falloffValue, ecoModeState, fatModeState
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
                        foreach (ComboBoxItem item in PresetSelector.Items)
                        {
                            if (item.Content.ToString() == savedPreset)
                            {
                                PresetSelector.SelectedItem = item;
                                break;
                            }
                        }
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
                            SetModeColors(_isDarkMode);
                        }
                    }

                    if (lines.Length >= 4 && ThemeSelector != null)
                    {
                        try
                        {
                            _targetAccent = (Color)ColorConverter.ConvertFromString(lines[3].Trim());
                            string themeName = GetThemeNameFromColor(_targetAccent);
                            bool foundMatch = false;

                            if (themeName != null)
                            {
                                foreach (ComboBoxItem item in ThemeSelector.Items)
                                {
                                    if (item.Content.ToString() == themeName)
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
                            if (EqFreqResponsePanel != null) EqFreqResponsePanel.Opacity = targetOpacity;
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

                    SyncThemeVariablesToTarget();
                }
            }
            catch { }
        }

        private string GetThemeNameFromColor(Color c)
        {
            if (ColorsAreClose(c, Color.FromRgb(92, 97, 255))) return "Blue (Default)";
            if (ColorsAreClose(c, Color.FromRgb(255, 71, 87))) return "Red";
            if (ColorsAreClose(c, Color.FromRgb(46, 204, 113))) return "Green";
            if (ColorsAreClose(c, Color.FromRgb(255, 159, 67))) return "Orange";
            if (ColorsAreClose(c, Color.FromRgb(155, 89, 182))) return "Purple";
            if (ColorsAreClose(c, Color.FromRgb(0, 210, 211))) return "Cyan";
            return null;
        }

        // ==========================================
        // UI Settings Toggles
        // ==========================================
        private void AlwaysOnTopToggle_Click(object sender, RoutedEventArgs e)
        {
            bool keepOnTop = AlwaysOnTopToggle.IsChecked ?? false;
            this.Topmost = false;

            if (keepOnTop)
            {
                this.Topmost = true;
                this.Activate();
            }
        }

        private void StartWithWindowsToggle_Click(object sender, RoutedEventArgs e)
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
                            string appPath = Environment.ProcessPath;
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

        private void VolumeSliderControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUpdatingVolumeUI && IsLoaded)
            {
                SystemVolumeManager.SetVolume(VolumeSliderControl.Value);
            }
        }

        // ==========================================
        // DSP Equalizer Engine
        // ==========================================
        private void InitializePresets()
        {
            _eqPresets.Add("Custom", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            _eqPresets.Add("Acoustic", new double[] { 5, 5, 4, 1, 1, 1, 3, 4, 3, 2 });
            _eqPresets.Add("Bass Boost", new double[] { 9, 7, 4, 2, 0, 0, 0, 0, 0, 0 });
            _eqPresets.Add("Electronic", new double[] { 6, 5, 0, -2, -1, 2, 4, 6, 5, 3 });
            _eqPresets.Add("Vocal", new double[] { -2, -1, 1, 4, 6, 6, 5, 2, 0, -1 });
            _eqPresets.Add("Rock", new double[] { 6, 4, 2, -1, -2, -1, 2, 4, 5, 6 });
            _eqPresets.Add("Dance", new double[] { 8, 6, 2, 0, -2, -2, 0, 2, 4, 6 });
        }

        private void GlobalEqToggle_Click(object sender, RoutedEventArgs e)
        {
            _isEqEnabled = GlobalEqToggle.IsChecked ?? false;

            if (CompactEqToggle != null) CompactEqToggle.IsChecked = _isEqEnabled;

            if (SlidersContainerGrid != null) SlidersContainerGrid.Opacity = _isEqEnabled ? 1.0 : 0.4;
            ApplyEqToAudioStream();
        }

        private void CompactEqToggle_Click(object sender, RoutedEventArgs e)
        {
            _isEqEnabled = CompactEqToggle?.IsChecked ?? false;

            if (GlobalEqToggle != null) GlobalEqToggle.IsChecked = _isEqEnabled;

            if (SlidersContainerGrid != null) SlidersContainerGrid.Opacity = _isEqEnabled ? 1.0 : 0.4;
            ApplyEqToAudioStream();
        }

        private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingPreset || PresetSelector == null || _eqSliders == null) return;

            var selected = PresetSelector.SelectedItem as ComboBoxItem;
            if (selected == null) return;

            string presetName = selected.Content.ToString();
            if (_eqPresets.ContainsKey(presetName))
            {
                _isUpdatingPreset = true;
                double[] values = _eqPresets[presetName];

                for (int i = 0; i < 10; i++) _eqSliders[i].Value = values[i];

                if (_isEqEnabled) ApplyEqToAudioStream();
                _isUpdatingPreset = false;
            }
        }

        private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUpdatingPreset && PresetSelector != null)
            {
                _isUpdatingPreset = true;
                PresetSelector.SelectedIndex = 0;
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
                        if (_isFatModeEnabled)
                        {
                            double lowBoost = 4.5;
                            double harmonicBoost = 2.0;
                            double preampComp = -3.5;

                            eqCommand += $"Preamp: {preampComp:0.0} dB\n";
                            eqCommand += $"Filter: ON LS Fc 120 Hz Gain {lowBoost:0.0} dB Q 0.7\n";
                            eqCommand += $"Filter: ON PK Fc 2500 Hz Gain {harmonicBoost:0.0} dB Q 1.0\n";
                        }

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

        // ==========================================
        // Faux Waveform Engine
        // ==========================================
        private Geometry GenerateWaveformGeometry(string seedStr)
        {
            int seed = seedStr != null ? seedStr.GetHashCode() : 0;
            Random rnd = new Random(seed);
            GeometryGroup group = new GeometryGroup();

            int numBars = 100;
            double width = 1000;
            double maxH = 40;
            double barWidth = width / numBars;
            double gap = barWidth * 0.4;

            for (int i = 0; i < numBars; i++)
            {
                double h = maxH * (0.1 + rnd.NextDouble() * 0.9);
                double envelope = Math.Sin((i / (double)numBars) * Math.PI);
                h = h * (0.4 + 0.6 * envelope);

                double x = i * barWidth;
                double y = (maxH - h) / 2;

                group.Children.Add(new RectangleGeometry(new Rect(x, y, barWidth - gap, h), 2, 2));
            }
            return group;
        }

        private void UpdateWaveformMask()
        {
            if (WaveformPlayedMask != null && SeekSlider != null && SeekSlider.Maximum > 0)
            {
                double percent = SeekSlider.Value / SeekSlider.Maximum;
                if (double.IsNaN(percent) || double.IsInfinity(percent)) percent = 0;

                double newWidth = percent * SeekSlider.ActualWidth;
                if (double.IsNaN(newWidth) || double.IsInfinity(newWidth) || newWidth < 0) newWidth = 0;

                // Fix for CS1061: Use the Rect property to set the width and height of the mask
                WaveformPlayedMask.Rect = new Rect(0, 0, newWidth, 20);
            }
        }

        // ==========================================
        // Wave Transition Engine
        // ==========================================
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

        // ==========================================
        // Custom Color Picker & Theme Handlers
        // ==========================================
        private void CustomColorBtn_Click(object sender, RoutedEventArgs e)
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

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

        private void HexInputBox_TextChanged(object sender, TextChangedEventArgs e)
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

        private void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
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
            });
        }

        private void SetModeColors(bool isDark)
        {
            if (isDark)
            {
                _tarWindowBg = Color.FromArgb(153, 11, 14, 20);
                _tarPanelBg = Color.FromArgb(115, 11, 14, 20);
                _tarText = Colors.White;
                _tarMutedText = Color.FromRgb(169, 177, 214);
                _tarBorder = Color.FromArgb(38, 255, 255, 255);
                _tarOverlay = Color.FromArgb(26, 255, 255, 255);
                _tarHover = Color.FromArgb(51, 255, 255, 255);
            }
            else
            {
                _tarWindowBg = Color.FromArgb(190, 240, 244, 248);
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

        private void ModeSwitchBtn_Click(object sender, RoutedEventArgs e)
        {
            Point origin = ModeSwitchBtn.TransformToAncestor(this).Transform(new Point(ModeSwitchBtn.ActualWidth / 2, ModeSwitchBtn.ActualHeight / 2));

            TriggerWaveTransition(origin, () =>
            {
                _isDarkMode = !_isDarkMode;
                ModeSwitchBtn.Content = _isDarkMode ? "☀" : "🌙";
                SetModeColors(_isDarkMode);
            });
        }

        private void ThemeSelector_ThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || ThemeSelector == null || !IsLoaded) return;
            var selectedItem = (ComboBoxItem)ThemeSelector.SelectedItem;
            if (selectedItem == null) return;

            string themeName = selectedItem.Content.ToString();
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
                this.Resources["WindowBgBrush"] = new SolidColorBrush(_curWindowBg) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["PanelBgBrush"] = new SolidColorBrush(_curPanelBg) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["TextBrush"] = new SolidColorBrush(_curText) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["MutedTextBrush"] = new SolidColorBrush(_curMutedText) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["BorderBrush"] = new SolidColorBrush(_curBorder) { Opacity = 1 }.GetCurrentValueAsFrozen();
                this.Resources["GlassOverlayBrush"] = new SolidColorBrush(_curOverlay) { Opacity = 1 }.GetCurrentValueAsFrozen();
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

        private void EnableGlassmorphismBlur()
        {
            try
            {
                var windowHelper = new WindowInteropHelper(this);
                var accent = new AccentPolicy();

                accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                accent.GradientColor = 0x00000000;

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

        private void VisualizerTimer_Tick(object sender, EventArgs e)
        {
            bool needsColorUpdate = false;
            if (!ColorsAreClose(_curWindowBg, _tarWindowBg) || !ColorsAreClose(_currentAccent, _targetAccent))
            {
                _curWindowBg = LerpColor(_curWindowBg, _tarWindowBg, 0.08);
                _curPanelBg = LerpColor(_curPanelBg, _tarPanelBg, 0.08);
                _curText = LerpColor(_curText, _tarText, 0.08);
                _curMutedText = LerpColor(_curMutedText, _tarMutedText, 0.08);
                _curBorder = LerpColor(_curBorder, _tarBorder, 0.08);
                _curOverlay = LerpColor(_curOverlay, _tarOverlay, 0.08);
                _curHover = LerpColor(_curHover, _tarHover, 0.08);
                _currentAccent = LerpColor(_currentAccent, _targetAccent, 0.08);
                needsColorUpdate = true;
            }
            else if (_curWindowBg != _tarWindowBg || _currentAccent != _targetAccent)
            {
                _curWindowBg = _tarWindowBg; _curPanelBg = _tarPanelBg; _curText = _tarText;
                _curMutedText = _tarMutedText; _curBorder = _tarBorder; _curOverlay = _tarOverlay;
                _curHover = _tarHover; _currentAccent = _targetAccent;
                needsColorUpdate = true;
            }
            if (needsColorUpdate) PushColorsToUI();

            float rawPeak = _isEcoModeEnabled ? 0f : SystemVolumeManager.GetPeakValue();

            double volumeFraction = VolumeSliderControl != null ? (VolumeSliderControl.Value / 100.0) : 1.0;
            float currentPeak = (float)(rawPeak * volumeFraction);

            bool isPlayingState = _currentSession?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            bool isActuallyPlayingAudio = !_isEcoModeEnabled && isPlayingState && currentPeak > 0.001f;

            for (int i = 0; i < 10; i++)
            {
                double sliderVal = _eqSliders != null ? _eqSliders[i].Value : 0;
                double baseVisualY = 50 - (sliderVal * 2.5);

                if (isActuallyPlayingAudio)
                {
                    if (_rand.NextDouble() < 0.2) _freqTargets[i] = baseVisualY + (_rand.Next(-15, 15) * currentPeak * 2.5);
                }
                else
                {
                    _freqTargets[i] = baseVisualY;
                }
                _freqCurrents[i] += (_freqTargets[i] - _freqCurrents[i]) * 0.10;
            }
            DrawFrequencyGraph();

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

                    if (EqSpectrumGrid.Children.Count > i && EqSpectrumGrid.Children[i] is Border border)
                    {
                        border.Height = _spectrumCurrents[i];
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
                    EqRightDbText.Text = rightDbVal <= -59.0 ? "-inf" : rightDbVal.ToString("0.0");
                }
            }
        }

        private void DrawFrequencyGraph()
        {
            if (EqFreqResponseLine == null || EqFreqResponseFill == null) return;

            Point[] points = new Point[10];
            for (int i = 0; i < 10; i++) points[i] = new Point(i * 11.11, _freqCurrents[i]);

            var geometry = CreateSmoothCurve(points);
            EqFreqResponseLine.Data = geometry;

            var fillGeometry = CreateSmoothCurve(points);
            var figure = ((PathGeometry)fillGeometry).Figures[0];
            figure.Segments.Add(new LineSegment(new Point(100, 100), false));
            figure.Segments.Add(new LineSegment(new Point(0, 100), false));
            figure.IsClosed = true;

            EqFreqResponseFill.Data = fillGeometry;
        }

        private PathGeometry CreateSmoothCurve(Point[] points)
        {
            PathGeometry geometry = new PathGeometry();
            if (points.Length < 2) return geometry;

            PathFigure figure = new PathFigure { StartPoint = points[0], IsClosed = false };
            PolyBezierSegment segment = new PolyBezierSegment();
            double tension = 0.3;

            for (int i = 0; i < points.Length - 1; i++)
            {
                Point p0 = i == 0 ? points[0] : points[i - 1];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = i + 2 == points.Length ? points[i + 1] : points[i + 2];

                Point cp1 = new Point(p1.X + (p2.X - p0.X) * tension, p1.Y + (p2.Y - p0.Y) * tension);
                Point cp2 = new Point(p2.X - (p3.X - p1.X) * tension, p2.Y - (p3.Y - p1.Y) * tension);

                segment.Points.Add(cp1);
                segment.Points.Add(cp2);
                segment.Points.Add(p2);
            }
            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);
            return geometry;
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => UpdateCurrentSession(sender.GetCurrentSession());

        private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession session)
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
                    TrackTitle.Text = "Unknown Track";
                    TrackArtist.Text = "Unknown Artist";
                    TrackImageBrush.ImageSource = null;
                    TrackIcon.Visibility = Visibility.Visible;

                    CurrentTimeText.Text = "0:00";
                    TotalTimeText.Text = "0:00";
                    SeekSlider.Value = 0;

                    // Clear faux waveform
                    if (WaveformPathUnplayed != null) WaveformPathUnplayed.Data = null;
                    if (WaveformPathPlayed != null) WaveformPathPlayed.Data = null;

                    PlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                    PlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
                });
            }
        }

        private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => UpdateMediaProperties();
        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => UpdatePlaybackState();
        private void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => UpdateTimeline();
        private void PlaybackTimer_Tick(object sender, EventArgs e) => UpdateTimeline();

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
                    BitmapImage bitmapImage = null;

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
                        TrackTitle.Text = title;
                        TrackArtist.Text = artist;

                        // Generate the unique faux-waveform seeded by the track title
                        Geometry wave = GenerateWaveformGeometry(title);
                        if (WaveformPathUnplayed != null) WaveformPathUnplayed.Data = wave;
                        if (WaveformPathPlayed != null) WaveformPathPlayed.Data = wave;

                        if (bitmapImage != null)
                        {
                            TrackImageBrush.ImageSource = bitmapImage;
                            TrackIcon.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            TrackImageBrush.ImageSource = null;
                            TrackIcon.Visibility = Visibility.Visible;
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
                        PlayPauseIcon.Data = Geometry.Parse(PauseIconData);
                        PlayPauseIcon.Margin = new Thickness(0);
                    }
                    else
                    {
                        PlayPauseIcon.Data = Geometry.Parse(PlayIconData);
                        PlayPauseIcon.Margin = new Thickness(3, 0, 0, 0);
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

                    SeekSlider.Maximum = timeline.EndTime.TotalSeconds;
                    SeekSlider.Value = currentPosition.TotalSeconds;

                    CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(currentPosition.TotalMinutes), currentPosition.Seconds);
                    TotalTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(timeline.EndTime.TotalMinutes), timeline.EndTime.Seconds);

                    UpdateWaveformMask();
                });
            }
        }

        private async void PlayPause_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (PlayPauseBorder.RenderTransform is ScaleTransform scaleTransform)
            {
                var popAnim = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
                };
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
            }

            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
        }

        private async void Previous_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null) await _currentSession.TrySkipPreviousAsync();
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null) await _currentSession.TrySkipNextAsync();
        }

        private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e) => _isDraggingSeekbar = true;

        private async void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_currentSession != null)
            {
                var newPosition = TimeSpan.FromSeconds(SeekSlider.Value);
                await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);
            }
            _isDraggingSeekbar = false;
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateWaveformMask();
            if (_isDraggingSeekbar)
            {
                var tempPosition = TimeSpan.FromSeconds(SeekSlider.Value);
                CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(tempPosition.TotalMinutes), tempPosition.Seconds);
            }
        }

        private async void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentSession != null && !_isDraggingSeekbar)
            {
                var newPosition = TimeSpan.FromSeconds(SeekSlider.Value);
                await _currentSession.TryChangePlaybackPositionAsync(newPosition.Ticks);

                CurrentTimeText.Text = string.Format("{0}:{1:D2}", Math.Floor(newPosition.TotalMinutes), newPosition.Seconds);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
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

        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            AboutOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var popIn = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            AboutScale.BeginAnimation(ScaleTransform.ScaleXProperty, popIn);
            AboutScale.BeginAnimation(ScaleTransform.ScaleYProperty, popIn);
        }

        private void CloseAbout_Click(object sender, RoutedEventArgs e)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => AboutOverlay.Visibility = Visibility.Collapsed;
            AboutOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    public static class SystemVolumeManager
    {
        public static MMDevice GetDefaultDevice()
        {
            try
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch { return null; }
        }

        public static float GetPeakValue()
        {
            try
            {
                var device = GetDefaultDevice();
                if (device != null)
                {
                    return device.AudioMeterInformation.MasterPeakValue;
                }
            }
            catch { }
            return 0f;
        }

        public static void SetVolume(double volumeLevel)
        {
            try
            {
                var device = GetDefaultDevice();
                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volumeLevel / 100.0);
                }
            }
            catch { }
        }

        public static double GetVolume()
        {
            try
            {
                var device = GetDefaultDevice();
                if (device != null)
                {
                    return device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0;
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