using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KaleidoStream
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly Logger _logger;
        private readonly List<StreamViewer> _streamViewers;
        private readonly DispatcherTimer _reconnectTimer;       
        private ResolutionConfig _globalResolution = new() { Width = 320, Height = 240 };

        private int _gridColumns;
        public int GridColumns
        {
            get => _gridColumns;
            set { _gridColumns = value; OnPropertyChanged(); }
        }

        private int _gridRows;
        public int GridRows
        {
            get => _gridRows;
            set { _gridRows = value; OnPropertyChanged(); }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _logger = new Logger(this);
            _streamViewers = new List<StreamViewer>();
           

            // Setup reconnect timer (check every 3 minutes)
            _reconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(3)
            };
            _reconnectTimer.Tick += ReconnectTimer_Tick;

            LoadConfiguration();
            InitializeStreamGrid();
            _reconnectTimer.Start();

            // Check if there are no streams configured
            if (_streamInfos == null || !_streamInfos.Any(s => s.Enabled))
            {
                var result = MessageBox.Show(
                    "Configure sources of video",
                    "No Video Sources Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                // Open VideosWindow for configuration
                var win = new VideosWindow(_streamInfos);
                if (win.ShowDialog() == true)
                {
                    SaveConfiguration();
                    LoadConfiguration();
                    InitializeStreamGrid();
                }
            }

            _logger.Log("KaleidoStream application started");
        }

        private List<StreamInfo> _streamInfos = new();

        private void LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.yaml");
                if (File.Exists(configPath))
                {
                    var yaml = File.ReadAllText(configPath);
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    var config = deserializer.Deserialize<StreamConfig>(yaml);

                    _streamInfos = config?.Streams?.Select(s => new StreamInfo
                    {
                        Name = s.Name,
                        Url = s.Url,
                        Enabled = s.Enabled
                    }).ToList() ?? new List<StreamInfo>();
                    _logger.Log($"Loaded {_streamInfos.Count} stream URLs from YAML configuration");

                    _globalResolution = config?.Resolution ?? new ResolutionConfig { Width = 320, Height = 240 };
                    _logger.Log($"Loaded default resolution {_globalResolution.Width}x{_globalResolution.Height} from YAML configuration");


                    // Set theme based on config
                    if (!string.IsNullOrEmpty(config?.Theme) && config.Theme.ToLower() == "dark")
                    {
                        _isDarkTheme = true; // so SwitchThemeMenuItem_Click will switch to dark if needed
                        SwitchTheme("Themes/DarkTheme.xaml");
                    }
                    else
                    {
                        _isDarkTheme = false; // so SwitchThemeMenuItem_Click will switch to light if needed
                        SwitchTheme("Themes/LightTheme.xaml");
                    }
                }
                else
                {
                    _logger.Log("KaleidoStream YAML configuration file not found. Creating sample config.yaml");
                    CreateSampleYamlConfig(configPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading YAML configuration: {ex.Message}");
            }
        }

        private void SaveConfiguration()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.yaml");
            var config = new StreamConfig
            {
                Streams = _streamInfos,
                Theme = _isDarkTheme ? "dark" : "light",
                Resolution = _globalResolution
            };
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var newYaml = serializer.Serialize(config);
            File.WriteAllText(configPath, newYaml);
        }

        private void SwitchTheme(string themePath)
        {
            var appResources = Application.Current.Resources.MergedDictionaries;
            appResources.Clear();
            var newTheme = new ResourceDictionary() { Source = new Uri(themePath, UriKind.Relative) };
            appResources.Add(newTheme);
        }

        private void CreateSampleYamlConfig(string configPath)
        {
            var sampleConfig = @"
# KaleidoStream - Configuration
# theme, it can be light or dark 
theme: light
# default resolution at launch of the app unless otherwise specified in each stream
resolution:
  width: 320
  height: 240
";
            File.WriteAllText(configPath, sampleConfig);
        }

        private void StopAllStreams()
        {
            foreach (var viewer in _streamViewers)
            {                
                viewer.StopStreaming();
            }

        }

        private void InitializeStreamGrid()
        {
            // Only include enabled streams
            var enabledStreams = _streamInfos.Where(s => s.Enabled).ToList();
            
            CalculateGridDimensions(enabledStreams.Count);

            foreach (var viewer in _streamViewers)
                viewer.Dispose();
            _streamViewers.Clear();
            StreamGrid.Children.Clear();

            if (enabledStreams.Count == 0) return;

            // Add StreamViewers for enabled streams only
            for (int i = 0; i < enabledStreams.Count; i++)
            {
                try
                {
                    var info = enabledStreams[i];
                    var streamViewer = new StreamViewer(info.Url, info.Name, _logger, _globalResolution.Width, _globalResolution.Height);
                    _streamViewers.Add(streamViewer);
                    StreamGrid.Children.Add(streamViewer);
                    // Start the stream immediately
                    _ = streamViewer.StartStreamingAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error creating stream viewer: {ex.Message}");
                }
            }

            // Add empty placeholders for remaining cells
            int totalCells = GridRows * GridColumns;
            int placeholdersNeeded = totalCells - enabledStreams.Count;
            for (int i = 0; i < placeholdersNeeded; i++)
            {
                var placeholder = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2)
                };
                StreamGrid.Children.Add(placeholder);
            }
        }

        private void CalculateGridDimensions(int streamCount)
        {
            if (streamCount == 0)
            {
                GridColumns = GridRows = 1;
                return;
            }

            // Calculate optimal grid layout
            double sqrt = Math.Sqrt(streamCount);
            GridColumns = (int)Math.Ceiling(sqrt);
            GridRows = (int)Math.Ceiling((double)streamCount / GridColumns);
        }

        private async void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            _logger.Log("Checking stream connections...");

            foreach (var viewer in _streamViewers)
            {
                if (!viewer.IsConnected)
                {
                    _logger.Log($"Attempting to reconnect to: {viewer.StreamUrl}");
                    await viewer.ReconnectAsync();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _reconnectTimer?.Stop();
               
                // Stop all streams with timeout
                var disposeTasks = _streamViewers.Select(viewer => Task.Run(() =>
                {
                    try { viewer.Dispose(); }
                    catch (Exception ex) { _logger?.LogError($"Error disposing viewer: {ex.Message}"); }
                })).ToArray();

                if (!Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(10)))
                {
                    _logger?.LogWarning("Some stream viewers did not dispose within timeout");
                }

                _logger?.Log("VideWall application closed");
                _logger?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StopAllStreams();
            Close();
        }

        private void VideosMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Create a deep copy for editing
            var tempList = _streamInfos.Select(s => new StreamInfo
            {
                Name = s.Name,
                Url = s.Url,
                Enabled = s.Enabled
            }).ToList();

            var win = new VideosWindow(tempList);
            win.Owner = this;
            if (win.ShowDialog() == true)
            {
                // Copy changes back
                bool anyChanged = false;
                if (_streamInfos.Count != tempList.Count) {
                    // if size is different, something changed
                    anyChanged = true;
                } else {
                    for (int i = 0; i < _streamInfos.Count && i < tempList.Count; i++)
                    {

                        if (_streamInfos[i].Name != tempList[i].Name ||
                            _streamInfos[i].Url != tempList[i].Url ||
                            _streamInfos[i].Enabled != tempList[i].Enabled)
                        {
                            anyChanged = true;
                            break;
                        }
                    }
                }
                if (anyChanged)
                {
                    // Copy changes back
                    _streamInfos.Clear();
                    _streamInfos.AddRange(tempList);
                    SaveConfiguration();
                    StopAllStreams();
                    InitializeStreamGrid();
                }
            }
        }

        private void ResolutionMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tag)
            {
                var parts = tag.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                {
                    ChangeAllStreamResolutions(width, height);
                }
            }
        }

        private void ChangeAllStreamResolutions(int width, int height)
        {
            _globalResolution.Width = width;
            _globalResolution.Height = height;

            // Optionally update config.yaml here if you want to persist the change

            foreach (var viewer in _streamViewers)
            {
                viewer.ChangeResolution(width, height);
            }
        }

        private bool _isDarkTheme;

        private void SwitchThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var appResources = Application.Current.Resources.MergedDictionaries;
            string themeToLoad = _isDarkTheme ? "Themes/LightTheme.xaml" : "Themes/DarkTheme.xaml";
            string themeToRemove = _isDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";

            // Remove the current theme
            var existingTheme = appResources
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.EndsWith(themeToRemove));
            if (existingTheme != null)
                appResources.Remove(existingTheme);

            // Add the new theme
            var newTheme = new ResourceDictionary() { Source = new Uri(themeToLoad, UriKind.Relative) };
            appResources.Add(newTheme);

            _isDarkTheme = !_isDarkTheme;

            // Save theme selection
            SaveThemeToConfig(_isDarkTheme ? "dark" : "light");

        }

        private void SaveThemeToConfig(string theme)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.yaml");
            if (!File.Exists(configPath)) return;

            var yaml = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var config = deserializer.Deserialize<StreamConfig>(yaml);
            config.Theme = theme;

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var newYaml = serializer.Serialize(config);
            File.WriteAllText(configPath, newYaml);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class StreamConfig
        {
            public List<StreamInfo> Streams { get; set; }
            public string Theme { get; set; }
            public ResolutionConfig Resolution { get; set; }
        }

        public class ResolutionConfig
        {
            public int Width { get; set; }
            public int Height { get; set; }
        }

       
    }

    public class Logger : IDisposable
    {
        private readonly StreamWriter _logWriter;
        private readonly MainWindow _mainWindow;
        private readonly object _lockObject = new object();

        public Logger(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory); // Ensure the logs directory exists

            string logFileName = $"KaleidoStream{DateTime.Now:yyyyMMdd_HHmmss}.log";
            string logPath = Path.Combine(logDirectory, logFileName);

            try
            {
                _logWriter = new StreamWriter(logPath, true) { AutoFlush = true };
                Log("KaleidoStream logger initialized");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize KaleidoStream logger: {ex.Message}");
            }
        }

        public void Log(string message)
        {
            var logMessage = $"[INFO] {message}";
            WriteLog(logMessage);
        }

        public void LogError(string message)
        {
            var logMessage = $"[ERROR] {message}";
            WriteLog(logMessage);
        }

        public void LogWarning(string message)
        {
            var logMessage = $"[WARNING] {message}";
            WriteLog(logMessage);
        }

        private void WriteLog(string message)
        {
            lock (_lockObject)
            {
                try
                {
                    var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
                    _logWriter?.WriteLine(timestampedMessage);
                    Console.WriteLine(timestampedMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Logging error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                _logWriter?.Dispose();
            }
        }
    }

    public class StreamViewer : UserControl, IDisposable
    {
        private readonly Logger _logger;
        private readonly FFmpegStreamRenderer _renderer;
        private StackPanel _statusPanel;
        private readonly Border _container;
        private readonly TextBlock _statusText;
        private readonly Grid _contentGrid;
        private Button _startButton;
        private Button _stopButton;
        private bool _disposed;
        private readonly string _streamName;
        private int _width;
        private int _height;
        private string _sourceResolution;
        private bool _isStopped;
        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (_isRecording != value)
                {
                    _isRecording = value;
                    UpdateStatus();
                }
            }
        }

        private MenuItem _recordMenuItem;        
        private MenuItem _startStopMenuItem;

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    UpdateStatus();
                }
            }
        }

        public string StreamUrl { get; }
        public bool IsConnected => _renderer?.IsConnected ?? false;

        public StreamViewer(string streamUrl, string streamName, Logger logger, int width, int height)
        {
            StreamUrl = streamUrl;
            _streamName = streamName;
            _logger = logger;
            _width = width;
            _height = height;

            // Create UI layout
            _container = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            _contentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Status panel (centered horizontally)
            _statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Status text
            _statusText = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Black,
                Foreground = Brushes.White,
                Padding = new Thickness(5)
            };

            // Start button
            _startButton = new Button
            {
                Content = Properties.Resources.StartButton,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _startButton.Click += async (s, e) => await StartStreamingAsync();

            // Stop button
            _stopButton = new Button
            {
                Content = Properties.Resources.StopButton,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _stopButton.Click += (s, e) => StopStreaming();

            _statusPanel.Children.Add(_statusText);
            _statusPanel.Children.Add(_startButton);
            _statusPanel.Children.Add(_stopButton);

            Grid.SetRow(_statusPanel, 1);
            _contentGrid.Children.Add(_statusPanel);

            _container.Child = _contentGrid;
            Content = _container;
            
            // Add context menu for start / stop and record
            _startStopMenuItem = new MenuItem();
            _startStopMenuItem.Click += StartStopMenuItem_Click;

            _recordMenuItem = new MenuItem();
            _recordMenuItem.Click += RecordMenuItem_Click;

            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(_startStopMenuItem);
            contextMenu.Items.Add(_recordMenuItem);
            ContextMenu = contextMenu;
            UpdateContextMenuText();

            // Initialize FFmpeg renderer
            _renderer = new FFmpegStreamRenderer(streamUrl, streamName, logger, this, _width, _height);
            _renderer.ResolutionDetected += OnResolutionDetected;

            // Set initial state to connecting and show Stop button
            _isStopped = true;
            _startButton.Visibility = Visibility.Visible;
            _stopButton.Visibility = Visibility.Collapsed;
            Status=Properties.Resources.Stopping;
        }

        private void UpdateContextMenuText()
        {
            if (_startStopMenuItem != null)
                _startStopMenuItem.Header = _isStopped ? "Start" : "Stop";

            if (_recordMenuItem != null)
            {
                _recordMenuItem.Header = _isRecording ? "Stop Recording" : "Record";
                if (_isRecording)
                {
                    _recordMenuItem.IsEnabled = true;
                }
                else if (IsConnected && !_isStopped)
                {
                    _recordMenuItem.IsEnabled = true;
                }
                else
                {
                    _recordMenuItem.IsEnabled = false;
                }

            }
        }

        private void StartStopMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isStopped)
            {
                _ = StartStreamingAsync();
            }
            else
            {
                StopStreaming();                
            }
            UpdateContextMenuText();
        }

        private void RecordMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording)
            {
                _renderer.StopRecording();
                IsRecording = false;
                _logger.Log($"Stopped recording for stream: {StreamUrl}");
            }
            else
            {
                _renderer.StartRecording();
                IsRecording = true;
                _logger.Log($"Started recording for stream: {StreamUrl}");
            }
            UpdateContextMenuText();
        }

        public async Task StartStreamingAsync()
        {
            _logger.Log($"StartStreamingAsync called for {StreamUrl}, _isStopped={_isStopped}, _disposed={_disposed}");
            if (_disposed) return;
            if (!_isStopped && _renderer.IsConnected) return;

            _isStopped = false;
            UpdateContextMenuText();
            _startButton.Visibility = Visibility.Collapsed;
            _stopButton.Visibility = Visibility.Visible;
            Status = Properties.Resources.ConnectingStatus;

            try
            {
                await _renderer.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start stream {StreamUrl}: {ex.Message}");
                Status = Properties.Resources.ConnectionFailedStatus;
            }
        }

        public void StopStreaming()
        {
            if (_disposed) return;
            _isStopped = true;
            UpdateContextMenuText();
            _renderer.StopCompletely();
            _startButton.Visibility = Visibility.Visible;
            _stopButton.Visibility = Visibility.Collapsed;
            _renderer.StopRecording();
            Status = Properties.Resources.StoppedStatus;            
        }

        private void OnResolutionDetected(string resolution)
        {
            _sourceResolution = resolution;
            if (IsConnected)
            {
                _startButton.Visibility = Visibility.Collapsed;
                _stopButton.Visibility = Visibility.Visible;
                Status = Properties.Resources.ConnectedStatus;
            }
        }



        public async Task ReconnectAsync()
        {
            if (_disposed) return;

            try
            {
                Status = Properties.Resources.ReconnectingStatus;
                await _renderer.ReconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Reconnection failed for {StreamUrl}: {ex.Message}");
                Status = Properties.Resources.ReconnectionFailedStatus;
            }
        }

        public void ChangeResolution(int width, int height)
        {
            _width = width;
            _height = height;
            _renderer?.ChangeResolution(width, height);
            // Force status update to reflect new render resolution
            Status = IsConnected ? Properties.Resources.ConnectedStatus : Properties.Resources.ConnectingStatus;
        }

        public void UpdateStatus()
        {
            if (_disposed) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateStatus()));
                return;
            }

            // Select color based on status
            Brush color = Brushes.White;
            if (_status == Properties.Resources.ConnectedStatus)
                color = Brushes.Green;
            else if (_status == Properties.Resources.ConnectingStatus || _status == Properties.Resources.ReconnectingStatus)
                color = Brushes.Yellow;
            else if (_status == Properties.Resources.StoppedStatus || _status == Properties.Resources.Stopping)
                color = Brushes.Gray;
            else if (_status == Properties.Resources.ConnectionFailedStatus || _status == Properties.Resources.ReconnectionFailedStatus)
                color = Brushes.Red;

            try
            {
                var displayName = _streamName;
                if (!string.IsNullOrEmpty(_sourceResolution) && _status == Properties.Resources.ConnectedStatus)
                    displayName += $" ({_sourceResolution}->{_width}x{_height})";

                _statusText.Inlines.Clear();
                _statusText.Inlines.Add(new System.Windows.Documents.Run(displayName + ": ") { Foreground = Brushes.White });
                _statusText.Inlines.Add(new System.Windows.Documents.Run(_status) { Foreground = color });

                // Add recording status
                if (_isRecording)
                {
                    _statusText.Inlines.Add(new System.Windows.Documents.Run(" (RECORDING)") { Foreground = Brushes.Red });
                }
                else
                {
                    _statusText.Inlines.Add(new System.Windows.Documents.Run(" (NOT RECORDING)") { Foreground = Brushes.White });
                }


                UpdateContextMenuText();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating status for {StreamUrl}: {ex.Message}");
            }
        }

        private Image _videoImage;

        public void SetVideoFrame(WriteableBitmap bitmap)
        {
            if (_disposed) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetVideoFrame(bitmap)));
                return;
            }

            try
            {
                if (_videoImage == null)
                {
                    _videoImage = new Image
                    {
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.Both,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    Grid.SetRow(_videoImage, 0);
                    _contentGrid.Children.Insert(0, _videoImage);
                }
                _videoImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating video frame for {StreamUrl}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _renderer?.Dispose();
        }
    }
}