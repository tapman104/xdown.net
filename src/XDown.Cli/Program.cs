using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XDown.Core;
using XDown.Core.Helpers;

namespace XDown.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? url = null;
        string? output = null;
        int segments = 4;
        string? hash = null;
        string? tempDir = null;
        bool noProgress = false;

        // Manual argument parsing
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url":
                    if (i + 1 < args.Length) url = args[++i];
                    else return BadArguments("Missing value for --url");
                    break;
                case "--output":
                    if (i + 1 < args.Length) output = args[++i];
                    else return BadArguments("Missing value for --output");
                    break;
                case "--segments":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int segs)) segments = segs;
                    else return BadArguments("Invalid or missing value for --segments");
                    break;
                case "--hash":
                    if (i + 1 < args.Length) hash = args[++i];
                    else return BadArguments("Missing value for --hash");
                    break;
                case "--temp-dir":
                    if (i + 1 < args.Length) tempDir = args[++i];
                    else return BadArguments("Missing value for --temp-dir");
                    break;
                case "--no-progress":
                    noProgress = true;
                    break;
                default:
                    return BadArguments($"Unknown argument: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return BadArguments("Argument --url is required.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            output = UrlHelper.DeriveFilename(url);
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent process from terminating immediately
            if (!noProgress)
            {
                Console.WriteLine(); // Clear the \r progress line
            }
            Console.Error.WriteLine("Cancelled.");
            cts.Cancel();
        };

        var job = new DownloadJob(url, output, segments, tempDir, hash);
        var service = new DownloadService();

        IProgress<DownloadProgress>? progressReporter = null;
        if (!noProgress)
        {
            progressReporter = new Progress<DownloadProgress>(p =>
            {
                // Format: [=====>    ] 45% | 2.1 MB/s | ETA 00:32
                
                int percent = p.TotalBytes > 0 ? (int)(p.BytesReceived * 100 / p.TotalBytes) : 0;
                
                int barLength = 10;
                int filledLength = p.TotalBytes > 0 ? (int)(p.BytesReceived * barLength / p.TotalBytes) : 0;
                
                string bar = new string('=', filledLength);
                if (filledLength < barLength && p.TotalBytes > 0)
                {
                    bar += ">";
                    bar = bar.PadRight(barLength, ' ');
                }
                else if (p.TotalBytes == 0)
                {
                    bar = new string(' ', barLength);
                }

                double speedMb = p.SpeedBytesPerSec / (1024.0 * 1024.0);
                string etaString = p.ETA.HasValue 
                    ? $"{(int)p.ETA.Value.TotalMinutes:D2}:{p.ETA.Value.Seconds:D2}" 
                    : "--:--";

                string outputStr = $"\r[{bar}] {percent}% | {speedMb:F1} MB/s | ETA {etaString}";
                // Pad to overwrite previous longer lines
                Console.Write(outputStr.PadRight(60));
            });
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await service.DownloadAsync(job, progressReporter, cts.Token);
            sw.Stop();
            
            if (!noProgress)
            {
                Console.WriteLine();
            }

            var fileInfo = new FileInfo(output);
            string formattedSize = FormatBytes(fileInfo.Length);
            
            // XDown.Core.DownloadService creates a final progress report with full completion info,
            // but we don't necessarily have the final timespan easily from outside unless we track it.
            Console.WriteLine($"Done. Saved to {output} ({formattedSize} in {sw.Elapsed.TotalSeconds:F1}s)");
            
            return 0; // Success
        }
        catch (OperationCanceledException)
        {
            return 1; // Ctrl+C prints Cancelled., we just exit with 1
        }
        catch (DownloadException ex) when (ex.Message.Contains("Checksum mismatch"))
        {
            if (!noProgress) Console.WriteLine();
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2; // Checksum mismatch
        }
        catch (Exception ex)
        {
            if (!noProgress) Console.WriteLine();
            Console.Error.WriteLine($"Error: {ex.ToString()}");
            return 1; // Download failed
        }
    }

    private static int BadArguments(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine("Usage: XDown.Cli --url <url> [--output <path>] [--segments <int>] [--hash <sha256:...>] [--temp-dir <dir>] [--no-progress]");
        return 3;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
