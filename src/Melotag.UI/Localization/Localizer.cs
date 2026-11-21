namespace Melotag.UI.Localization;

using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Melotag.Core.Services;

/// <summary>
/// 简中 / 繁中 / 英 / 日资源入口。XAML 通过 <see cref="TrExtension"/> 绑定
/// <see cref="LocalizedString.Value"/>，代码通过 <see cref="T"/> / <see cref="F"/> 取文案。
/// 繁中（zh-Hant）不维护独立 resx，读取简中资源后经 OpenCC 运行时转换。
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources =
        new("Melotag.UI.Localization.Strings", typeof(Localizer).Assembly);

    public static Localizer Instance { get; } = new();

    private readonly Dictionary<string, LocalizedString> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hantCache = new(StringComparer.Ordinal);
    private readonly ChineseConversionService _chinese = new();
    private CultureInfo _culture = CultureInfo.GetCultureInfo("zh-Hans");

    private Localizer()
    {
    }

    public string this[string key]
    {
        get
        {
            var culture = _culture.Name == "zh-Hant"
                ? CultureInfo.GetCultureInfo("zh-Hans")
                : _culture;
            var value = Resources.GetString(key, culture) ?? key;
            if (_culture.Name != "zh-Hant") return value;

            if (!_hantCache.TryGetValue(key, out var converted))
            {
                converted = _chinese.ToTraditional(value);
                _hantCache[key] = converted;
            }

            return converted;
        }
    }

    public string CurrentLanguage => _culture.Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    /// <summary>取得某个 key 的可观察字符串，供 XAML 绑定并随语言切换刷新。</summary>
    public LocalizedString Get(string key)
    {
        lock (_strings)
        {
            if (!_strings.TryGetValue(key, out var value))
            {
                value = new LocalizedString(key);
                _strings[key] = value;
            }

            return value;
        }
    }

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args)
    {
        var format = Instance[key];
        return args.Length == 0 ? format : string.Format(Instance._culture, format, args);
    }

    public static string SourceName(string id, string fallback)
    {
        var key = $"Source.{id}";
        var value = Instance[key];
        return value == key ? fallback : value;
    }

    /// <summary>把设置值解析成受支持的语言代码（zh-Hans / ja / en）。</summary>
    public static string ResolveLanguage(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return DetectSystemLanguage();

        return Normalize(setting);
    }

    public void SetLanguage(string language)
    {
        var normalized = ResolveLanguage(language);
        _culture = normalized switch
        {
            "zh-Hans" => CultureInfo.GetCultureInfo("zh-Hans"),
            "zh-Hant" => CultureInfo.GetCultureInfo("zh-Hant"),
            "ja" => CultureInfo.GetCultureInfo("ja"),
            _ => CultureInfo.GetCultureInfo("en"),
        };

        _hantCache.Clear();
        CultureInfo.CurrentUICulture = _culture;

        LocalizedString[] strings;
        lock (_strings)
        {
            strings = [.. _strings.Values];
        }

        foreach (var value in strings)
            value.Refresh();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        LanguageChanged?.Invoke();
    }

    private static string DetectSystemLanguage() =>
        IsHant(CultureInfo.CurrentUICulture.Name)
            ? "zh-Hant"
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
            {
                "zh" => "zh-Hans",
                "ja" => "ja",
                _ => "en",
            };

    private static string Normalize(string setting)
    {
        if (IsHant(setting)) return "zh-Hant";
        if (setting.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (setting.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        return "en";
    }

    /// <summary>繁中相关的语言/地区标记（zh-Hant、zh-TW、zh-HK、zh-MO 及旧式 zh-CHT）。</summary>
    private static bool IsHant(string name) =>
        name.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("zh-tw", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("zh-hk", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("zh-mo", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("zh-cht", StringComparison.OrdinalIgnoreCase);
}

/// <summary>单个资源 key 的可观察值；语言切换时刷新 <see cref="Value"/>。</summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    internal LocalizedString(string key) => Key = key;

    public string Key { get; }

    public string Value => Localizer.Instance[Key];

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
