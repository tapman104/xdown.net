namespace XDown.Core;

public sealed record DownloadJob(
    string Url,
    string OutputPath,       // full path including filename
    int MaxSegments = 4,     // how many parallel HTTP range requests to use
    string? TempDirectory = null // null = use system temp, else user-specified
);
