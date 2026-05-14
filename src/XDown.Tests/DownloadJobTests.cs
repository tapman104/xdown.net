using XDown.Core;

namespace XDown.Tests;

public class DownloadJobTests
{
    [Fact]
    public void DownloadJob_DefaultValues_AreCorrect()
    {
        var job = new DownloadJob("http://example.com", "out.bin");

        Assert.Equal("http://example.com", job.Url);
        Assert.Equal("out.bin", job.OutputPath);
        Assert.Equal(4, job.MaxSegments);
        Assert.Null(job.TempDirectory);
        Assert.Null(job.ExpectedHash);
        Assert.Null(job.HashAlgorithm);
    }
}
