using System;
using System.IO;
using System.Threading.Tasks;

namespace XDown.App.Services;

/// <summary>
/// Append-only log file for download history at %AppData%\XDown\downloads.log
/// </summary>
public class LogService
{
    private readonly string _logPath;

    public LogService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xdownDir = Path.Combine(appDataPath, "XDown");
        _logPath = Path.Combine(xdownDir, "downloads.log");

        // Ensure directory exists
        Directory.CreateDirectory(xdownDir);
    }

    /// <summary>
    /// Log a successful download.
    /// Format: 2026-05-15 22:13 | 21.1 MB | 2.3 MB/s | Sleep.Logger_1.0.apk | https://github.com/...
    /// </summary>
    public async Task LogDownloadSuccessAsync(string fileName, string url, long totalBytes, double speedBytesPerSec)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var sizeStr = FormatBytes(totalBytes);
            var speedStr = FormatBytes((long)speedBytesPerSec);
            var line = $"{timestamp} | {sizeStr} | {speedStr}/s | {fileName} | {url}";
            await AppendLineAsync(line);
        }
        catch
        {
            // Silently fail on log errors
        }
    }

    /// <summary>
    /// Log a failed download.
    /// Format: 2026-05-15 22:14 | FAILED | Sleep.Logger_1.0.apk | Checksum mismatch
    /// </summary>
    public async Task LogDownloadFailureAsync(string fileName, string reason)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var line = $"{timestamp} | FAILED | {fileName} | {reason}";
            await AppendLineAsync(line);
        }
        catch
        {
            // Silently fail on log errors
        }
    }

    private async Task AppendLineAsync(string line)
    {
        try
        {
            // Use FileStream with FileShare to allow concurrent access
            using (var file = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file))
            {
                await writer.WriteLineAsync(line);
            }
        }
        catch
        {
            // Silently fail
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.0} {sizes[order]}";
    }
}
