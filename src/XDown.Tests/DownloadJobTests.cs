using XDown.Core;

namespace XDown.Tests;

public class DownloadJobTests
{
    [Fact]
    public void DownloadJob_StoresUrlAndOutputPath()
    {
        var job = new DownloadJob("http://example.com", "out.bin");

        Assert.Equal("http://example.com", job.Url);
        Assert.Equal("out.bin", job.OutputPath);
    }

    [Fact]
    public void DownloadOptions_DefaultValues_AreCorrect()
    {
        var options = new DownloadOptions();

        Assert.Equal(4, options.MaxSegments);
        Assert.Null(options.TempDirectory);
        Assert.Null(options.ExpectedHash);
        Assert.Null(options.HashAlgorithm);
        Assert.True(options.Resume);
    }
}
