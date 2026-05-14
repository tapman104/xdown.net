using XDown.Core;

namespace XDown.Tests;

public class DownloadProgressTests
{
    [Fact]
    public void DownloadProgress_RecordEquality_Works()
    {
        var p1 = new DownloadProgress(100, 200, 10.5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        var p2 = new DownloadProgress(100, 200, 10.5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void DownloadProgress_Properties_AssignedCorrectly()
    {
        var p = new DownloadProgress(100, -1, 0, TimeSpan.FromSeconds(1), null);

        Assert.Equal(100, p.BytesReceived);
        Assert.Equal(-1, p.TotalBytes);
        Assert.Equal(0, p.SpeedBytesPerSec);
        Assert.Equal(TimeSpan.FromSeconds(1), p.Elapsed);
        Assert.Null(p.ETA);
    }
}
