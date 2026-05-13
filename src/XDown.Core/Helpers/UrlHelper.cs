namespace XDown.Core.Helpers;

public static class UrlHelper
{
    public static string DeriveFilename(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
        }
        catch
        {
            return "download.bin";
        }
    }
}
