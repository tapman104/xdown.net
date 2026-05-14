using System.Security.Cryptography;
using XDown.Core;

namespace XDown.Tests;

public class DownloadServiceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DownloadService _service;

    public DownloadServiceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xdown_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new DownloadService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task DownloadSingleStream_Works()
    {
        string outPath = Path.Combine(_tempDir, "100KB.bin");
        var job = new DownloadJob("http://httpbin.org/bytes/102400", outPath, MaxSegments: 1, TempDirectory: _tempDir);
        
        await _service.DownloadAsync(job, null, CancellationToken.None);

        Assert.True(File.Exists(outPath));
        Assert.Equal(102400, new FileInfo(outPath).Length);
    }

    [Fact]
    public async Task DownloadSegmented_Works()
    {
        string outPath = Path.Combine(_tempDir, "5MB.bin");
        var job = new DownloadJob("http://speed.cloudflare.com/__down?bytes=5242880", outPath, MaxSegments: 4, TempDirectory: _tempDir);

        await _service.DownloadAsync(job, null, CancellationToken.None);

        Assert.True(File.Exists(outPath));
        Assert.Equal(5242880, new FileInfo(outPath).Length);
    }

    [Fact]
    public async Task CancelMidDownload_ThrowsOperationCanceledException()
    {
        string outPath = Path.Combine(_tempDir, "cancel.bin");
        var job = new DownloadJob("http://speed.cloudflare.com/__down?bytes=52428800", outPath, MaxSegments: 4, TempDirectory: _tempDir);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => 
            await _service.DownloadAsync(job, null, cts.Token));
    }

    [Fact]
    public async Task Checksum_Passes_OnCorrectHash()
    {
        string outPath = Path.Combine(_tempDir, "tiny.bin");
        var job1 = new DownloadJob("http://speed.cloudflare.com/__down?bytes=1024", outPath, MaxSegments: 1, TempDirectory: _tempDir);
        await _service.DownloadAsync(job1, null, CancellationToken.None);
        
        string expectedHex;
        using (var fs = File.OpenRead(outPath))
        {
            var hash = await SHA256.HashDataAsync(fs);
            expectedHex = Convert.ToHexString(hash);
        }
        
        File.Delete(outPath);

        var job2 = new DownloadJob("http://speed.cloudflare.com/__down?bytes=1024", outPath, MaxSegments: 1, TempDirectory: _tempDir, ExpectedHash: expectedHex);
        await _service.DownloadAsync(job2, null, CancellationToken.None);

        Assert.True(File.Exists(outPath));
    }

    [Fact]
    public async Task Checksum_Fails_OnWrongHash()
    {
        string outPath = Path.Combine(_tempDir, "fail.bin");
        var job = new DownloadJob("http://speed.cloudflare.com/__down?bytes=1024", outPath, MaxSegments: 1, TempDirectory: _tempDir, ExpectedHash: "sha256:0000000000000000000000000000000000000000000000000000000000000000");

        await Assert.ThrowsAsync<DownloadException>(async () => 
            await _service.DownloadAsync(job, null, CancellationToken.None));
            
        Assert.False(File.Exists(outPath));
    }
}
