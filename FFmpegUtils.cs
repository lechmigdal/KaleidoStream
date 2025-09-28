using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KaleidoStream
{
    internal class FFmpegUtils
    {
        public static string GetFFmpegPath()
        {
            // Try to find FFmpeg in common locations
            var possiblePaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", "ffmpeg.exe"),
                "ffmpeg" // Try system PATH without .exe for cross-platform compatibility
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Try PATH
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where", // Windows command to find executable in PATH
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
                        return firstPath;
                }
            }
            catch
            {
                // Fall through to error
            }

            throw new FileNotFoundException(
                "FFmpeg executable not found. Please:\n" +
                "1. Download FFmpeg from https://ffmpeg.org/download.html\n" +
                "2. Place ffmpeg.exe in your application folder, or\n" +
                "3. Install FFmpeg and add it to your system PATH");
        }


        public static bool IsRtmpStream(string url)
        {
            return url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        }


        public static bool IsHlsStream(string url)
        {
            return url != null && url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        }
    }

   

}
