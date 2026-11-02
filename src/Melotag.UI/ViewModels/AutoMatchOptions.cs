namespace Melotag.UI.ViewModels;

using Melotag.Core.Merge;
using Melotag.Core.Models;

/// <summary>批量自动匹配的用户选择：覆盖方式、字段、封面开关与在线源顺序。</summary>
public sealed record AutoMatchOptions(
    OverwriteRule Rule,
    IReadOnlySet<FieldKey> Fields,
    bool IncludeCover,
    IReadOnlyList<string> SourceOrder);
