namespace XDown.Core;

public sealed record DownloadProgress(
    long BytesReceived,      // total bytes written so far (all segments combined)
    long TotalBytes,         // -1 if unknown (server didn't send Content-Length)
    double SpeedBytesPerSec, // rolling average over the active 5-second window
    TimeSpan Elapsed,
    TimeSpan? ETA            // null if TotalBytes == -1
);
