using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.IO;

namespace KaleidoStream
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Set culture to OS UI language
            var culture = CultureInfo.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;

            // Check if ffmpeg is installed
            if (!IsFfmpegInstalled())
            {
                var result = MessageBox.Show(
                    "FFmpeg is required to run this application.\n\n" +
                    "Please download and install FFmpeg from:\nhttps://ffmpeg.org/\n\n" +
                    "Click OK to exit.",
                    "FFmpeg Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Shutdown();
                return;
            }

            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, ex) =>
            {
                MessageBox.Show($"KaleidoStream - Unhandled exception: {ex.ExceptionObject}", "KaleidoStream Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (sender, ex) =>
            {
                MessageBox.Show($"KaleidoStream - UI thread exception: {ex.Exception.Message}", "KaleidoStream Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            base.OnStartup(e);
        }

        private bool IsFfmpegInstalled()
        {
            // Check common locations
            string[] possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", "ffmpeg.exe"),
                "ffmpeg.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return true;
            }

            // Try to find ffmpeg in PATH
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "ffmpeg",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var firstPath = output.Split('\n')[0].Trim();
                    if (File.Exists(firstPath))
                        return true;
                }
            }
            catch
            {
                // Ignore errors, will return false
            }

            return false;
        }
    }
}