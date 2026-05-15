using System.Security.Cryptography;

namespace XDown.Core;

public sealed record DownloadOptions(
    int MaxSegments = 4,
    string? TempDirectory = null,
    string? ExpectedHash = null,
    HashAlgorithmName? HashAlgorithm = null,
    bool Resume = true
);
