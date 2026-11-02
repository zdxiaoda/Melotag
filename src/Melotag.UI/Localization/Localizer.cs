namespace Melotag.UI.Localization;

using System.ComponentModel;
using System.Globalization;
using System.Resources;

/// <summary>
/// 三语（简中 / 日 / 英）资源入口。XAML 通过 <see cref="TrExtension"/> 绑定
/// <see cref="LocalizedString.Value"/>，代码通过 <see cref="T"/> / <see cref="F"/> 取文案。
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources =
        new("Melotag.UI.Localization.Strings", typeof(Localizer).Assembly);

    public static Localizer Instance { get; } = new();

    private readonly Dictionary<string, LocalizedString> _strings = new(StringComparer.Ordinal);
    private CultureInfo _culture = CultureInfo.GetCultureInfo("zh-Hans");

    private Localizer()
    {
    }

    public string this[string key] => Resources.GetString(key, _culture) ?? key;

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
            "ja" => CultureInfo.GetCultureInfo("ja"),
            _ => CultureInfo.GetCultureInfo("en"),
        };

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
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "zh" => "zh-Hans",
            "ja" => "ja",
            _ => "en",
        };

    private static string Normalize(string setting)
    {
        if (setting.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (setting.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        return "en";
    }
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
