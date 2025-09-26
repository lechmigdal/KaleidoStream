using System;
using System.Diagnostics;
using System.IO;

namespace KaleidoStream
{
    public class FFmpegRecordingManager
    {
        private Process _recordingProcess;
        private string _recordingFilePath;
        private bool _isRecording;
        private bool _recordingRequested;
        private readonly string _streamName;
        private readonly string _streamUrl;
        private readonly Logger _logger;

        public bool IsRecording => _isRecording;

        public FFmpegRecordingManager(Logger logger, string streamUrl, string streamName)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _streamUrl = streamUrl ?? throw new ArgumentNullException(nameof(streamUrl));
            _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        }

        public void StartRecording()
        {
            if (_isRecording) return;

            try
            {
                string videosDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "videos");
                Directory.CreateDirectory(videosDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{_streamName}_{timestamp}.ts";
                _recordingFilePath = System.IO.Path.Combine(videosDir, fileName);
                _logger.Log($"Recording path: {_recordingFilePath}");

                // FFmpeg command for recording MPEG-TS
                string arguments;
                if (FFmpegUtils.IsRtmpStream(_streamUrl))
                {
                    arguments = $"-fflags nobuffer -flush_packets 1 -fflags +genpts -i \"{_streamUrl}\" -c copy -f mpegts \"{_recordingFilePath}\"";
                }
                else
                {
                    arguments = $"-rtsp_transport tcp -fflags nobuffer -flush_packets 1 -fflags +genpts -i \"{_streamUrl}\" -c copy -f mpegts \"{_recordingFilePath}\"";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = FFmpegUtils.GetFFmpegPath(),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                };

                _recordingProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _recordingProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _logger.Log($"FFmpeg Record: {e.Data}");
                };

                _recordingProcess.Exited += (sender, e) =>
                {
                    // This runs on a threadpool thread, so be careful with UI access
                    if (_recordingRequested)
                    {
                        _logger.LogWarning($"Recording process exited unexpectedly, restarting recording...");
                        _isRecording = false;
                        // Restart recording on the UI thread if needed
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            StartRecording();
                        });
                    }
                };

                if (_recordingProcess.Start())
                {
                    _recordingProcess.BeginErrorReadLine();
                    _isRecording = true;
                    _logger.Log($"Started recording to {_recordingFilePath}");
                }
                else
                {
                    _logger.LogError("Failed to start FFmpeg recording process.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting recording: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (!_isRecording || _recordingProcess == null) return;

            try
            {

                // Close standard input to signal FFmpeg to finish
                _recordingProcess.StandardInput.Close();

                // Wait for FFmpeg to flush and exit
                if (!_recordingProcess.WaitForExit(2000))
                {
                    _logger.LogWarning($"FFmpeg recording process did not exit within timeout, killing...");
                    _recordingProcess.Kill();
                    _recordingProcess.WaitForExit(2000);
                }

                _logger.Log($"Stopped recording. File saved: {_recordingFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping recording: {ex.Message}");
            }
            finally
            {
                _isRecording = false;
                _recordingProcess?.Dispose();
                _recordingProcess = null;
                _recordingFilePath = null;
            }
        }

        public void RequestRecordingStart()
        {
            _recordingRequested = true;
            StartRecording();
        }

        public void RequestRecordingStop()
        {
            _recordingRequested = false;
            StopRecording();
        }

    }
}