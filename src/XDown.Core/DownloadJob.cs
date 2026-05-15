namespace XDown.Core;

public sealed record DownloadJob(
    string Url,
    string OutputPath // full path including filename
);
