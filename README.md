# KaleidoStream

Multi-stream viewer and recorder for Windows 10 and Windows 11.

Supported stream types: RTMP, RTSP, HLS (.m3u8)

## Features
- View multiple video streams in a dynamic grid
- Record any stream to disk (MPEG-TS format)
- Light and dark theme support
- Per-stream resolution selection
- Context menu for stream actions (start/stop, record)
- YAML-based configuration

## Installation
1. Download and install [ffmpeg](https://ffmpeg.org/) (version 8.0+ recommended). Place `ffmpeg.exe` in the application folder or add it to your system PATH.
2. Install [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime). (optional if you use the self-contained build))

## Usage
- Launch `KaleidoStream.exe`.
- Right-click a stream for options
- Use the menu to change theme or resolution.
- Logs are saved in the `logs` directory.
- Videos are saves in the 'videos' directory.

## Troubleshooting
- If streams do not display, check that `ffmpeg.exe` is accessible and your stream URLs are correct.
- For errors, see the log files in the `logs` folder.
- For support, open an issue on [GitHub](https://github.com/lechmigdal/KaleidoStream).

## License
[MIT](LICENSE.txt)