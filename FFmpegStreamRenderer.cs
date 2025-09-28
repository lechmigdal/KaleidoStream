using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace KaleidoStream
{
    public class FFmpegStreamRenderer : IDisposable
    {
        private readonly string _streamUrl;
        private readonly Logger _logger;
        private readonly StreamViewer _viewer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _streamingTask;
        private bool _disposed;
        private DateTime _lastFrameTime;
        private readonly object _lockObject = new object();
        private WriteableBitmap _videoBitmap;
        private readonly string _streamName;
        private int _displayWidth;
        private int _displayHeight;
        private string _inputResolution;

        private FFmpegRecordingManager _recordingManager;

        public event Action<string> ResolutionDetected;

        private Process _displayProcess;

        public bool IsRecording => _recordingManager.IsRecording;

        public bool IsConnected { get; private set; }

        public void StopRecording()
        {
            _recordingManager.RequestRecordingStop();
        }

        public void StartRecording()
        {
            _recordingManager.RequestRecordingStart();
        }

        public string InputResolution
        {
            get => _inputResolution;
            private set
            {
                if (_inputResolution != value)
                {
                    _inputResolution = value;
                    ResolutionDetected?.Invoke(_inputResolution);
                }
            }
        }

        public FFmpegStreamRenderer(string streamUrl, string streamName, Logger logger, StreamViewer viewer, int width, int height)
        {
            _streamUrl = streamUrl;
            _logger = logger;
            _viewer = viewer;
            _streamName = streamName;
            _displayWidth = width;
            _displayHeight = height;
            _lastFrameTime = DateTime.MinValue;

            _recordingManager = new FFmpegRecordingManager(_logger, _streamUrl, _streamName);
        }

        public async Task StartAsync()
        {
            _logger.LogWarning($"{_streamName} - StartAsync {_streamUrl}");
            if (_disposed) return;

            lock (_lockObject)
            {
                if (_streamingTask != null && !_streamingTask.IsCompleted)
                    return;

                _cancellationTokenSource = new CancellationTokenSource();
            }

            _logger.Log($"{_streamName} - Starting stream: {_streamUrl}");
            _viewer.Status = "Connecting...";

            _streamingTask = Task.Run(async () => await StreamLoop(_cancellationTokenSource.Token));

            // Wait a moment to let the task start
            await Task.Delay(100);
        }

        public async Task ReconnectAsync()
        {
            if (_disposed) return;

            bool wasRecording = _recordingManager.IsRecording; ;

            if (wasRecording)
            {
                _recordingManager.StopRecording();
            }

            await StopAsync();
            await Task.Delay(1000); // 1 second delay before reconnecting
            await StartAsync();

            if (wasRecording)
            {
                _recordingManager.StartRecording();
            }
        }

        public void StopCompletely()
        {
            _cancellationTokenSource?.Cancel();
            IsConnected = false;
        }

        private async Task StopAsync()
        {
            lock (_lockObject)
            {
                _cancellationTokenSource?.Cancel();
                IsConnected = false;
            }

            if (_streamingTask != null)
            {
                try
                {
                    await _streamingTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{_streamName} - Error stopping stream {_streamUrl}: {ex.Message}");
                }
            }
        }

        public void ChangeResolution(int width, int height)
        {
            if (_displayWidth == width && _displayHeight == height) return;
            _displayWidth = width;
            _displayHeight = height;
            _ = ReconnectAsync();
        }
        private async Task StreamLoop(CancellationToken cancellationToken)
        {
            int retryCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessStream(cancellationToken);
                    break; // Successful processing
                }
                catch (OperationCanceledException)
                {
                    break; // Intentional cancellation
                }
                catch (Exception ex)
                {
                    retryCount++;
                    IsConnected = false;
                    _logger.LogError($"{_streamName} - Stream error for {_streamUrl} (attempt {retryCount}): {ex.Message}");
                    _viewer.Status = $"Error (Retry {retryCount})";

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(5000, cancellationToken); // Wait 5 seconds before retry
                    }
                }
            }
        }

        private async Task ProcessStream(CancellationToken cancellationToken)
        {
            // Low latency FFmpeg arguments for RTSP
            string arguments;
            if (FFmpegUtils.IsRtmpStream(_streamUrl))
            {
                // RTMP: no -rtsp_transport, use low latency flags if needed
                arguments = $"-fflags nobuffer -flags low_delay -max_delay 0 -i \"{_streamUrl}\" " +
                            $"-vf scale={_displayWidth}:{_displayHeight} -pix_fmt bgr24 -f rawvideo -";
            }
            else if (FFmpegUtils.IsHlsStream(_streamUrl))
            {
                // HLS: no -rtsp_transport, just use the URL
                arguments = $"-re -fflags nobuffer -flags low_delay -i \"{_streamUrl}\" " +
                            $"-vf scale={_displayWidth}:{_displayHeight} -pix_fmt bgr24 -f rawvideo -";
            }
            else
            {
                // RTSP
                arguments = $"-rtsp_transport tcp -fflags nobuffer -flags low_delay -strict experimental " +
                            $"-avioflags direct -fflags discardcorrupt  -flush_packets 1 -max_delay 0 -i \"{_streamUrl}\" " +
                            $"-vf scale={_displayWidth}:{_displayHeight} -pix_fmt bgr24 -f rawvideo -";
            }

            _logger.Log($"ffmpeg arguments: {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = FFmpegUtils.GetFFmpegPath(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            _displayProcess = new Process { StartInfo = startInfo };

            var errorOutput = new System.Text.StringBuilder();
            _displayProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.Log($"{e.Data}");
                    errorOutput.AppendLine(e.Data);
                    if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning($"FFmpeg: {e.Data}");
                    }

                    // Extract resolution from lines like: "Stream #0:0: Video: ... 176x144 ..."
                    var match = Regex.Match(
                                           e.Data,
                                             @"^\s*Stream #0:0: Video: h264.*? (\d{2,5})x(\d{2,5}),",
                                           RegexOptions.Compiled);
                    if (match.Success)
                    {
                        string width = match.Groups[1].Value;
                        string height = match.Groups[2].Value;
                        InputResolution = $"{width}x{height}";
                        _logger.Log($"{_streamName}: Detected source resolution: {InputResolution}");

                    }
                }
            };

            try
            {
                if (!_displayProcess.Start())
                {
                    throw new InvalidOperationException("Failed to start FFmpeg process");
                }

                // Set process priority to AboveNormal or High
                try
                {
                    _displayProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{_streamName} - Could not set FFmpeg process priority: {ex.Message}");
                }

                _displayProcess.BeginErrorReadLine();

                // Wait for process to initialize and start producing output
                await Task.Delay(200, cancellationToken);

                if (_displayProcess.HasExited)
                {
                    throw new InvalidOperationException($"FFmpeg exited early: {errorOutput}");
                }

                int width = _displayWidth;
                int height = _displayHeight;
                int frameSize = width * height * 3; // BGR24 = 3 bytes per pixel

                _viewer.Dispatcher.Invoke(() => {
                    _videoBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
                    _viewer.SetVideoFrame(_videoBitmap);
                });

                var buffer = new byte[frameSize];
                var stream = _displayProcess.StandardOutput.BaseStream;
                var readBuffer = new byte[8192]; // Read buffer for chunks

                _lastFrameTime = DateTime.Now;
                bool firstFrameReceived = false;

                while (!cancellationToken.IsCancellationRequested && !_displayProcess.HasExited)
                {
                    int totalBytesRead = 0;

                    // Read complete frame
                    while (totalBytesRead < frameSize && !cancellationToken.IsCancellationRequested && !_displayProcess.HasExited)
                    {
                        int chunkSize = Math.Min(readBuffer.Length, frameSize - totalBytesRead);
                        int bytesRead = await stream.ReadAsync(readBuffer, 0, chunkSize, cancellationToken);

                        if (bytesRead == 0)
                        {
                            // Check if process is still running
                            if (_displayProcess.HasExited)
                                throw new InvalidOperationException($"FFmpeg process exited unexpectedly: {errorOutput}");

                            await Task.Delay(10, cancellationToken); // Brief delay before retry
                            continue;
                        }

                        Array.Copy(readBuffer, 0, buffer, totalBytesRead, bytesRead);
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead == frameSize)
                    {
                        await ProcessFrame(buffer, width, height);
                        _lastFrameTime = DateTime.Now;

                        if (!firstFrameReceived)
                        {
                            IsConnected = true;
                            _viewer.Status = "Connected";
                            _logger.Log($"{_streamName} - Successfully connected to stream: {_streamUrl}");
                            firstFrameReceived = true;

                            // Start recording if requested and not already running
                            if (_recordingManager.IsRecording && (_displayProcess == null || _displayProcess.HasExited))
                            {
                                _recordingManager.StartRecording();
                            }
                        }
                    }
                }
                // If we exit the loop and never received a frame, treat as failure
                if (!firstFrameReceived)
                {
                    throw new InvalidOperationException($"FFmpeg did not deliver any frames: {errorOutput}");
                }

                // If FFmpeg exited unexpectedly, throw to trigger retry
                if (_displayProcess.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"FFmpeg process exited unexpectedly: {errorOutput}");
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError($"{_streamName} - Stream processing error: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (!_displayProcess.HasExited)
                    {
                        _displayProcess.Kill();
                        // Give process time to exit gracefully
                        if (!_displayProcess.WaitForExit(2000))
                        {
                            _logger.LogWarning($"{_streamName} - FFmpeg process did not exit within timeout for {_streamUrl}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{_streamName} - Error stopping FFmpeg process: {ex.Message}");
                }
                IsConnected = false;
            }
        }

        private async Task ProcessFrame(byte[] frameData, int width, int height)
        {
            if (_disposed) return;

            try
            {
                await _viewer.Dispatcher.InvokeAsync(() =>
                {
                    _videoBitmap.Lock();
                    try
                    {
                        int stride = _videoBitmap.BackBufferStride;
                        IntPtr backBuffer = _videoBitmap.BackBuffer;

                        for (int y = 0; y < height; y++)
                        {
                            IntPtr destLine = backBuffer + y * stride;
                            int srcOffset = y * width * 3;
                            Marshal.Copy(frameData, srcOffset, destLine, width * 3);
                        }

                        _videoBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        _videoBitmap.Unlock();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{_streamName} - Error processing frame for {_streamUrl}: {ex.Message}");
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cancellationTokenSource?.Cancel();

                // Stop recording if active
                _recordingManager.StopRecording();

                // Give the task time to complete gracefully
                if (_streamingTask != null && !_streamingTask.IsCompleted)
                {
                    var completed = _streamingTask.Wait(TimeSpan.FromSeconds(3));
                    if (!completed)
                    {
                        _logger.LogWarning($"{_streamName} - Stream task for {_streamUrl} did not complete within timeout");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{_streamName} - Error during dispose for {_streamUrl}: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                IsConnected = false;
            }
        }
    }
}