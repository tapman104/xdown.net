using System.Security.Cryptography;

namespace XDown.Core;

public sealed record DownloadJob(
    string Url,
    string OutputPath,       // full path including filename
    int MaxSegments = 4,     // how many parallel HTTP range requests to use
    string? TempDirectory = null, // null = use system temp, else user-specified
    string? ExpectedHash = null, // e.g. "sha256:abc123..."
    HashAlgorithmName? HashAlgorithm = null // default handled in service (SHA256)
);
