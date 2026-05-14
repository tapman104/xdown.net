using XDown.Core.Helpers;

namespace XDown.Tests;

public class UrlHelperTests
{
    [Fact]
    public void DeriveFilename_ValidUrl_ReturnsFilename()
    {
        var result = UrlHelper.DeriveFilename("https://example.com/file.zip");
        Assert.Equal("file.zip", result);
    }

    [Fact]
    public void DeriveFilename_UrlWithQueryString_IgnoresQuery()
    {
        var result = UrlHelper.DeriveFilename("https://example.com/file.zip?token=123");
        Assert.Equal("file.zip", result);
    }

    [Fact]
    public void DeriveFilename_TrailingSlash_ReturnsDefault()
    {
        var result = UrlHelper.DeriveFilename("https://example.com/dir/");
        Assert.Equal("download.bin", result);
    }

    [Fact]
    public void DeriveFilename_GarbageString_ReturnsDefault()
    {
        var result = UrlHelper.DeriveFilename("not a url");
        Assert.Equal("download.bin", result);
    }

    [Fact]
    public void DeriveFilename_NullOrEmpty_ReturnsDefault()
    {
        Assert.Equal("download.bin", UrlHelper.DeriveFilename(""));
        Assert.Equal("download.bin", UrlHelper.DeriveFilename(null!));
    }
}
