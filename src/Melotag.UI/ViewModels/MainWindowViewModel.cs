namespace Melotag.UI.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Melotag.Core.Abstractions;
using Melotag.Core.Files;
using Melotag.Core.Merge;
using Melotag.Core.Models;
using Melotag.Core.Rename;
using Melotag.Core.Services;
using Melotag.Core.Sources;
using Melotag.Storage;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITagStore _tagStore;
    private readonly TagSearchService _search;
    private readonly CoverService _covers;
    private readonly IHistoryStore _historyStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IPlatformHost _host;
    private readonly List<TrackRowViewModel> _all = [];

    public MainWindowViewModel(
        ITagStore tagStore,
        TagSearchService search,
        CoverService covers,
        IHistoryStore historyStore,
        ISettingsStore settingsStore,
        AppSettings settings,
        IPlatformHost? host = null)
    {
        _tagStore = tagStore;
        _search = search;
        _covers = covers;
        _historyStore = historyStore;
        _settingsStore = settingsStore;
        _host = host ?? new FileSystemPlatformHost();
        Settings = settings;

        Http.UserAgent = string.IsNullOrWhiteSpace(settings.Sources.UserAgent)
            ? Http.UserAgent
            : settings.Sources.UserAgent;

        UpdateCounts();
    }

    public AppSettings Settings { get; }
    public TagSearchService Search => _search;
    public IHistoryStore HistoryStore => _historyStore;
    public IPlatformHost Platform => _host;
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];
    public IReadOnlyList<TrackRowViewModel> AllTracks => _all;

    [ObservableProperty] private TrackRowViewModel? selectedTrack;
    [ObservableProperty] private string statusText = "就绪";
    [ObservableProperty] private string countText = "0 个文件";
    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string? value) => ApplyFilter();

    /// <summary>桌面入口：把路径展开成文件句柄（目录递归枚举），再走 <see cref="AddFilesAsync"/>。</summary>
    public Task AddPathsAsync(IEnumerable<string> paths, bool recursive = true)
    {
        var candidates = new List<IFileHandle>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in EnumerateAudioFiles(path, recursive))
                    candidates.Add(new PhysicalFileHandle(file));
            }
            else if (File.Exists(path))
            {
                candidates.Add(new PhysicalFileHandle(path));
            }
        }

        return AddFilesAsync(candidates);
    }

    /// <summary>平台无关入口：接收已经展开好的文件句柄（Android SAF 用这个）。</summary>
    public async Task AddFilesAsync(IEnumerable<IFileHandle> files)
    {
        var known = _all.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var todo = new List<IFileHandle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (!_tagStore.CanHandle(file.Extension)) continue;
            if (known.Contains(file.Key) || !seen.Add(file.Key)) continue;
            todo.Add(file);
        }

        if (todo.Count == 0)
        {
            StatusText = "没有新增文件";
            return;
        }

        StatusText = $"正在读取 {todo.Count} 个文件…";
        var results = await Task.Run(() => todo.Select(SafeRead).ToList());

        foreach (var item in results)
            _all.Add(new TrackRowViewModel(item, Remove));

        ApplyFilter();
        StatusText = $"已读取 {results.Count(r => r.Status == TrackStatus.Loaded)} 个，失败/跳过 {results.Count(r => r.Status is TrackStatus.Failed or TrackStatus.Skipped)} 个";
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_all.Count == 0) return;

        StatusText = "正在重新读取…";
        var handles = _all.Select(t => t.Source ?? (IFileHandle)new PhysicalFileHandle(t.Path)).ToList();
        var results = await Task.Run(() => handles.Select(SafeRead).ToList());

        for (var i = 0; i < results.Count && i < _all.Count; i++)
            _all[i].ApplyFrom(results[i]);

        StatusText = "重新读取完成";
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedTrack is null)
        {
            StatusText = "请先选择一个文件";
            return;
        }

        await SaveRowsAsync([SelectedTrack]);
    }

    public async Task SaveAllAsync()
    {
        var rows = _all.Where(r => r.StatusText is not ("失败" or "跳过")).ToList();
        if (rows.Count == 0)
        {
            StatusText = "没有可保存的文件";
            return;
        }

        await SaveRowsAsync(rows);
    }

    private async Task SaveRowsAsync(IReadOnlyList<TrackRowViewModel> rows)
    {
        var ok = 0;
        var failed = 0;

        foreach (var row in rows)
        {
            StatusText = $"正在保存 {row.FileName}…";
            var tags = row.ToTags();
            var options = new TagWriteOptions(
                Settings.Writing.Id3v2Version,
                Settings.Writing.KeepModifiedTime,
                Settings.Writing.WriteLyricsToTag,
                ReplacePictures: row.PicturesDirty);

            try
            {
                var handle = row.Source ?? (IFileHandle)new PhysicalFileHandle(row.Path);
                if (!handle.IsWritable)
                    throw new InvalidOperationException("该文件只有只读权限，请重新选择并授权写入");

                await Task.Run(() => _tagStore.Write(handle, tags, options));
                await Task.Run(() => _historyStore.AddSnapshot(row.Key, tags));

                row.ApplyFrom(new TrackItem
                {
                    Path = row.Path,
                    DisplayName = row.FileName,
                    Tags = tags,
                    Info = row.Info,
                    Status = TrackStatus.Saved,
                    Source = handle,
                });
                ok++;
            }
            catch (Exception ex)
            {
                row.StatusText = "保存失败";
                row.ErrorText = ex.Message;
                failed++;
            }
        }

        StatusText = failed == 0 ? $"已保存 {ok} 个文件" : $"已保存 {ok} 个，失败 {failed} 个";
    }

    public async Task ClearTagsAsync()
    {
        var rows = _all.Where(r => r.StatusText is not ("失败" or "跳过")).ToList();
        foreach (var row in rows)
        {
            row.ApplyTags(new TrackTags());
            row.ClearCover();
        }

        await Task.CompletedTask;
        StatusText = $"已清空 {rows.Count} 个文件的标签（尚未写入）";
    }

    /// <summary>从列表移除一行（只影响列表，不改文件）。</summary>
    public void Remove(TrackRowViewModel row)
    {
        var wasSelected = ReferenceEquals(SelectedTrack, row);

        _all.Remove(row);
        Tracks.Remove(row);

        if (wasSelected) SelectedTrack = null;

        UpdateCounts();
        StatusText = "已从列表移除";
    }

    [RelayCommand]
    private Task RemoveSelectedAsync()
    {
        if (SelectedTrack is null) return Task.CompletedTask;

        Remove(SelectedTrack);
        return Task.CompletedTask;
    }

    public async Task ApplyCandidateAsync(TrackRowViewModel row, TagCandidate candidate, OverwriteRule rule, bool includeCover = true)
    {
        var merged = TagMerger.Merge(row.ToTags(), candidate.ToTags(), new OverwriteOptions { Default = rule });
        row.ApplyTags(merged);

        if (includeCover && !string.IsNullOrWhiteSpace(candidate.CoverUrl) && rule != OverwriteRule.KeepExisting)
        {
            var source = _search.Find(candidate.SourceId);
            if (source is not null)
            {
                var bytes = await source.DownloadCoverAsync(candidate.CoverUrl, CancellationToken.None);
                if (bytes is not null)
                {
                    var normalized = _covers.Normalize(
                        bytes,
                        Settings.Pictures.MaxWidth,
                        Settings.Pictures.MaxHeight,
                        Settings.Pictures.MaxSizeKb,
                        Settings.Pictures.AllowedFormats);

                    if (normalized is not null)
                        row.SetCover(normalized.Data, normalized.MimeType);
                }
            }
        }

        StatusText = $"已应用 {candidate.SourceId} 的候选，保存后写入文件";
    }

    /// <summary>批量自动匹配：每首歌取第一个候选，按"只填空字段"合并（封面留到单独操作）。</summary>
    public async Task AutoMatchAllAsync()
    {
        var rows = _all.Where(r => r.StatusText is not ("失败" or "跳过")).ToList();
        if (rows.Count == 0)
        {
            StatusText = "没有可匹配的文件";
            return;
        }

        var hit = 0;
        var miss = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            StatusText = $"自动匹配 {index + 1}/{rows.Count}：{row.FileName}";

            var query = new TagQuery
            {
                Title = row.Title,
                Artist = row.Artist,
                Album = row.Album,
                FileName = row.FileName,
                Duration = row.Info?.Duration,
            };

            try
            {
                var results = await _search.SearchAsync(
                    query, SourceCapability.Tags, Settings.Sources.Tags,
                    Settings.Search.ItemsPerSource, Settings.Search.Threads, CancellationToken.None);

                var best = results.FirstOrDefault();
                if (best is null)
                {
                    miss++;
                    continue;
                }

                await ApplyCandidateAsync(row, best, OverwriteRule.FillEmptyOnly, includeCover: false);
                hit++;
            }
            catch
            {
                miss++;
            }
        }

        StatusText = $"自动匹配完成：命中 {hit}，无结果 {miss}（保存后写入文件）";
    }

    /// <summary>把已有封面导出到音频同目录，命名为 "文件名-cover.jpg/png"。</summary>
    public async Task ExtractCoversAsync()
    {
        var rows = _all.Where(r => r.Pictures.Count > 0).ToList();
        if (rows.Count == 0)
        {
            StatusText = "没有可提取的封面";
            return;
        }

        var count = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            var picture = row.Pictures[0];
            var extension = picture.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var handle = row.Source ?? (IFileHandle)new PhysicalFileHandle(row.Path);

            try
            {
                var target = await _host.ExportCoverAsync(handle, picture.Data, extension);
                if (target is null) failed++;
                else count++;
            }
            catch
            {
                failed++;
            }
        }

        StatusText = failed == 0
            ? $"已提取 {count} 个封面"
            : $"已提取 {count} 个封面，失败 {failed} 个";
    }

    private readonly ChineseConversionService _chinese = new();

    /// <summary>把列表中所有标签做简繁转换（只改内存，保存后写入）。</summary>
    public Task ConvertTagsAsync(bool toTraditional)
    {
        foreach (var row in _all)
        {
            Convert(row);
        }

        StatusText = toTraditional ? "标签已转为繁体（保存后写入）" : "标签已转为简体（保存后写入）";
        return Task.CompletedTask;

        void Convert(TrackRowViewModel row)
        {
            string? C(string? value) => value is null ? null : toTraditional
                ? _chinese.ToTraditional(value)
                : _chinese.ToSimplified(value);

            row.Title = C(row.Title);
            row.Artist = C(row.Artist);
            row.Album = C(row.Album);
            row.AlbumArtist = C(row.AlbumArtist);
            row.Genre = C(row.Genre);
            row.Comment = C(row.Comment);
            row.Composer = C(row.Composer);
        }
    }

    /// <summary>把列表中的文件名做简繁转换（直接改磁盘文件）。</summary>
    public Task ConvertFileNamesAsync(bool toTraditional, ISafeNamePolicy policy)
    {
        if (!_host.SupportsRename)
        {
            StatusText = "当前平台不支持重命名文件，可在电脑端完成";
            return Task.CompletedTask;
        }

        var renamed = 0;
        var failed = 0;
        var service = new RenameService();

        foreach (var row in _all.ToList())
        {
            var baseName = Path.GetFileNameWithoutExtension(row.FileName);
            var converted = toTraditional ? _chinese.ToTraditional(baseName) : _chinese.ToSimplified(baseName);
            if (string.Equals(baseName, converted, StringComparison.Ordinal)) continue;

            try
            {
                var result = service.ApplyExact(row.Path, converted, policy,
                    alsoRenameLyrics: _host.SupportsSiblingFiles,
                    alsoRenamePictures: _host.SupportsSiblingFiles);
                if (result.NewPath != result.OldPath)
                {
                    row.RenameTo(result.NewPath);
                    renamed++;
                }
            }
            catch
            {
                failed++;
            }
        }

        StatusText = $"文件名转换完成：重命名 {renamed} 个" + (failed > 0 ? $"，失败 {failed} 个" : "");
        return Task.CompletedTask;
    }

    public async Task RestoreHistoryAsync(TrackRowViewModel row, HistoryEntry entry)
    {
        try
        {
            var handle = row.Source ?? (IFileHandle)new PhysicalFileHandle(row.Path);
            await Task.Run(() => _tagStore.Write(
                handle,
                entry.Tags,
                new TagWriteOptions(Settings.Writing.Id3v2Version, Settings.Writing.KeepModifiedTime, Settings.Writing.WriteLyricsToTag)));

            var item = await Task.Run(() => _tagStore.Read(handle));
            row.ApplyFrom(item);
            row.StatusText = "已恢复";
            StatusText = $"已恢复到 {entry.CreatedAt:HH:mm:ss} 的记录";
        }
        catch (Exception ex)
        {
            StatusText = "恢复失败：" + ex.Message;
        }
    }

    public void SaveSettings() => _settingsStore.Save(Settings);

    private void ApplyFilter()
    {
        var selectedKey = SelectedTrack?.Key;
        var filter = FilterText?.Trim();

        Tracks.Clear();
        foreach (var track in _all)
        {
            if (string.IsNullOrEmpty(filter) || Matches(track, filter))
                Tracks.Add(track);
        }

        SelectedTrack = Tracks.FirstOrDefault(t => t.Key == selectedKey)
                        ?? Tracks.FirstOrDefault();
        UpdateCounts();
    }

    private static bool Matches(TrackRowViewModel row, string filter) =>
        Contains(row.FileName, filter) || Contains(row.Title, filter) ||
        Contains(row.Artist, filter) || Contains(row.Album, filter);

    private static bool Contains(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EnumerateAudioFiles(string directory, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, "*", option))
        {
            if (_tagStore.CanHandle(Path.GetExtension(file).ToLowerInvariant()))
                yield return file;
        }
    }

    private TrackItem SafeRead(IFileHandle handle)
    {
        try
        {
            if (!_tagStore.CanHandle(handle.Extension))
            {
                return new TrackItem
                {
                    Path = handle.DisplayPath,
                    DisplayName = handle.FileName,
                    Status = TrackStatus.Skipped,
                    Error = "不支持的格式",
                    Source = handle,
                };
            }

            return _tagStore.Read(handle);
        }
        catch (Exception ex)
        {
            return new TrackItem
            {
                Path = handle.DisplayPath,
                DisplayName = handle.FileName,
                Status = TrackStatus.Failed,
                Error = ex.Message,
                Source = handle,
            };
        }
    }

    private void UpdateCounts() =>
        CountText = _all.Count == Tracks.Count
            ? $"{_all.Count} 个文件"
            : $"{Tracks.Count} / {_all.Count} 个文件";
}
