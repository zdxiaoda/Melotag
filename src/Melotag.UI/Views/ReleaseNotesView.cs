namespace Melotag.UI.Views;

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

/// <summary>
/// 把 GitHub Release 的 Markdown 说明渲染为本地控件。
/// 只覆盖自动生成说明实际用到的一小部分语法：标题、列表、行内粗体、代码与链接。
/// </summary>
internal static partial class ReleaseNotesView
{
    private const double MaxHeight = 260;

    private static readonly Regex InlineToken = new(
        @"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)",
        RegexOptions.Compiled);

    public static Control Build(string markdown)
    {
        var panel = new StackPanel { Spacing = 4 };

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("### ", StringComparison.Ordinal)) { AddHeading(panel, line[4..], 14); continue; }
            if (line.StartsWith("## ", StringComparison.Ordinal)) { AddHeading(panel, line[3..], 16); continue; }
            if (line.StartsWith("# ", StringComparison.Ordinal)) { AddHeading(panel, line[2..], 18); continue; }

            var bullet = line.StartsWith("- ", StringComparison.Ordinal)
                         || line.StartsWith("* ", StringComparison.Ordinal);
            var text = bullet ? line[2..] : line;

            var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
            if (bullet)
                block.Inlines!.Add(new Run { Text = "•  " });
            AddInline(block, text);
            panel.Children.Add(block);
        }

        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = MaxHeight,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static void AddHeading(StackPanel panel, string text, double size) =>
        panel.Children.Add(new TextBlock
        {
            Text = PlainText(text),
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 2),
        });

    private static string PlainText(string text) =>
        InlineToken.Replace(text, match =>
            match.Groups["bold"].Success ? match.Groups["bold"].Value
            : match.Groups["code"].Success ? match.Groups["code"].Value
            : match.Groups["text"].Value);

    private static void AddInline(TextBlock block, string text)
    {
        var index = 0;
        foreach (Match match in InlineToken.Matches(text))
        {
            if (match.Index > index)
                block.Inlines!.Add(new Run { Text = text[index..match.Index] });

            if (match.Groups["bold"].Success)
                block.Inlines!.Add(new Run { Text = match.Groups["bold"].Value, FontWeight = FontWeight.Bold });
            else if (match.Groups["code"].Success)
                block.Inlines!.Add(new Run { Text = match.Groups["code"].Value });
            else
                block.Inlines!.Add(new Run { Text = match.Groups["text"].Value });

            index = match.Index + match.Length;
        }

        if (index < text.Length)
            block.Inlines!.Add(new Run { Text = text[index..] });
    }
}
