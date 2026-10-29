namespace Melotag.Core.Rename;

using System.Text.RegularExpressions;

/// <summary>按平台区分的安全文件名规则。三端的非法字符集合不同，必须注入。</summary>
public interface ISafeNamePolicy
{
    string Sanitize(string name);
}

public sealed class WindowsSafeNamePolicy : ISafeNamePolicy
{
    private static readonly char[] Invalid = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public string Sanitize(string name)
    {
        var chars = name.Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        result = result.TrimEnd('.', ' ');

        var stem = Path.GetFileNameWithoutExtension(result);
        if (Reserved.Contains(stem))
            result = "_" + result;

        return result.Length == 0 ? "_" : result;
    }
}

/// <summary>Linux/Android(SAF) 通用规则：只有 '/' 和 NUL 非法，但为跨平台一致性仍做保守替换。</summary>
public sealed class PortableSafeNamePolicy : ISafeNamePolicy
{
    private static readonly char[] Invalid = ['/', '\0', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public string Sanitize(string name)
    {
        var result = new string(name.Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        return result.Length == 0 ? "_" : result;
    }
}
