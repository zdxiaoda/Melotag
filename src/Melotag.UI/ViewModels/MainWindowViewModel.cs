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
using Melotag.UI.Localization;

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

        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        StatusText = Localizer.T("Status.Ready");
        UpdateCounts();
    }

    private void OnLanguageChanged()
    {
        foreach (var row in _all)
            row.RefreshLocalizedText();

        UpdateCounts();
        OnPropertyChanged(nameof(AppVersionText));
        StatusText = Localizer.T("Status.Ready");
    }

    public AppSettings Settings { get; }
    public TagSearchService Search => _search;
    public IHistoryStore HistoryStore => _historyStore;
    public IPlatformHost Platform => _host;
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];
    public IReadOnlyList<TrackRowViewModel> AllTracks => _all;

    /// <summary>状态栏版本号，避免在 XAML 里硬编码。</summary>
    public string AppVersionText => UI.AppInfo.VersionLine();

    [ObservableProperty] private TrackRowViewModel? selectedTrack;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string countText = "";
    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

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
            StatusText = Localizer.T("Status.NoNewFiles");
            return;
        }

        StatusText = Localizer.F("Status.LoadingFiles", todo.Count);
        var results = await Task.Run(() => todo.Select(SafeRead).ToList());

        foreach (var item in results)
            _all.Add(new TrackRowViewModel(item, Remove));

        ApplyFilter();
        StatusText = Localizer.F(
            "Status.LoadedFiles",
            results.Count(r => r.Status == TrackStatus.Loaded),
            results.Count(r => r.Status is TrackStatus.Failed or TrackStatus.Skipped));
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_all.Count == 0) return;

        StatusText = Localizer.T("Status.Reloading");
        var handles = _all.Select(t => t.Source ?? (IFileHandle)new PhysicalFileHandle(t.Path)).ToList();
        var results = await Task.Run(() => handles.Select(SafeRead).ToList());

        for (var i = 0; i < results.Count && i < _all.Count; i++)
            _all[i].ApplyFrom(results[i]);

        StatusText = Localizer.T("Status.Reloaded");
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedTrack is null)
        {
            StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        await SaveRowsAsync([SelectedTrack]);
    }

    public async Task SaveAllAsync()
    {
        var rows = _all.Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped)).ToList();
        if (rows.Count == 0)
        {
            StatusText = Localizer.T("Status.NoSavableFiles");
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
            StatusText = Localizer.F("Status.Saving", row.FileName);
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
                    throw new InvalidOperationException(Localizer.T("Status.ReadOnlyFile"));

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
                row.Status = TrackStatus.Failed;
                row.ErrorText = ex.Message;
                failed++;
            }
        }

        StatusText = failed == 0
            ? Localizer.F("Status.Saved", ok)
            : Localizer.F("Status.SavedPartial", ok, failed);
    }

    public async Task ClearTagsAsync()
    {
        var rows = _all.Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped)).ToList();
        foreach (var row in rows)
        {
            row.ApplyTags(new TrackTags());
            row.ClearCover();
        }

        await Task.CompletedTask;
        StatusText = Localizer.F("Status.ClearedTags", rows.Count);
    }

    /// <summary>从列表移除一行（只影响列表，不改文件）。</summary>
    public void Remove(TrackRowViewModel row)
    {
        var wasSelected = ReferenceEquals(SelectedTrack, row);

        _all.Remove(row);
        Tracks.Remove(row);

        if (wasSelected) SelectedTrack = null;

        UpdateCounts();
        StatusText = Localizer.T("Status.RemovedFromList");
    }

    /// <summary>批量从列表移除（Delete 键）。</summary>
    public void RemoveRange(IEnumerable<TrackRowViewModel> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;

        foreach (var row in list)
        {
            _all.Remove(row);
            Tracks.Remove(row);
        }

        if (SelectedTrack is not null && list.Contains(SelectedTrack))
            SelectedTrack = Tracks.FirstOrDefault();

        UpdateCounts();
        StatusText = list.Count == 1
            ? Localizer.T("Status.RemovedFromList")
            : Localizer.F("Status.RemovedMany", list.Count);
    }

    [RelayCommand]
    private Task RemoveSelectedAsync()
    {
        if (SelectedTrack is null) return Task.CompletedTask;

        Remove(SelectedTrack);
        return Task.CompletedTask;
    }

    public Task ApplyCandidateAsync(TrackRowViewModel row, TagCandidate candidate, OverwriteRule rule, bool includeCover = true) =>
        ApplyCandidateAsync(row, candidate, new OverwriteOptions { Default = rule }, includeCover);

    public async Task ApplyCandidateAsync(TrackRowViewModel row, TagCandidate candidate, OverwriteOptions overwrite, bool includeCover = true)
    {
        var merged = TagMerger.Merge(row.ToTags(), candidate.ToTags(), overwrite);
        row.ApplyTags(merged);

        if (includeCover && !string.IsNullOrWhiteSpace(candidate.CoverUrl) && overwrite.Default != OverwriteRule.KeepExisting)
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

        StatusText = Localizer.F("Status.AppliedCandidate", candidate.SourceId);
    }

    /// <summary>批量自动匹配：每首歌取第一个候选，按用户选择的字段、源和覆盖方式合并。</summary>
    public async Task AutoMatchAllAsync(AutoMatchOptions? options = null)
    {
        options ??= new AutoMatchOptions(
            OverwriteRule.Replace,
            Enum.GetValues<FieldKey>().ToHashSet(),
            IncludeCover: false,
            Settings.Sources.Tags);

        var rows = _all.Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped)).ToList();
        if (rows.Count == 0)
        {
            StatusText = Localizer.T("Status.NoMatchFiles");
            return;
        }

        var overwrite = new OverwriteOptions { Default = options.Rule };
        foreach (var key in Enum.GetValues<FieldKey>())
            if (!options.Fields.Contains(key))
                overwrite.PerField[key] = OverwriteRule.Ignore;

        var capability = SourceCapability.Tags
            | (options.IncludeCover ? SourceCapability.Cover : SourceCapability.None)
            | (options.Fields.Contains(FieldKey.Lyrics) ? SourceCapability.Lyrics : SourceCapability.None);

        var hit = 0;
        var miss = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            StatusText = Localizer.F("Status.AutoMatching", index + 1, rows.Count, row.FileName);

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
                    query, capability, options.SourceOrder,
                    Settings.Search.ItemsPerSource, Settings.Search.Threads, CancellationToken.None);

                var best = results.FirstOrDefault();
                if (best is null)
                {
                    miss++;
                    continue;
                }

                await ApplyCandidateAsync(row, best, overwrite, options.IncludeCover);
                hit++;
            }
            catch
            {
                miss++;
            }
        }

        StatusText = Localizer.F("Status.AutoMatchDone", hit, miss);
    }

    /// <summary>把已有封面导出到音频同目录，命名为 "文件名-cover.jpg/png"。</summary>
    public async Task ExtractCoversAsync()
    {
        var rows = _all.Where(r => r.Pictures.Count > 0).ToList();
        if (rows.Count == 0)
        {
            StatusText = Localizer.T("Status.NoCovers");
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
            ? Localizer.F("Status.ExtractedCovers", count)
            : Localizer.F("Status.ExtractedCoversPartial", count, failed);
    }

    private readonly ChineseConversionService _chinese = new();

    /// <summary>把列表中所有标签做简繁转换（只改内存，保存后写入）。</summary>
    public Task ConvertTagsAsync(bool toTraditional)
    {
        foreach (var row in _all)
        {
            Convert(row);
        }

        StatusText = Localizer.T(toTraditional ? "Status.TagsToTraditional" : "Status.TagsToSimplified");
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
            StatusText = Localizer.T("Status.RenameUnsupported");
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

        StatusText = failed > 0
            ? Localizer.F("Status.FileNameConvertedPartial", renamed, failed)
            : Localizer.F("Status.FileNameConverted", renamed);
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
            row.Status = TrackStatus.Saved;
            StatusText = Localizer.F("Status.Restored", entry.CreatedAt.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            StatusText = Localizer.F("Status.RestoreFailed", ex.Message);
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
                    Error = Localizer.T("Status.UnsupportedFormat"),
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
            ? Localizer.F("Count.Files", _all.Count)
            : Localizer.F("Count.Filtered", Tracks.Count, _all.Count);
}
