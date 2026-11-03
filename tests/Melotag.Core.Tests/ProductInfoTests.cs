namespace Melotag.Core.Tests;

using Melotag.Core;

public class ProductInfoTests
{
    [Fact]
    public void Version_IsSemanticVersion()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ProductInfo.Version);
    }

    [Fact]
    public void UserAgent_UsesCurrentVersionAndRepository()
    {
        Assert.Equal($"Melotag/{ProductInfo.Version} (+{ProductInfo.RepositoryUrl})", ProductInfo.UserAgent);
    }

    [Fact]
    public void RepositoryUrl_IsNotPlaceholder()
    {
        Assert.StartsWith("https://github.com/", ProductInfo.RepositoryUrl);
        Assert.DoesNotContain("example.com", ProductInfo.RepositoryUrl, StringComparison.OrdinalIgnoreCase);
    }
}
