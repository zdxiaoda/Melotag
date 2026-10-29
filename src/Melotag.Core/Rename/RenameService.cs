namespace Melotag.Core.Rename;

using Melotag.Core.Models;

public sealed record RenamePreview(string SourcePath, string TargetPath, string? Error)
{
    public bool IsValid => Error is null && !string.Equals(SourcePath, TargetPath, StringComparison.Ordinal);
    public string SourceName => Path.GetFileName(SourcePath);
    public string TargetName => Path.GetFileName(TargetPath);
}

public sealed record RenameResult(string OldPath, string NewPath, IReadOnlyList<string> RenamedSiblings);

/// <summary>文件改名：模板渲染、冲突规避、连带改名 .lrc/封面。</summary>
public sealed class RenameService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
    };

    public RenamePreview Preview(string path, TrackTags tags, string pattern, ISafeNamePolicy policy)
    {
        var errors = NamePattern.Validate(pattern, PatternDirection.Generate);
        if (errors.Count > 0)
            return new RenamePreview(path, path, Describe(errors[0]));

        var rendered = NamePattern.Render(pattern, tags);
        var name = policy.Sanitize(rendered);
        if (string.IsNullOrWhiteSpace(name))
            return new RenamePreview(path, path, "生成的文件名为空");

        var directory = Path.GetDirectoryName(path) ?? ".";
        var extension = Path.GetExtension(path);
        var target = Path.Combine(directory, name + extension);

        return new RenamePreview(path, MakeUnique(target, path), null);
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

        var oldPath = preview.SourcePath;
        var newPath = preview.TargetPath;
        var renamed = new List<string>();

        var oldBase = Path.GetFileNameWithoutExtension(oldPath);
        var newBase = Path.GetFileNameWithoutExtension(newPath);
        var directory = Path.GetDirectoryName(oldPath) ?? ".";

        File.Move(oldPath, newPath);

        foreach (var sibling in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(sibling);
            var ext = Path.GetExtension(sibling);
            if (string.Equals(sibling, newPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase)) continue;

            var isLyrics = ext.Equals(".lrc", StringComparison.OrdinalIgnoreCase);
            var isImage = ImageExtensions.Contains(ext);
            if ((isLyrics && !alsoRenameLyrics) || (isImage && !alsoRenamePictures) || (!isLyrics && !isImage)) continue;

            var suffix = name[oldBase.Length..];
            var target = Path.Combine(directory, newBase + suffix);
            if (File.Exists(target)) continue;

            File.Move(sibling, target);
            renamed.Add(target);
        }

        return new RenameResult(oldPath, newPath, renamed);
    }

    /// <summary>按显式文件名（不含扩展名）改名，用于简繁转换等场景。</summary>
    public RenameResult ApplyExact(string path, string newBaseName, ISafeNamePolicy policy, bool alsoRenameLyrics, bool alsoRenamePictures)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var extension = Path.GetExtension(path);
        var name = policy.Sanitize(newBaseName);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("新文件名为空");

        var target = MakeUnique(Path.Combine(directory, name + extension), path);
        return Apply(new RenamePreview(path, target, null), alsoRenameLyrics, alsoRenamePictures);
    }

    private static string MakeUnique(string target, string source)
    {
        if (!File.Exists(target) || string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
            return target;

        var dir = Path.GetDirectoryName(target) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return target;
    }
}
