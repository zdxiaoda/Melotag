namespace Melotag.Core.Rename;

using Melotag.Core.Abstractions;
using Melotag.Core.Models;

public sealed record RenamePreview(IFileHandle Source, string TargetName, string? Error)
{
    public bool IsValid => Error is null && !string.Equals(Source.FileName, TargetName, StringComparison.Ordinal);
    public string SourceName => Source.FileName;
}

public sealed record RenameResult(IFileHandle Old, IFileHandle New, IReadOnlyList<string> RenamedSiblings);

/// <summary>文件改名：模板渲染、冲突规避、连带改名 .lrc/封面。</summary>
public sealed class RenameService
{
    /// <summary>渲染结果为空时的稳定错误标记，供 UI 本地化。</summary>
    public const string EmptyNameError = "empty-name";

    /// <summary>句柄未获得改名权限时的稳定错误标记，供 UI 本地化。</summary>
    public const string NotRenameableError = "not-renameable";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
    };

    private readonly Dictionary<string, HashSet<string>> _nameCache = new(StringComparer.Ordinal);

    /// <summary>清空同目录文件名缓存；对话框每次刷新预览前调用。</summary>
    public void Invalidate() => _nameCache.Clear();

    public RenamePreview Preview(IFileHandle file, TrackTags tags, string pattern, ISafeNamePolicy policy)
    {
        var errors = NamePattern.Validate(pattern, PatternDirection.Generate);
        if (errors.Count > 0)
            return new RenamePreview(file, file.FileName, Describe(errors[0]));

        if (!file.CanRename)
            return new RenamePreview(file, file.FileName, NotRenameableError);

        var rendered = NamePattern.Render(pattern, tags);
        var name = policy.Sanitize(rendered);
        if (string.IsNullOrWhiteSpace(name))
            return new RenamePreview(file, file.FileName, EmptyNameError);

        return new RenamePreview(file, MakeUnique(file, name + file.Extension), null);
    }

    public static string Describe(PatternError error) => error switch
    {
        PatternError.Empty => "模板不能为空",
        PatternError.NoPlaceholder => "模板必须包含至少一个有效占位符（@1–@8）",
        PatternError.OnlyUnused => "不能只使用 @0",
        PatternError.DuplicatePlaceholder => "占位符不能重复",
        PatternError.AdjacentPlaceholders => "相邻占位符会产生歧义",
        PatternError.UnknownPlaceholder => "存在未知占位符（只能使用 @0–@8）",
        _ => "模板无效",
    };

    public RenameResult Apply(RenamePreview preview, bool alsoRenameLyrics, bool alsoRenamePictures)
    {
        if (!preview.IsValid) throw new InvalidOperationException(preview.Error ?? "无变化");

        var source = preview.Source;
        var siblings = source.GetSiblings();
        var oldBase = source.FileNameWithoutExtension;
        var newBase = Path.GetFileNameWithoutExtension(preview.TargetName);
        var renamed = new List<string>();

        var renamedFile = source.Rename(preview.TargetName);

        foreach (var sibling in siblings)
        {
            if (string.Equals(sibling.Key, source.Key, StringComparison.Ordinal)) continue;

            var name = sibling.FileName;
            var ext = sibling.Extension;
            if (!name.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase)) continue;

            var isLyrics = ext.Equals(".lrc", StringComparison.OrdinalIgnoreCase);
            var isImage = ImageExtensions.Contains(ext);
            if ((isLyrics && !alsoRenameLyrics) || (isImage && !alsoRenamePictures) || (!isLyrics && !isImage)) continue;

            var suffix = name[oldBase.Length..];
            var isCompanion = suffix.Length == 0
                || suffix[0] == '.'
                || suffix.StartsWith(" (", StringComparison.Ordinal)
                || suffix.StartsWith("-cover", StringComparison.OrdinalIgnoreCase)
                || suffix.StartsWith("_cover", StringComparison.OrdinalIgnoreCase);
            if (!isCompanion) continue;

            var target = newBase + suffix;
            if (siblings.Any(s => string.Equals(s.FileName, target, StringComparison.OrdinalIgnoreCase))) continue;

            try
            {
                sibling.Rename(target);
                renamed.Add(target);
            }
            catch
            {
                // 单个兄弟文件失败不影响主文件
            }
        }

        Invalidate();
        return new RenameResult(source, renamedFile, renamed);
    }

    /// <summary>按显式文件名（不含扩展名）改名，用于简繁转换等场景。</summary>
    public RenameResult ApplyExact(IFileHandle file, string newBaseName, ISafeNamePolicy policy, bool alsoRenameLyrics, bool alsoRenamePictures)
    {
        if (!file.CanRename) throw new InvalidOperationException(NotRenameableError);

        var name = policy.Sanitize(newBaseName);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("新文件名为空");

        var target = MakeUnique(file, name + file.Extension);
        if (string.Equals(target, file.FileName, StringComparison.Ordinal))
            return new RenameResult(file, file, []);

        return Apply(new RenamePreview(file, target, null), alsoRenameLyrics, alsoRenamePictures);
    }

    private string MakeUnique(IFileHandle file, string targetName)
    {
        if (string.Equals(file.FileName, targetName, StringComparison.OrdinalIgnoreCase))
            return targetName;

        var names = Names(file);
        if (names.Count == 0 || !names.Contains(targetName)) return targetName;

        var stem = Path.GetFileNameWithoutExtension(targetName);
        var ext = Path.GetExtension(targetName);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!names.Contains(candidate)) return candidate;
        }

        return targetName;
    }

    private HashSet<string> Names(IFileHandle file)
    {
        var key = file.DirectoryKey;
        if (key is null) return [];

        if (_nameCache.TryGetValue(key, out var cached)) return cached;

        var names = new HashSet<string>(file.GetSiblings().Select(s => s.FileName), StringComparer.OrdinalIgnoreCase);
        _nameCache[key] = names;
        return names;
    }
}
