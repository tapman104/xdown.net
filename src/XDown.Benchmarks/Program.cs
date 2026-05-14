using BenchmarkDotNet.Running;

namespace XDown.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<DownloadBenchmarks>();
    }
}
