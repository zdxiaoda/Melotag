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

    /// <summary>[offset:±毫秒] 头部标签，用于整体平移时间轴。</summary>
    private static readonly Regex OffsetTag = new(@"^\[\s*offset\s*:\s*([+-]?\d+)\s*\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public List<LrcLine> Lines { get; } = [];

    public static LrcDocument Parse(string text)
    {
        var doc = new LrcDocument();

        // 行首可能有被当作正文的 BOM（从网络/子串拼接来的歌词常见）
        var body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        var offsetMs = 0;

        foreach (var raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = new LrcLine();
            var consumed = ParseTimeTags(raw, line);
            line.Text = consumed > 0 ? raw[consumed..] : raw;
            doc.Lines.Add(line);

            if (!line.HasTime && OffsetTag.Match(line.Text.Trim()) is { Success: true } offset &&
                int.TryParse(offset.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                offsetMs += ms;
            }
        }

        // 整体应用 [offset:]，而不是留在正文里由调用方自行处理
        if (offsetMs != 0) doc.Shift(TimeSpan.FromMilliseconds(offsetMs));

        return doc;
    }

    /// <summary>解析行首的时间标签，返回已消费的字符数。</summary>
    private static int ParseTimeTags(string raw, LrcLine line)
    {
        var consumed = 0;

        while (true)
        {
            var m = TimeToken.Match(raw, consumed);
            if (!m.Success || m.Index != consumed) break;

            line.Times.Add(ToTimeSpan(m));
            consumed += m.Length;

            // 时间标签之间可能用空格/制表符分隔（"[00:01.00] [00:02.00]hello"）。
            // 先探测：只有确认后面还有时间标签才提交这些空白，
            // 否则 "[00:01.00] hello" 的正文前导空格会被吃掉。
            var probe = consumed;
            while (probe < raw.Length && raw[probe] is ' ' or '\t') probe++;

            if (probe == consumed) continue;   // 紧邻下一个标签，交给下一轮直接匹配
            if (probe >= raw.Length) break;

            var next = TimeToken.Match(raw, probe);
            if (!next.Success || next.Index != probe) break;

            consumed = probe;
        }

        return consumed;
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

    /// <summary>
    /// 整体平移时间轴。若平移会让最早的一行低于 0，则统一按最早时间截断（等价于把最早一行挪到 0），
    /// 保留行间相对间隔；逐行 clamp 会把前几行压成同一时间点。
    /// </summary>
    public void Shift(TimeSpan delta)
    {
        var earliest = TimeSpan.MaxValue;
        foreach (var line in Lines)
        {
            foreach (var time in line.Times)
            {
                if (time < earliest) earliest = time;
            }
        }

        if (earliest == TimeSpan.MaxValue) return;   // 没有任何时间轴

        if (earliest + delta < TimeSpan.Zero) delta = -earliest;

        foreach (var line in Lines)
        {
            for (var i = 0; i < line.Times.Count; i++)
            {
                line.Times[i] += delta;
            }
        }
    }

    public void RemoveBlankLines() => Lines.RemoveAll(l => string.IsNullOrWhiteSpace(l.Text));

    /// <summary>删除 [ti:]、[ar:]、[offset:] 这类头部元信息行，保留正文时间轴。</summary>
    public void RemoveHeadTags() => Lines.RemoveAll(l => !l.HasTime && HeadTag.IsMatch(l.Text.Trim()));

    /// <summary>把译文按相同时间戳合并到原文行尾（原文 + 连接符 + 译文）。</summary>
    public void MergeTranslation(LrcDocument translated, string connector)
    {
        var byTime = new Dictionary<TimeSpan, string>();
        var untimedTexts = new List<string>();

        foreach (var line in translated.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;

            var text = line.Text.Trim();
            if (!line.HasTime)
            {
                if (HeadTag.IsMatch(text)) continue;
                untimedTexts.Add(text);
                continue;
            }

            foreach (var time in line.Times)
            {
                // 译文重复同一时间戳时追加，不要后者覆盖前者
                byTime[time] = byTime.TryGetValue(time, out var existing)
                    ? existing + connector + text
                    : text;
            }
        }

        var untimedIndex = 0;
        foreach (var line in Lines)
        {
            if (!line.HasTime)
            {
                // 无时间轴的原文行按行序回退匹配
                if (string.IsNullOrWhiteSpace(line.Text) || HeadTag.IsMatch(line.Text.Trim())) continue;
                if (untimedIndex >= untimedTexts.Count) continue;

                AppendTranslation(line, untimedTexts[untimedIndex++], connector);
                continue;
            }

            // 一行多个时间戳时，命中任意一个就把译文合并到该行（ToLrcText 会为每个时间戳输出）
            foreach (var time in line.Times)
            {
                if (!byTime.TryGetValue(time, out var text)) continue;

                AppendTranslation(line, text, connector);
                break;
            }
        }
    }

    private static void AppendTranslation(LrcLine line, string text, string connector) =>
        line.Text = string.IsNullOrWhiteSpace(line.Text) ? text : line.Text.TrimEnd() + connector + text;

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
