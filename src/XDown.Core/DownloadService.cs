using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace XDown.Core;

public sealed class DownloadService
{
    // Minimum file size to bother with segmentation (2MB)
    private const long SegmentThreshold = 2 * 1024 * 1024;
    // Buffer for reading HTTP response body
    private const int ReadBufferSize = 81920; // 80KB

    private readonly HttpClient _http;

    public DownloadService(bool allowInvalidServerCertificates = false)
    {
        var handler = new SocketsHttpHandler();
        if (allowInvalidServerCertificates)
        {
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
        }

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
        await DownloadAsync(job, new DownloadOptions(), progress, ct);
    }

    public async Task DownloadAsync(
        DownloadJob job,
        DownloadOptions? options,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        options ??= new DownloadOptions();

        // Step 1: probe the URL
        var (totalBytes, supportsRanges) = await ProbeAsync(job.Url, ct);

        bool useSegments = supportsRanges
            && totalBytes > SegmentThreshold
            && options.MaxSegments > 1;

        if (useSegments)
            await DownloadSegmentedAsync(job, options, totalBytes, progress, ct);
        else
            await DownloadSingleAsync(job, totalBytes, progress, ct);

        // Step 3: Checksum (Change 5)
        if (!string.IsNullOrWhiteSpace(options.ExpectedHash))
        {
            await VerifyChecksumAsync(job.OutputPath, options.ExpectedHash!, options.HashAlgorithm, ct);
        }
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
                double speed = CalculateSpeedBytesPerSecond(speedQueue, bytesInQueue, nowUtc);
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
    // Splits file into N equal byte ranges and downloads in parallel
    // directly into a pre-allocated output file.
    // ---------------------------------------------------------------
    private async Task DownloadSegmentedAsync(
        DownloadJob job,
        DownloadOptions options,
        long totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        string tempDir = options.TempDirectory ?? Path.GetTempPath();
        string sidecarPath = BuildSidecarPath(tempDir, job);

        int segCount = options.MaxSegments;
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

        bool canResume = options.Resume
            && sidecar != null
            && sidecar.Url == job.Url
            && sidecar.TotalBytes == totalBytes
            && sidecar.Segments.Length == segCount
            && File.Exists(job.OutputPath);

        var sidecarSegments = new XDownSegment[segCount];
        for (int i = 0; i < segCount; i++)
        {
            if (canResume)
            {
                sidecarSegments[i] = sidecar!.Segments[i];
            }
            else
            {
                long start = i * segSize;
                long end = (i == segCount - 1) ? totalBytes - 1 : (start + segSize - 1);
                sidecarSegments[i] = new XDownSegment(start, end, 0);
            }
        }

        if (!canResume)
        {
            // Pre-allocate the final file to full size upfront
            await using (var fs = new FileStream(job.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                fs.SetLength(totalBytes);
            }

            sidecar = new XDownSidecar(job.Url, totalBytes, sidecarSegments);
            string newJson = System.Text.Json.JsonSerializer.Serialize(sidecar, XDownSidecarContext.Default.XDownSidecar);
            await File.WriteAllTextAsync(sidecarPath, newJson, ct);
        }

        long totalReceived = sidecarSegments.Sum(s => s.Completed);
        long[] segmentProgress = sidecarSegments.Select(s => s.Completed).ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = sw.Elapsed;
        var lastSave = sw.Elapsed;
        var reportLock = new object();
        var speedQueue = new Queue<(long bytes, DateTime time)>();
        long bytesInQueue = 0;

        async Task SaveSidecarAsync()
        {
            try
            {
                // Sync current progress back to the sidecar record for saving
                for (int i = 0; i < segCount; i++)
                {
                    sidecar!.Segments[i] = sidecar.Segments[i] with { Completed = segmentProgress[i] };
                }

                string json = System.Text.Json.JsonSerializer.Serialize(sidecar!, XDownSidecarContext.Default.XDownSidecar);
                await File.WriteAllTextAsync(sidecarPath, json, ct);
            }
            catch { }
        }

        void ReportProgress(int index, long delta)
        {
            long current = Interlocked.Add(ref totalReceived, delta);
            Interlocked.Add(ref segmentProgress[index], delta);

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

                // Periodically save sidecar (every 5 seconds) to allow resume after crash
                if ((now - lastSave).TotalSeconds >= 5)
                {
                    lastSave = now;
                    _ = SaveSidecarAsync();
                }

                if ((now - lastReport).TotalMilliseconds < 200) return;

                double speed = CalculateSpeedBytesPerSecond(speedQueue, bytesInQueue, nowUtc);
                TimeSpan? eta = speed > 0
                    ? TimeSpan.FromSeconds((totalBytes - current) / speed)
                    : null;
                progress?.Report(new DownloadProgress(
                    current, totalBytes, speed, sw.Elapsed, eta));
                lastReport = now;
            }
        }

        try
        {
            // Parallel download directly into the pre-allocated final file
            await Task.WhenAll(Enumerable.Range(0, segCount).Select(i =>
                DownloadSegmentAsync(job.Url, sidecarSegments[i].Start, sidecarSegments[i].End, job.OutputPath, i, ReportProgress, ct, sidecarSegments[i].Completed)));
        }
        catch (RangeNotSupportedException)
        {
            // Some servers ignore Range and return 200. Fall back to single stream.
            try { File.Delete(sidecarPath); } catch { }
            await DownloadSingleAsync(job, totalBytes, progress, ct);
            return;
        }

        // Cleanup sidecar upon successful completion
        try { File.Delete(sidecarPath); } catch { }

        progress?.Report(new DownloadProgress(
            totalBytes, totalBytes, 0, sw.Elapsed, TimeSpan.Zero));
    }

    private async Task VerifyChecksumAsync(
        string outputPath,
        string expectedHash,
        HashAlgorithmName? hashAlgorithm,
        CancellationToken ct)
    {
        var algorithm = hashAlgorithm ?? HashAlgorithmName.SHA256;
        byte[] hash;

        // Open file in its own scope to ensure disposal before possible deletion
        {
            await using var fs = new FileStream(outputPath,
                FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: ReadBufferSize, useAsync: true);

            if (algorithm == HashAlgorithmName.SHA256)
                hash = await SHA256.HashDataAsync(fs, ct);
            else if (algorithm == HashAlgorithmName.SHA1)
                hash = await SHA1.HashDataAsync(fs, ct);
            else if (algorithm == HashAlgorithmName.SHA512)
                hash = await SHA512.HashDataAsync(fs, ct);
            else if (algorithm == HashAlgorithmName.MD5)
                hash = await MD5.HashDataAsync(fs, ct);
            else
                throw new NotSupportedException($"Algorithm {algorithm.Name} is not supported.");
        }

        string actualHex = Convert.ToHexString(hash);

        // Handle "sha256:HEX" format
        string expected = expectedHash;
        int colonIndex = expected.IndexOf(':');
        if (colonIndex >= 0)
            expected = expected[(colonIndex + 1)..];

        if (!string.Equals(actualHex, expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(outputPath); } catch { }
            throw new DownloadException($"Checksum mismatch! Expected {expected}, got {actualHex}");
        }
    }

    private async Task DownloadSegmentAsync(
        string url, long start, long end, string outputPath,
        int segmentIndex, Action<int, long> reportProgress, CancellationToken ct, long existingBytes = 0)
    {
        long currentStart = start + existingBytes;
        if (currentStart > end) return;

        await RetryAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(currentStart, end);

            using var resp = await _http.SendAsync(req,
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.OK)
                throw new RangeNotSupportedException();

            if (resp.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException($"Server returned {resp.StatusCode} instead of 206");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            
            // Open the shared file with ReadWrite share to allow other segments to write in parallel
            await using var file = new FileStream(outputPath,
                FileMode.Open, FileAccess.Write, FileShare.ReadWrite,
                bufferSize: ReadBufferSize, useAsync: true);
            
            file.Seek(currentStart, SeekOrigin.Begin);

            var buffer = new byte[ReadBufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                reportProgress(segmentIndex, read);
                currentStart += read;
            }
        }, 4, ct);
    }

    private static double CalculateSpeedBytesPerSecond(
        Queue<(long bytes, DateTime time)> speedQueue,
        long bytesInQueue,
        DateTime nowUtc)
    {
        if (bytesInQueue <= 0 || speedQueue.Count == 0)
            return 0;

        var elapsedSeconds = (nowUtc - speedQueue.Peek().time).TotalSeconds;
        if (elapsedSeconds <= 0)
            return 0;

        return bytesInQueue / elapsedSeconds;
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

                long exponentialDelayMs = Math.Min(30000, (long)Math.Pow(2, attempt - 1) * 1000);
                int jitterMs = Random.Shared.Next(0, 251);
                await Task.Delay(TimeSpan.FromMilliseconds(exponentialDelayMs + jitterMs), ct);
            }
        }
    }

    private static string BuildSidecarPath(string tempDirectory, DownloadJob job)
    {
        string outputFilename = Path.GetFileName(job.OutputPath);
        string identity = $"{job.Url}|{Path.GetFullPath(job.OutputPath)}";
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        return Path.Combine(tempDirectory, $"{outputFilename}.{fingerprint}.xdown");
    }
}

internal sealed record XDownSegment(long Start, long End, long Completed);
internal sealed record XDownSidecar(string Url, long TotalBytes, XDownSegment[] Segments);
internal sealed class RangeNotSupportedException : Exception;

[System.Text.Json.Serialization.JsonSerializable(typeof(XDownSidecar))]
internal partial class XDownSidecarContext : System.Text.Json.Serialization.JsonSerializerContext { }
