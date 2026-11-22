namespace Melotag.Core.Services;

using OpenCC;

/// <summary>简繁转换（OpenCC 纯 C# 移植，MIT）。tw2 使用台湾日常用词规则。</summary>
public sealed class ChineseConversionService
{
    private static readonly Lazy<Func<string, string>> ToTraditionalConverter =
        new(() => OpenCC.Converter("cn", "tw2"), isThreadSafe: true);

    private static readonly Lazy<Func<string, string>> ToSimplifiedConverter =
        new(() => OpenCC.Converter("tw2", "cn"), isThreadSafe: true);

    /// <summary>
    /// OpenCC 的转换器是第三方实现，没有公开的线程安全保证（内部词典/缓冲可能被复用）；
    /// 批量任务会并发调用转换，这里用实例级锁把转换调用串行化，代价远小于偶发的数据错乱。
    /// </summary>
    private readonly object _gate = new();

    public string ToTraditional(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        lock (_gate) return ToTraditionalConverter.Value(text);
    }

    public string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        lock (_gate) return ToSimplifiedConverter.Value(text);
    }
}
