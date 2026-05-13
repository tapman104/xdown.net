using System.Net;

namespace XDown.Core;

public sealed class DownloadService
{
    // Minimum file size to bother with segmentation (2MB)
    private const long SegmentThreshold = 2 * 1024 * 1024;
    // Buffer for reading HTTP response body
    private const int ReadBufferSize = 81920; // 80KB

    private readonly HttpClient _http;

    public DownloadService()
    {
        // SocketsHttpHandler is fully AOT-safe and cross-platform
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("XDown/1.0");
    }

    public async Task DownloadAsync(
        DownloadJob job,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // Step 1: probe the URL
        var (totalBytes, supportsRanges) = await ProbeAsync(job.Url, ct);

        bool useSegments = supportsRanges
            && totalBytes > SegmentThreshold
            && job.MaxSegments > 1;

        if (useSegments)
            await DownloadSegmentedAsync(job, totalBytes, progress, ct);
        else
            await DownloadSingleAsync(job, totalBytes, progress, ct);
    }

    // ---------------------------------------------------------------
    // PROBE
    // Send HEAD request. Return (contentLength, acceptsRanges).
    // contentLength = -1 if unknown.
    // ---------------------------------------------------------------
    private async Task<(long ContentLength, bool AcceptsRanges)> ProbeAsync(
        string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var resp = await _http.SendAsync(req, ct);

        long len = resp.Content.Headers.ContentLength ?? -1;
        bool ranges = resp.Headers.AcceptRanges.Contains("bytes");
        return (len, ranges);
    }

    // ---------------------------------------------------------------
    // SINGLE STREAM DOWNLOAD
    // Simple GET, write to file, report progress every ~200ms.
    // ---------------------------------------------------------------
    private async Task DownloadSingleAsync(
        DownloadJob job,
        long totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var resp = await _http.GetAsync(job.Url,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(job.OutputPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: ReadBufferSize, useAsync: true);

        var buffer = new byte[ReadBufferSize];
        long received = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        var speedQueue = new Queue<(long bytes, DateTime time)>();
        long bytesInQueue = 0;

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            var nowUtc = DateTime.UtcNow;
            speedQueue.Enqueue((read, nowUtc));
            bytesInQueue += read;

            var cutoff = nowUtc.AddSeconds(-5);
            while (speedQueue.Count > 0 && speedQueue.Peek().time < cutoff)
            {
                bytesInQueue -= speedQueue.Dequeue().bytes;
            }

            // Report at most ~5x per second
            if ((sw.Elapsed - lastReport).TotalMilliseconds >= 200)
            {
                double speed = bytesInQueue / 5.0;
                TimeSpan? eta = (totalBytes > 0 && speed > 0)
                    ? TimeSpan.FromSeconds((totalBytes - received) / speed)
                    : null;

                progress?.Report(new DownloadProgress(
                    received, totalBytes, speed, sw.Elapsed, eta));

                lastReport = sw.Elapsed;
            }
        }

        // Final report
        progress?.Report(new DownloadProgress(
            received, totalBytes, 0, sw.Elapsed, TimeSpan.Zero));
    }

    // ---------------------------------------------------------------
    // SEGMENTED DOWNLOAD
    // Splits file into N equal byte ranges, downloads in parallel,
    // merges .partN temp files into final output.
    // ---------------------------------------------------------------
    private async Task DownloadSegmentedAsync(
        DownloadJob job,
        long totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        string tempDir = job.TempDirectory ?? Path.GetTempPath();
        string outputFilename = Path.GetFileName(job.OutputPath);
        string sidecarPath = Path.Combine(tempDir, $"{outputFilename}.xdown");

        int segCount = job.MaxSegments;
        long segSize = totalBytes / segCount;

        XDownSidecar? sidecar = null;
        if (File.Exists(sidecarPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(sidecarPath, ct);
                sidecar = System.Text.Json.JsonSerializer.Deserialize(json, XDownSidecarContext.Default.XDownSidecar);
            }
            catch { }
        }

        bool canResume = sidecar != null
            && sidecar.Url == job.Url
            && sidecar.TotalBytes == totalBytes
            && sidecar.Segments.Length == segCount;

        var segments = new (long Start, long End, string TempPath, long ExistingBytes)[segCount];
        var sidecarSegments = new XDownSegment[segCount];

        for (int i = 0; i < segCount; i++)
        {
            long start = i * segSize;
            long end = (i == segCount - 1) ? totalBytes - 1 : (start + segSize - 1);
            string tempPath = Path.Combine(tempDir, $"{outputFilename}.part{i}");
            
            long existingBytes = 0;
            if (canResume && File.Exists(tempPath))
            {
                existingBytes = new FileInfo(tempPath).Length;
                if (start + existingBytes > end + 1)
                    existingBytes = (end - start) + 1;
            }
            else
            {
                try { File.Delete(tempPath); } catch { }
            }

            segments[i] = (start, end, tempPath, existingBytes);
            sidecarSegments[i] = new XDownSegment(start, end, existingBytes);
        }

        if (!canResume)
        {
            sidecar = new XDownSidecar(job.Url, totalBytes, sidecarSegments);
            string newJson = System.Text.Json.JsonSerializer.Serialize(sidecar, XDownSidecarContext.Default.XDownSidecar);
            await File.WriteAllTextAsync(sidecarPath, newJson, ct);
        }

        long totalReceived = segments.Sum(s => s.ExistingBytes);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        var reportLock = new object();
        var speedQueue = new Queue<(long bytes, DateTime time)>();
        long bytesInQueue = 0;

        void ReportProgress(long delta)
        {
            long current = Interlocked.Add(ref totalReceived, delta);
            var nowUtc = DateTime.UtcNow;

            lock (reportLock)
            {
                speedQueue.Enqueue((delta, nowUtc));
                bytesInQueue += delta;

                var cutoff = nowUtc.AddSeconds(-5);
                while (speedQueue.Count > 0 && speedQueue.Peek().time < cutoff)
                {
                    bytesInQueue -= speedQueue.Dequeue().bytes;
                }

                var now = sw.Elapsed;
                if ((now - lastReport).TotalMilliseconds < 200) return;

                double speed = bytesInQueue / 5.0;
                TimeSpan? eta = speed > 0
                    ? TimeSpan.FromSeconds((totalBytes - current) / speed)
                    : null;
                progress?.Report(new DownloadProgress(
                    current, totalBytes, speed, sw.Elapsed, eta));
                lastReport = now;
            }
        }

        await Task.WhenAll(segments.Select(seg =>
            DownloadSegmentAsync(job.Url, seg.Start, seg.End, seg.TempPath, ReportProgress, ct, seg.ExistingBytes)));

        await using var final = new FileStream(job.OutputPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: ReadBufferSize, useAsync: true);

        foreach (var (_, _, tempPath, _) in segments)
        {
            await using var part = new FileStream(tempPath,
                FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: ReadBufferSize, useAsync: true);
            await part.CopyToAsync(final, ct);
        }

        foreach (var (_, _, tempPath, _) in segments)
            try { File.Delete(tempPath); } catch { }
        try { File.Delete(sidecarPath); } catch { }

        progress?.Report(new DownloadProgress(
            totalBytes, totalBytes, 0, sw.Elapsed, TimeSpan.Zero));
    }

    private async Task DownloadSegmentAsync(
        string url, long start, long end, string tempPath,
        Action<long> reportProgress, CancellationToken ct, long existingBytes = 0)
    {
        long currentStart = start + existingBytes;

        await RetryAsync(async () =>
        {
            if (currentStart > end) return;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(currentStart, end);

            using var resp = await _http.SendAsync(req,
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var mode = currentStart == start ? FileMode.Create : FileMode.Append;
            await using var file = new FileStream(tempPath,
                mode, FileAccess.Write, FileShare.None,
                bufferSize: ReadBufferSize, useAsync: true);

            var buffer = new byte[ReadBufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                reportProgress(read);
                currentStart += read;
            }
        }, 4, ct);
    }

    private static async Task RetryAsync(Func<Task> operation, int maxAttempts, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await operation();
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
            {
                attempt++;
                if (attempt >= maxAttempts)
                    throw;

                int delaySeconds = 1 << (attempt - 1);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
        }
    }
}

internal sealed record XDownSegment(long Start, long End, long Completed);
internal sealed record XDownSidecar(string Url, long TotalBytes, XDownSegment[] Segments);

[System.Text.Json.Serialization.JsonSerializable(typeof(XDownSidecar))]
internal partial class XDownSidecarContext : System.Text.Json.Serialization.JsonSerializerContext { }
