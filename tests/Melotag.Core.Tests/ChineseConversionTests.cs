namespace Melotag.Core.Tests;

using Melotag.Core.Services;

public class ChineseConversionTests
{
    [Fact]
    public void ToTraditional_ConvertsSimple()
    {
        var service = new ChineseConversionService();
        Assert.Equal("漢語", service.ToTraditional("汉语"));
    }

    [Fact]
    public void ToSimplified_ConvertsTraditional()
    {
        var service = new ChineseConversionService();
        Assert.Equal("汉语", service.ToSimplified("漢語"));
    }

    [Fact]
    public void EmptyInput_IsReturnedAsIs()
    {
        var service = new ChineseConversionService();
        Assert.Equal("", service.ToTraditional(""));
        Assert.Equal("", service.ToSimplified(""));
    }

    [Fact]
    public void EnglishText_IsUnchanged()
    {
        var service = new ChineseConversionService();
        Assert.Equal("Get Lucky", service.ToTraditional("Get Lucky"));
    }
}
