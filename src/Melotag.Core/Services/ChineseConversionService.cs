namespace Melotag.Core.Services;

using OpenCC;

/// <summary>简繁转换（OpenCC 纯 C# 移植，MIT）。tw2 使用台湾日常用词规则。</summary>
public sealed class ChineseConversionService
{
    private static readonly Lazy<Func<string, string>> ToTraditionalConverter =
        new(() => OpenCC.Converter("cn", "tw2"), isThreadSafe: true);

    private static readonly Lazy<Func<string, string>> ToSimplifiedConverter =
        new(() => OpenCC.Converter("tw2", "cn"), isThreadSafe: true);

    public string ToTraditional(string text) =>
        string.IsNullOrEmpty(text) ? text : ToTraditionalConverter.Value(text);

    public string ToSimplified(string text) =>
        string.IsNullOrEmpty(text) ? text : ToSimplifiedConverter.Value(text);
}
