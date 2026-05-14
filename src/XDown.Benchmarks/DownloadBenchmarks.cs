using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using XDown.Core;

namespace XDown.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, iterationCount: 3)] // Minimal iterations to avoid being rate limited by Cloudflare
public class DownloadBenchmarks
{
    private DownloadService _service = null!;
    private string _tempDir = null!;

    [Params(1, 4, 8)]
    public int Segments { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _service = new DownloadService();
        _tempDir = Path.Combine(Path.GetTempPath(), "xdown_benchmarks_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Benchmark(Description = "Download10MB")]
    public async Task DownloadAsync()
    {
        string outPath = Path.Combine(_tempDir, $"bench_{Guid.NewGuid():N}.bin");
        var job = new DownloadJob("http://speed.cloudflare.com/__down?bytes=10485760", outPath, MaxSegments: Segments, TempDirectory: _tempDir);
        
        await _service.DownloadAsync(job, null, CancellationToken.None);
        
        if (File.Exists(outPath)) File.Delete(outPath);
    }
}
