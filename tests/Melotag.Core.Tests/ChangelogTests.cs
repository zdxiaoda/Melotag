namespace Melotag.Core.Tests;

using Melotag.Core;

public class ChangelogTests
{
    private const string Markdown = """
    # Changelog

    ## 1.4.0

    ### 新增

    - 新功能 A

    ## 1.3.2

    ### 修复

    - 修复 B

    ### 工程

    - 工程 C

    ## 1.3.1

    - 旧版本 D
    """;

    [Fact]
    public void ExtractSection_ReturnsOnlyRequestedVersion()
    {
        var section = Changelog.ExtractSection(Markdown, "1.3.2");

        Assert.NotNull(section);
        Assert.Contains("修复 B", section);
        Assert.Contains("工程 C", section);
        Assert.Contains("### 修复", section);
        Assert.DoesNotContain("新功能 A", section);
        Assert.DoesNotContain("旧版本 D", section);
        Assert.DoesNotContain("## 1.3.2", section);
    }

    [Fact]
    public void ExtractSection_LastSectionRunsToEnd()
    {
        var section = Changelog.ExtractSection(Markdown, "1.3.1");

        Assert.NotNull(section);
        Assert.Contains("旧版本 D", section);
    }

    [Fact]
    public void ExtractSection_AcceptsVPrefixAndBrackets()
    {
        const string bracketed = """
        ## [1.4.0] - 2026-11-04

        - 版本说明
        """;

        var section = Changelog.ExtractSection(bracketed, "v1.4.0");

        Assert.NotNull(section);
        Assert.Contains("版本说明", section);
        Assert.NotNull(Changelog.ExtractSection(Markdown, "v1.4.0"));
    }

    [Fact]
    public void ExtractSection_MissingVersion_ReturnsNull()
    {
        Assert.Null(Changelog.ExtractSection(Markdown, "9.9.9"));
        Assert.Null(Changelog.ExtractSection(Markdown, ""));
    }

    [Fact]
    public void EmbeddedChangelog_IsReadable()
    {
        var section = Changelog.ForVersion("1.3.0");

        Assert.NotNull(section);
        Assert.Contains("SAF", section);
    }
}
