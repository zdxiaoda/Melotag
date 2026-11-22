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

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ITagStore _tagStore;
    private readonly TagSearchService _search;
    private readonly CoverService _covers;
    private readonly IHistoryStore _historyStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IPlatformHost _host;
    private readonly List<TrackRowViewModel> _all = [];

    /// <summary>分批读取，边读边显示；每批之间让出 UI 线程。</summary>
    private const int LoadBatchSize = 40;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _autoMatchCts;
    private CancellationTokenSource? _convertCts;
    private int _operationVersion;
    private TrackSortField? _sortField;

    // 批量操作守卫：重复点击直接返回，避免两次操作并发写同一批文件。
    private bool _isSaving;
    private bool _isAutoMatching;
    private bool _isConverting;

    // 恢复 DataGrid 原生选中项期间，忽略绑定回写的临时 null，避免封面反复解码。
    private int _selectionRestoreDepth;
    private TrackRowViewModel? _previewRow;
    private bool _disposed;

    /// <summary>列表数据变化后请求桌面 DataGrid 重新应用当前原生排序（移动端不订阅）。</summary>
    public event Action? SortRefreshRequested;

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

        _sortField = ParseSortField(settings.Ui.SortColumn);
        SortOptions = BuildSortOptions();

        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        StatusText = Localizer.T("Status.Ready");
        UpdateCounts();
    }

    private void OnLanguageChanged()
    {
        foreach (var row in _all)
            row.RefreshLocalizedText();

        SortOptions = BuildSortOptions();
        OnPropertyChanged(nameof(SortOptions));
        UpdateCounts();
        OnPropertyChanged(nameof(AppVersionText));
        StatusText = Localizer.T("Status.Ready");
    }

    private static TrackSortField? ParseSortField(string? id) =>
        TrackColumns.TryParseSortField(id, out var field) ? field : null;

    private static IReadOnlyList<SortOption> BuildSortOptions() =>
        TrackColumns.All
            .Select(id => new SortOption(
                Enum.Parse<TrackSortField>(id),
                Localizer.T(TrackColumns.LabelKey(id))))
            .ToList();

    public AppSettings Settings { get; }
    public TagSearchService Search => _search;
    public IHistoryStore HistoryStore => _historyStore;
    public IPlatformHost Platform => _host;

    /// <summary>供文件夹导入时按扩展名过滤。</summary>
    public bool CanHandle(string extension) => _tagStore.CanHandle(extension);

    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];
    public IReadOnlyList<TrackRowViewModel> AllTracks => _all;

    /// <summary>状态栏版本号，避免在 XAML 里硬编码。</summary>
    public string AppVersionText => UI.AppInfo.VersionLine();

    /// <summary>移动端排序面板的字段列表（标签随语言刷新）。</summary>
    public IReadOnlyList<SortOption> SortOptions { get; private set; } = [];

    public TrackSortField? SortField => _sortField;

    public bool SortDescending => Settings.Ui.SortDescending;

    /// <summary>正在增量调整 Tracks 顺序；移动端据此避免 Move 事件导致的详情页误关闭。</summary>
    public bool IsReordering { get; private set; }

    /// <summary>设置排序字段并立即重排列表；null 表示恢复导入顺序。</summary>
    public void SetSort(TrackSortField? field, bool descending)
    {
        _sortField = field;
        Settings.Ui.SortColumn = field?.ToString() ?? "";
        Settings.Ui.SortDescending = field is not null && descending;
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(SortDescending));
        ApplyFilter();
        SaveSettings();
    }

    /// <summary>行内容被外部操作修改后，重新应用过滤/排序并通知桌面刷新原生排序。</summary>
    public void RefreshSort() => ReapplyView();

    [ObservableProperty] private TrackRowViewModel? selectedTrack;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string countText = "";
    [ObservableProperty] private string filterText = "";
    [ObservableProperty] private bool isLoading;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTrackChanged(TrackRowViewModel? oldValue, TrackRowViewModel? newValue)
    {
        // 恢复原生选中项（DataGrid.Refresh）期间的临时 null 不触发释放/重解码。
        if (_selectionRestoreDepth > 0) return;

        SyncCoverPreview(newValue);
    }

    /// <summary>只让当前选中项持有详情大图；重复选中同一行时不再重新解码。</summary>
    private void SyncCoverPreview(TrackRowViewModel? target)
    {
        if (ReferenceEquals(_previewRow, target)) return;

        _previewRow?.ReleaseCoverPreview();
        target?.RequestCoverPreview();
        _previewRow = target;
    }

    /// <summary>
    /// 开始恢复 DataGrid 原生选中项：期间选中项变化（含 Refresh 造成的临时 null）不驱动封面预览，
    /// 结束时会按最终选中项补一次同步，避免选中项闪断导致封面重复解码。
    /// </summary>
    public void BeginSelectionRestore() => _selectionRestoreDepth++;

    public void EndSelectionRestore()
    {
        if (_selectionRestoreDepth == 0) return;
        if (--_selectionRestoreDepth > 0) return;

        SyncCoverPreview(SelectedTrack);
    }

    [RelayCommand]
    private void CancelLoad()
    {
        // 取消当前导入/重载/自动匹配/转换，并让仍在扫描目录的旧请求失效。
        _operationVersion++;
        CancelActiveLoad();
        CancelAutoMatch();
        CancelConvert();
        _loadCts = null;
        IsLoading = false;
        StatusText = Localizer.T("Status.LoadingCanceled");
        ReapplyView();
    }

    private void CancelActiveLoad() => Cancel(_loadCts);

    private void CancelAutoMatch() => Cancel(_autoMatchCts);

    private void CancelConvert() => Cancel(_convertCts);

    private static void Cancel(CancellationTokenSource? cts)
    {
        if (cts is null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 旧操作已经结束并释放了令牌源。
        }
    }

    /// <summary>窗口关闭时退订全局事件并释放仍占用的令牌源/位图。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        CancelActiveLoad();
        CancelAutoMatch();
        CancelConvert();

        foreach (var row in _all)
            row.Dispose();
    }

    /// <summary>桌面入口：把路径展开成文件句柄（目录递归枚举），再走 <see cref="AddFilesAsync"/>。</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths, bool recursive = true)
    {
        var list = paths.ToList();
        var version = ++_operationVersion;
        CancelActiveLoad();
        IsLoading = true;
        StatusText = Localizer.T("Status.FolderScanning");

        try
        {
            List<IFileHandle> candidates;
            try
            {
                candidates = await Task.Run(() =>
                {
                    var result = new List<IFileHandle>();
                    foreach (var path in list)
                    {
                        if (Directory.Exists(path))
                        {
                            foreach (var file in EnumerateAudioFiles(path, recursive))
                                result.Add(new PhysicalFileHandle(file));
                        }
                        else if (File.Exists(path))
                        {
                            result.Add(new PhysicalFileHandle(path));
                        }
                    }

                    return result;
                });
            }
            catch (Exception ex) when (IsScanFailure(ex))
            {
                // 目录被删除/无权限/路径非法：给出可见提示，不能把界面留在“正在加载”。
                if (version != _operationVersion) return;

                IsLoading = false;
                StatusText = Localizer.F("Status.FolderScanFailed", ex.Message);
                return;
            }

            // 目录扫描期间又来了新的导入/重载/取消请求：旧扫描结果不能覆盖新状态。
            if (version != _operationVersion) return;

            await AddFilesCoreAsync(candidates, version);
        }
        finally
        {
            // 已经被新操作取代时由新操作负责 IsLoading，这里不能误清。
            if (version == _operationVersion)
                IsLoading = false;
        }
    }

    /// <summary>扫描目录时应当转成用户提示（而不是崩溃）的异常集合。</summary>
    private static bool IsScanFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException;

    /// <summary>平台无关入口：接收已经展开好的文件句柄（Android SAF 用这个）。</summary>
    public async Task AddFilesAsync(IEnumerable<IFileHandle> files)
    {
        // 先去掉已存在的文件，避免一次纯重复导入取消正在进行的导入。
        var todo = CollectNewFiles(files);
        if (todo.Count == 0)
        {
            // 已有导入在跑时不要把「正在加载」状态覆盖成「没有新文件」。
            if (!IsLoading)
                StatusText = Localizer.T("Status.NoNewFiles");
            return;
        }

        var version = ++_operationVersion;
        CancelActiveLoad();

        try
        {
            await LoadInBatchesAsync(todo, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 平台选择器（SAF）给出的句柄可能在读取时失效，不能让它冒泡到 async void 事件处理。
            if (version == _operationVersion)
            {
                IsLoading = false;
                StatusText = Localizer.F("Status.ImportFailed", ex.Message);
            }
        }
    }

    private async Task AddFilesCoreAsync(IEnumerable<IFileHandle> files, int version)
    {
        if (version != _operationVersion) return;

        var current = version == _operationVersion;
        try
        {
            var todo = CollectNewFiles(files);
            if (todo.Count == 0)
            {
                if (version != _operationVersion) return;
                StatusText = Localizer.T("Status.NoNewFiles");
                return;
            }

            await LoadInBatchesAsync(todo, version);
            current = version == _operationVersion;
        }
        catch (OperationCanceledException)
        {
            // 取消由 LoadInBatchesAsync 内部记账，这里只保证状态复位。
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            current = version == _operationVersion;
            if (current)
                StatusText = Localizer.F("Status.ImportFailed", ex.Message);
        }
        finally
        {
            // 被新操作取代时由新操作维护 IsLoading，不能在这里误清。
            if (current && version == _operationVersion)
                IsLoading = false;
        }
    }

    private List<IFileHandle> CollectNewFiles(IEnumerable<IFileHandle> files)
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

        return todo;
    }

    /// <summary>分批读取并增量更新列表；旧批次在合并前会检查操作版本，已取消/被取代时不落表。</summary>
    private async Task LoadInBatchesAsync(IReadOnlyList<IFileHandle> todo, int version)
    {
        CancelActiveLoad();
        using var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        IsLoading = true;
        var loaded = 0;
        var failed = 0;
        var canceled = false;
        var current = true;
        // 过滤词只归一化一次，避免每行重复 Trim。
        var filter = FilterText?.Trim() ?? "";

        try
        {
            StatusText = Localizer.F("Status.LoadingFiles", todo.Count);

            for (var offset = 0; offset < todo.Count; offset += LoadBatchSize)
            {
                if (token.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                var slice = new List<IFileHandle>(Math.Min(LoadBatchSize, todo.Count - offset));
                for (var i = offset; i < todo.Count && i < offset + LoadBatchSize; i++)
                    slice.Add(todo[i]);

                var results = await Task.Run(() => slice.Select(h => SafeRead(h, token)).ToList(), token);

                // 这一批读取完成前可能已被新操作取代或取消，结果必须整批丢弃。
                if (token.IsCancellationRequested ||
                    version != _operationVersion ||
                    !ReferenceEquals(_loadCts, cts))
                {
                    canceled = true;
                    break;
                }

                foreach (var item in results)
                {
                    var row = new TrackRowViewModel(item, Remove, _tagStore.ReadPictures);
                    _all.Add(row);

                    if (MatchesFilter(row, filter)) Tracks.Add(row);

                    if (item.Status == TrackStatus.Loaded) loaded++;
                    else if (item.Status is TrackStatus.Failed or TrackStatus.Skipped) failed++;
                }

                UpdateCounts();
                StatusText = Localizer.F("Status.LoadingProgress", offset + slice.Count, todo.Count);
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        finally
        {
            current = version == _operationVersion && ReferenceEquals(_loadCts, cts);
            if (ReferenceEquals(_loadCts, cts))
                _loadCts = null;
            if (current)
                IsLoading = false;
        }

        if (!current) return;

        ApplyFilter();

        StatusText = canceled
            ? Localizer.T("Status.LoadingCanceled")
            : Localizer.F("Status.LoadedFiles", loaded, failed);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_all.Count == 0) return;

        var version = ++_operationVersion;
        CancelActiveLoad();
        using var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        var pairs = _all
            .Select(row => (Row: row, Handle: row.Source ?? (IFileHandle)new PhysicalFileHandle(row.Path)))
            .ToList();

        IsLoading = true;
        var canceled = false;
        var current = true;
        try
        {
            StatusText = Localizer.T("Status.Reloading");

            for (var offset = 0; offset < pairs.Count; offset += LoadBatchSize)
            {
                if (token.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                var slice = pairs.Skip(offset).Take(LoadBatchSize).ToList();
                var results = await Task.Run(() =>
                {
                    foreach (var pair in slice)
                    {
                        try
                        {
                            // 句柄缓存了 FileInfo/SAF 查询结果，重读前必须刷新。
                            pair.Handle.RefreshMetadata();
                        }
                        catch
                        {
                            // 读取失败交给 SafeRead 标记该行，不阻断整批。
                        }
                    }

                    return slice.Select(p => SafeRead(p.Handle, token)).ToList();
                }, token);

                if (token.IsCancellationRequested ||
                    version != _operationVersion ||
                    !ReferenceEquals(_loadCts, cts))
                {
                    canceled = true;
                    break;
                }

                for (var i = 0; i < results.Count; i++)
                {
                    var row = slice[i].Row;
                    if (!_all.Contains(row)) continue;

                    // 等待期间行被改名/换源时，旧句柄的结果不能覆盖新状态。
                    if (row.Source is { } currentHandle &&
                        !string.Equals(currentHandle.Key, slice[i].Handle.Key, StringComparison.Ordinal))
                        continue;

                    row.ApplyFrom(results[i]);
                }

                StatusText = Localizer.F("Status.LoadingProgress", offset + slice.Count, pairs.Count);
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        finally
        {
            current = version == _operationVersion && ReferenceEquals(_loadCts, cts);
            if (ReferenceEquals(_loadCts, cts))
                _loadCts = null;
            if (current)
                IsLoading = false;
        }

        if (!current) return;

        ReapplyView();

        StatusText = canceled ? Localizer.T("Status.LoadingCanceled") : Localizer.T("Status.Reloaded");
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        // 与「保存全部」「自动匹配」共用写入门闩，避免同一文件被两个流程同时写。
        if (_isSaving || _isAutoMatching) return;

        if (SelectedTrack is null)
        {
            StatusText = Localizer.T("Status.SelectFile");
            return;
        }

        _isSaving = true;
        try
        {
            await SaveRowsAsync([SelectedTrack]);
        }
        finally
        {
            _isSaving = false;
        }
    }

    public async Task SaveAllAsync()
    {
        // 重复点击/与自动匹配并发会同时写同一批文件：直接用守卫挡掉后来的请求。
        if (_isSaving || _isAutoMatching) return;

        _isSaving = true;
        try
        {
            var rows = _all.Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped)).ToList();
            if (rows.Count == 0)
            {
                StatusText = Localizer.T("Status.NoSavableFiles");
                return;
            }

            await SaveRowsAsync(rows);
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>尚未写回文件的修改行数。</summary>
    public int UnsavedCount => _all.Count(r => r.Status == TrackStatus.Modified);

    public bool HasUnsavedChanges => UnsavedCount > 0;

    /// <summary>保存所有尚未写回文件的修改行（退出确认用）。</summary>
    public Task SaveUnsavedAsync() =>
        SaveSelectedRowsAsync(_all.Where(r => r.Status == TrackStatus.Modified));

    /// <summary>保存右键菜单里选中的多行（不是整个列表）。</summary>
    public async Task SaveSelectedRowsAsync(IEnumerable<TrackRowViewModel> selection)
    {
        if (_isSaving || _isAutoMatching) return;

        var rows = selection
            .Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped))
            .ToList();
        if (rows.Count == 0)
        {
            StatusText = Localizer.T("Status.NoSavableFiles");
            return;
        }

        _isSaving = true;
        try
        {
            await SaveRowsAsync(rows);
        }
        finally
        {
            _isSaving = false;
        }
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

                var previous = await Task.Run(() =>
                {
                    try { return _tagStore.Read(handle, includePictures: false).Tags; }
                    catch { return null; }
                });

                await Task.Run(() =>
                {
                    _tagStore.Write(handle, tags, options);
                    try
                    {
                        // 写入可能改变文件大小，KeepModifiedTime 关闭时还会改变 mtime。
                        handle.RefreshMetadata();
                    }
                    catch
                    {
                        // 元数据刷新失败不应让已成功的写入变成失败。
                    }
                });
                if (previous is not null)
                {
                    try
                    {
                        // 历史是辅助功能：写失败不能把已经成功的标签写入标记成失败。
                        await Task.Run(() => _historyStore.AddSnapshot(row.Key, previous));
                    }
                    catch
                    {
                        // 忽略历史写入失败，标签本身已保存。
                    }
                }

                row.ApplyFrom(new TrackItem
                {
                    Path = row.Path,
                    DisplayName = row.FileName,
                    HasCover = row.HasCover || tags.Pictures.Count > 0,
                    ModifiedUtc = ModifiedUtcOf(handle),
                    CreatedUtc = handle.CreationTimeUtc,
                    Tags = tags,
                    Info = row.Info is { } info ? info with { SizeBytes = handle.Length } : FallbackInfo(handle),
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

        ReapplyView();

        StatusText = failed == 0
            ? Localizer.F("Status.Saved", ok)
            : Localizer.F("Status.SavedPartial", ok, failed);
    }

    public Task ClearTagsAsync() => ClearTagsAsync(_all);

    /// <summary>清空指定行的标签（右键多选菜单用）。</summary>
    public Task ClearTagsAsync(IEnumerable<TrackRowViewModel> selection)
    {
        var rows = selection
            .Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped))
            .ToList();
        if (rows.Count == 0) return Task.CompletedTask;

        foreach (var row in rows)
        {
            row.ApplyTags(new TrackTags());
            row.ClearCover();
        }

        ReapplyView();
        StatusText = Localizer.F("Status.ClearedTags", rows.Count);
        return Task.CompletedTask;
    }

    /// <summary>从列表移除一行（只影响列表，不改文件）。</summary>
    public void Remove(TrackRowViewModel row)
    {
        var wasSelected = ReferenceEquals(SelectedTrack, row);

        _all.Remove(row);
        Tracks.Remove(row);
        row.Dispose();

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
            row.Dispose();
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

    public async Task ApplyCandidateAsync(
        TrackRowViewModel row,
        TagCandidate candidate,
        OverwriteOptions overwrite,
        bool includeCover = true,
        bool resort = true,
        bool spoolCoverToDisk = false,
        CancellationToken cancellationToken = default)
    {
        // 合并只涉及文本字段；不要为批量匹配把已暂存的封面额外读回内存。
        var merged = TagMerger.Merge(row.ToTags(includePictures: false), candidate.ToTags(), overwrite);
        row.ApplyTags(merged, overwrite.RuleFor(FieldKey.Lyrics));

        if (includeCover && ShouldApplyCover(row, overwrite) && !string.IsNullOrWhiteSpace(candidate.CoverUrl))
        {
            var source = _search.Find(candidate.SourceId);
            if (source is not null)
            {
                var bytes = await source.DownloadCoverAsync(candidate.CoverUrl, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (bytes is not null)
                {
                    var normalized = await Task.Run(() => Settings.Pictures.KeepOriginal
                        ? _covers.ValidateOriginal(bytes, Settings.Pictures.AllowedFormats)
                        : _covers.Normalize(
                            bytes,
                            Settings.Pictures.MaxWidth,
                            Settings.Pictures.MaxHeight,
                            Settings.Pictures.MaxSizeKb,
                            Settings.Pictures.AllowedFormats), cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (normalized is not null &&
                        !row.SetCover(normalized.Data, normalized.MimeType, spoolCoverToDisk))
                        throw new IOException("Unable to spool cover data.");
                }
            }
        }

        if (resort)
            ReapplyView();

        StatusText = Localizer.F("Status.AppliedCandidate", candidate.SourceId);
    }

    /// <summary>
    /// 按覆盖规则决定要不要替换封面：只填空字段时仅当当前行没有封面才下载，
    /// 保留已有封面时一律不动，替换时才无条件覆盖。
    /// </summary>
    private static bool ShouldApplyCover(TrackRowViewModel row, OverwriteOptions overwrite) =>
        overwrite.Default switch
        {
            OverwriteRule.Replace => true,
            OverwriteRule.FillEmptyOnly => !row.HasCover,
            _ => false,
        };

    /// <summary>对全部可匹配行执行批量自动匹配。</summary>
    public Task AutoMatchAllAsync(AutoMatchOptions? options = null) => AutoMatchAsync(_all, options);

    /// <summary>对指定行（右键多选）执行批量自动匹配。</summary>
    public async Task AutoMatchAsync(IEnumerable<TrackRowViewModel> selection, AutoMatchOptions? options = null)
    {
        // 重复点击会并发写同一批文件（尤其包含封面时），直接返回。
        if (_isAutoMatching || _isSaving) return;

        options ??= new AutoMatchOptions(
            OverwriteRule.Replace,
            Enum.GetValues<FieldKey>().ToHashSet(),
            IncludeCover: false,
            Settings.Sources.Tags);

        _isAutoMatching = true;
        using var cts = new CancellationTokenSource();
        _autoMatchCts = cts;
        var token = cts.Token;

        try
        {
            var rows = selection
                .Where(r => r.Status is not (TrackStatus.Failed or TrackStatus.Skipped))
                .ToList();
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
            var failed = 0;
            var canceled = false;

            for (var index = 0; index < rows.Count; index++)
            {
                if (token.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                var row = rows[index];
                StatusText = Localizer.F("Status.AutoMatching", index + 1, rows.Count, row.FileName);

                var query = new TagQuery
                {
                    Title = row.Title,
                    Artist = row.Artist,
                    Album = row.Album,
                    FileName = row.FileName,
                    Duration = row.Info?.Duration,
                    File = row.Source,
                };

                try
                {
                    var results = await _search.SearchAsync(
                        query, capability, options.SourceOrder,
                        Settings.Search.ItemsPerSource, Settings.Search.AutoMatchThreads, token);

                    if (token.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }

                    var best = results.FirstOrDefault();
                    if (best is null)
                    {
                        // 搜索正常但没有候选：不算错误。
                        miss++;
                        continue;
                    }

                    await ApplyCandidateAsync(
                        row, best, overwrite, options.IncludeCover,
                        resort: false, spoolCoverToDisk: true, cancellationToken: token);
                    hit++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    // 搜索成功但合并/下载/暂存失败：单独计数并记到行上便于定位。
                    // 不改 Status，避免这行被后续「全部保存」当成不可保存的行跳过。
                    failed++;
                    row.ErrorText = ex.Message;
                }
            }

            ReapplyView();

            if (canceled)
            {
                StatusText = Localizer.T("Search.Cancelled");
                return;
            }

            StatusText = Localizer.F("Status.AutoMatchDone", hit, miss);
            if (failed > 0)
                StatusText = $"{StatusText} · {Localizer.T("TrackStatus.Failed")} {failed}";

            // 指纹解码失败（如 Linux 缺 GStreamer 解码插件）随批量结果一并提示。
            if (_search.Find("acoustid")?.LastError is { } decodeError)
                StatusText = $"{StatusText} · {decodeError}";
        }
        finally
        {
            if (ReferenceEquals(_autoMatchCts, cts))
                _autoMatchCts = null;
            _isAutoMatching = false;
        }
    }

    /// <summary>把已有封面导出到音频同目录，命名为 "文件名-cover.jpg/png"。</summary>
    public async Task ExtractCoversAsync()
    {
        var rows = _all.Where(r => r.HasCover).ToList();
        if (rows.Count == 0)
        {
            StatusText = Localizer.T("Status.NoCovers");
            return;
        }

        var count = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            var pictures = await Task.Run(row.LoadPictures);
            if (pictures.Count == 0)
            {
                failed++;
                continue;
            }

            var picture = pictures[0];
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

    /// <summary>简繁转换服务不是线程安全的：后台转换与重命名共用同一把锁串行使用。</summary>
    private readonly SemaphoreSlim _chineseGate = new(1, 1);

    private sealed record ConvertedFields(
        TrackRowViewModel Row,
        string? Title,
        string? Artist,
        string? Album,
        string? AlbumArtist,
        string? Genre,
        string? Comment,
        string? Composer);

    /// <summary>把列表中所有标签做简繁转换（只改内存，保存后写入）。</summary>
    public async Task ConvertTagsAsync(bool toTraditional, CancellationToken cancellationToken = default)
    {
        if (_isConverting) return;

        var rows = _all.ToList();
        if (rows.Count == 0) return;

        _isConverting = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _convertCts = cts;
        var token = cts.Token;

        try
        {
            // 纯 CPU 的字符转换放到后台线程，UI 线程只做属性写回。
            var converted = await Task.Run(() =>
            {
                _chineseGate.Wait(token);
                try
                {
                    var result = new List<ConvertedFields>(rows.Count);
                    foreach (var row in rows)
                    {
                        token.ThrowIfCancellationRequested();
                        result.Add(new ConvertedFields(
                            row,
                            Convert(row.Title),
                            Convert(row.Artist),
                            Convert(row.Album),
                            Convert(row.AlbumArtist),
                            Convert(row.Genre),
                            Convert(row.Comment),
                            Convert(row.Composer)));
                    }

                    return result;
                }
                finally
                {
                    _chineseGate.Release();
                }
            }, token).ConfigureAwait(true);

            foreach (var item in converted)
            {
                item.Row.Title = item.Title;
                item.Row.Artist = item.Artist;
                item.Row.Album = item.Album;
                item.Row.AlbumArtist = item.AlbumArtist;
                item.Row.Genre = item.Genre;
                item.Row.Comment = item.Comment;
                item.Row.Composer = item.Composer;
            }

            ReapplyView();
            StatusText = Localizer.T(toTraditional ? "Status.TagsToTraditional" : "Status.TagsToSimplified");
        }
        catch (OperationCanceledException)
        {
            StatusText = Localizer.T("Search.Cancelled");
        }
        finally
        {
            if (ReferenceEquals(_convertCts, cts))
                _convertCts = null;
            _isConverting = false;
        }

        return;

        string? Convert(string? value) => value is null ? null : toTraditional
            ? _chinese.ToTraditional(value)
            : _chinese.ToSimplified(value);
    }

    /// <summary>把列表中的文件名做简繁转换（直接改磁盘文件）。</summary>
    public async Task ConvertFileNamesAsync(bool toTraditional, ISafeNamePolicy policy, CancellationToken cancellationToken = default)
    {
        if (_isConverting) return;

        var rows = _all.ToList();
        if (!rows.Any(r => ResolveHandle(r)?.CanRename == true))
        {
            StatusText = Localizer.T("Status.RenameUnsupported");
            return;
        }

        _isConverting = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _convertCts = cts;
        var token = cts.Token;

        try
        {
            // 重命名是磁盘操作：整批放后台，UI 线程只负责把新句柄写回行。
            var outcome = await Task.Run(() =>
            {
                var renames = new List<(TrackRowViewModel Row, IFileHandle Handle)>();
                var done = 0;
                var failed = 0;

                _chineseGate.Wait(token);
                try
                {
                    var service = new RenameService();

                    foreach (var row in rows)
                    {
                        token.ThrowIfCancellationRequested();

                        var handle = ResolveHandle(row);
                        var baseName = handle?.FileNameWithoutExtension ?? Path.GetFileNameWithoutExtension(row.FileName);
                        var converted = toTraditional ? _chinese.ToTraditional(baseName) : _chinese.ToSimplified(baseName);
                        if (string.Equals(baseName, converted, StringComparison.Ordinal)) continue;

                        if (handle is not { CanRename: true })
                        {
                            failed++;
                            continue;
                        }

                        try
                        {
                            var result = service.ApplyExact(handle, converted, policy,
                                alsoRenameLyrics: true,
                                alsoRenamePictures: true);
                            if (!string.Equals(result.New.Key, result.Old.Key, StringComparison.Ordinal))
                            {
                                renames.Add((row, result.New));
                                done++;
                            }
                        }
                        catch (Exception)
                        {
                            failed++;
                        }
                    }
                }
                finally
                {
                    _chineseGate.Release();
                }

                return (Renames: renames, Renamed: done, Failed: failed);
            }, token).ConfigureAwait(true);

            foreach (var (row, handle) in outcome.Renames)
                row.RenameTo(handle);

            ReapplyView();

            StatusText = outcome.Failed > 0
                ? Localizer.F("Status.FileNameConvertedPartial", outcome.Renamed, outcome.Failed)
                : Localizer.F("Status.FileNameConverted", outcome.Renamed);
        }
        catch (OperationCanceledException)
        {
            StatusText = Localizer.T("Search.Cancelled");
        }
        finally
        {
            if (ReferenceEquals(_convertCts, cts))
                _convertCts = null;
            _isConverting = false;
        }
    }

    private static IFileHandle? ResolveHandle(TrackRowViewModel row)
    {
        if (row.Source is { } source) return source;
        return Path.IsPathRooted(row.Path) ? new PhysicalFileHandle(row.Path) : null;
    }

    public async Task RestoreHistoryAsync(TrackRowViewModel row, HistoryEntry entry)
    {
        try
        {
            var handle = row.Source ?? (IFileHandle)new PhysicalFileHandle(row.Path);
            await Task.Run(() =>
            {
                _tagStore.Write(
                    handle,
                    entry.Tags,
                    new TagWriteOptions(Settings.Writing.Id3v2Version, Settings.Writing.KeepModifiedTime, Settings.Writing.WriteLyricsToTag));
                try
                {
                    handle.RefreshMetadata();
                }
                catch
                {
                    // 元数据刷新失败不影响恢复结果。
                }
            });

            var item = await Task.Run(() => _tagStore.Read(handle, includePictures: false));
            row.ApplyFrom(item);
            row.Status = TrackStatus.Saved;
            ReapplyView();
            StatusText = Localizer.F("Status.Restored", entry.CreatedAt.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            StatusText = Localizer.F("Status.RestoreFailed", ex.Message);
        }
    }

    public void SaveSettings() => _settingsStore.Save(Settings);

    private void ReapplyView()
    {
        ApplyFilter();
        SortRefreshRequested?.Invoke();
    }

    /// <summary>过滤/排序改为增量维护 Tracks，避免整表 Clear/Add 丢失选中项和移动端详情。</summary>
    private void ApplyFilter()
    {
        // 过滤词只 Trim 一次，避免每行都重新分配字符串。
        var filter = FilterText?.Trim() ?? "";

        var previous = SelectedTrack;
        var desired = TrackSorter.Sort(_all.Where(row => MatchesFilter(row, filter)), _sortField, SortDescending);
        var desiredSet = new HashSet<TrackRowViewModel>(desired);

        // 选中行仍保留在列表中时，Move 不应让移动端把详情页当成“选中项被移除”。
        var preserveSelection = previous is not null && desiredSet.Contains(previous);
        if (preserveSelection)
            IsReordering = true;

        try
        {
            for (var i = Tracks.Count - 1; i >= 0; i--)
            {
                if (!desiredSet.Contains(Tracks[i]))
                    Tracks.RemoveAt(i);
            }

            for (var i = 0; i < desired.Count; i++)
            {
                var row = desired[i];
                if (i < Tracks.Count && ReferenceEquals(Tracks[i], row))
                    continue;

                var current = -1;
                for (var j = i + 1; j < Tracks.Count; j++)
                {
                    if (ReferenceEquals(Tracks[j], row))
                    {
                        current = j;
                        break;
                    }
                }

                if (current >= 0)
                    Tracks.Move(current, i);
                else
                    Tracks.Insert(i, row);
            }

            if (previous is not null && Tracks.Contains(previous))
            {
                if (!ReferenceEquals(SelectedTrack, previous))
                    SelectedTrack = previous;
            }
            else
            {
                SelectedTrack = Tracks.FirstOrDefault();
            }
        }
        finally
        {
            IsReordering = false;
        }

        UpdateCounts();
    }

    private static bool MatchesFilter(TrackRowViewModel row, string filter) =>
        filter.Length == 0 || Matches(row, filter);

    private static bool Matches(TrackRowViewModel row, string filter) =>
        Contains(row.FileName, filter) || Contains(row.Title, filter) ||
        Contains(row.Artist, filter) || Contains(row.Album, filter) ||
        Contains(row.AlbumArtist, filter) || Contains(row.Genre, filter);

    private static bool Contains(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> EnumerateAudioFiles(string directory, bool recursive)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            // 保持与旧 SearchOption 行为一致：不额外跳过隐藏/系统文件。
            AttributesToSkip = 0,
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            if (_tagStore.CanHandle(Path.GetExtension(file).ToLowerInvariant()))
                yield return file;
        }
    }

    private TrackItem SafeRead(IFileHandle handle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

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
                    ModifiedUtc = ModifiedUtcOf(handle),
                    CreatedUtc = handle.CreationTimeUtc,
                    Info = FallbackInfo(handle),
                    Source = handle,
                };
            }

            return _tagStore.Read(handle, includePictures: false);
        }
        catch (Exception ex)
        {
            return new TrackItem
            {
                Path = handle.DisplayPath,
                DisplayName = handle.FileName,
                Status = TrackStatus.Failed,
                Error = ex.Message,
                ModifiedUtc = ModifiedUtcOf(handle),
                CreatedUtc = handle.CreationTimeUtc,
                Info = FallbackInfo(handle),
                Source = handle,
            };
        }
    }

    private static DateTime? ModifiedUtcOf(IFileHandle handle) =>
        handle.LastWriteTimeUtc > DateTime.UnixEpoch ? handle.LastWriteTimeUtc : null;

    private static AudioInfo FallbackInfo(IFileHandle handle) => new(
        Format: string.IsNullOrWhiteSpace(handle.Extension)
            ? null
            : handle.Extension.TrimStart('.').ToUpperInvariant(),
        Duration: TimeSpan.Zero,
        BitrateKbps: null,
        SampleRateHz: null,
        Channels: null,
        SizeBytes: handle.Length);

    private void UpdateCounts() =>
        CountText = _all.Count == Tracks.Count
            ? Localizer.F("Count.Files", _all.Count)
            : Localizer.F("Count.Filtered", Tracks.Count, _all.Count);
}
