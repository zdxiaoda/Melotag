namespace Melotag.Core.Lyrics;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed class LrcLine
{
    public List<TimeSpan> Times { get; } = [];
    public string Text { get; set; } = string.Empty;
    public bool HasTime => Times.Count > 0;
}

/// <summary>LRC 解析与处理：重排/移除/偏移时间标签、去空行、去头部标签、合并译文。</summary>
public sealed class LrcDocument
{
    private static readonly Regex TimeToken = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex HeadTag = new(@"^\[[A-Za-z][A-Za-z0-9_-]*:[^\]]*\]$", RegexOptions.Compiled);

    public List<LrcLine> Lines { get; } = [];

    public static LrcDocument Parse(string text)
    {
        var doc = new LrcDocument();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = new LrcLine();
            var rest = raw;
            var consumed = 0;

            // 行首连续的 [mm:ss.xx] 都属于同一行
            while (true)
            {
                var m = TimeToken.Match(rest, consumed);
                if (!m.Success || m.Index != consumed) break;
                line.Times.Add(ToTimeSpan(m));
                consumed += m.Length;
            }

            line.Text = consumed > 0 ? raw[consumed..] : raw;
            doc.Lines.Add(line);
        }

        return doc;
    }

    private static TimeSpan ToTimeSpan(Match m)
    {
        var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var fraction = m.Groups[3].Success ? m.Groups[3].Value : "0";
        var ms = fraction.Length switch
        {
            1 => int.Parse(fraction, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fraction, CultureInfo.InvariantCulture) * 10,
            _ => int.Parse(fraction[..3], CultureInfo.InvariantCulture),
        };
        return new TimeSpan(0, 0, minutes, seconds, ms);
    }

    private static string FormatTime(TimeSpan t)
    {
        var totalMinutes = (int)t.TotalMinutes;
        var hundredths = t.Milliseconds / 10;
        return $"[{totalMinutes:00}:{t.Seconds:00}.{hundredths:00}]";
    }

    /// <summary>把「一行多时间戳」拆成逐行，并按时间升序（无时间的元信息行排在末尾）。</summary>
    public void ReformatTimeTags()
    {
        var expanded = new List<LrcLine>(Lines.Count);
        foreach (var line in Lines)
        {
            if (line.Times.Count <= 1)
            {
                expanded.Add(line);
                continue;
            }

            foreach (var time in line.Times)
            {
                var copy = new LrcLine { Text = line.Text };
                copy.Times.Add(time);
                expanded.Add(copy);
            }
        }

        var timed = expanded.Where(l => l.HasTime).OrderBy(l => l.Times[0]).ToList();
        var untimed = expanded.Where(l => !l.HasTime).ToList();
        timed.AddRange(untimed);

        Lines.Clear();
        Lines.AddRange(timed);
    }

    public void RemoveTimeTags()
    {
        foreach (var line in Lines) line.Times.Clear();
    }

    public void Shift(TimeSpan delta)
    {
        foreach (var line in Lines)
        {
            for (var i = 0; i < line.Times.Count; i++)
            {
                var shifted = line.Times[i] + delta;
                line.Times[i] = shifted < TimeSpan.Zero ? TimeSpan.Zero : shifted;
            }
        }
    }

    public void RemoveBlankLines() => Lines.RemoveAll(l => string.IsNullOrWhiteSpace(l.Text));

    /// <summary>删除 [ti:]、[ar:]、[offset:] 这类头部元信息行，保留正文时间轴。</summary>
    public void RemoveHeadTags() => Lines.RemoveAll(l => !l.HasTime && HeadTag.IsMatch(l.Text.Trim()));

    /// <summary>把译文按相同时间戳合并到原文行尾（原文 + 连接符 + 译文）。</summary>
    public void MergeTranslation(LrcDocument translated, string connector)
    {
        var map = new Dictionary<TimeSpan, string>();
        foreach (var line in translated.Lines)
        {
            if (!line.HasTime || string.IsNullOrWhiteSpace(line.Text)) continue;
            map[line.Times[0]] = line.Text.Trim();
        }

        foreach (var line in Lines)
        {
            if (!line.HasTime) continue;
            if (!map.TryGetValue(line.Times[0], out var text)) continue;
            if (string.IsNullOrWhiteSpace(text)) continue;
            line.Text = string.IsNullOrWhiteSpace(line.Text) ? text : line.Text.TrimEnd() + connector + text;
        }
    }

    public string ToLrcText()
    {
        var sb = new StringBuilder();
        foreach (var line in Lines)
        {
            if (line.Times.Count == 0)
            {
                sb.Append(line.Text).Append('\n');
                continue;
            }

            foreach (var time in line.Times)
                sb.Append(FormatTime(time)).Append(line.Text).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    public string ToPlainText() =>
        string.Join(Environment.NewLine, Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => l.Text.TrimEnd()));
}
