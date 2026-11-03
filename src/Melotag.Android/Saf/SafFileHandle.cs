namespace Melotag.Droid;

using Android.Content;
using Android.Provider;
using Melotag.Core.Abstractions;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// SAF 文档句柄：读优先用可 seek 的 ParcelFileDescriptor 直读，
/// provider 不支持时回退到私有缓存；写入统一走缓存 + 整体写回。
/// </summary>
public sealed class SafFileHandle : IFileHandle
{
    private readonly Context _context;
    private readonly ContentResolver _resolver;
    private readonly string _cachePath;
    private bool _cacheReady;

    internal SafFileHandle(Context context, ContentResolver resolver, SafDocument document)
    {
        _context = context;
        _resolver = resolver;
        Document = document;

        FileName = document.DisplayName;
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(document.DisplayName);
        Extension = Path.GetExtension(document.DisplayName).ToLowerInvariant();
        Length = document.Size;
        LastWriteTimeUtc = document.LastModifiedMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(document.LastModifiedMs).UtcDateTime
            : DateTime.UnixEpoch;

        _cachePath = SafCache.PathFor(context, document.Uri, document.DisplayName);
        _cacheReady = File.Exists(_cachePath);
    }

    internal SafDocument Document { get; }

    /// <summary>SAF 下没有真正的路径，展示文件名。</summary>
    public string DisplayPath => FileName;

    public string FileName { get; }

    public string FileNameWithoutExtension { get; }

    public string Extension { get; }

    public long Length { get; }

    public DateTime LastWriteTimeUtc { get; }

    public string Key => Document.Uri.ToString()!;

    public bool IsWritable => Document.CanWrite;

    public bool CanRename => Document.CanRename && Document.CanWrite;

    public string? DirectoryKey =>
        Document.TreeUri is null || ParentDocId is not { } parent ? null : $"{Document.TreeUri}|{parent}";

    private string? ParentDocId
    {
        get
        {
            var slash = Document.DocId.LastIndexOf('/');
            return slash <= 0 ? null : Document.DocId[..slash];
        }
    }

    public Stream OpenRead()
    {
        if (_cacheReady)
            return new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        return TryOpenDirect() ?? OpenCached();
    }

    public Stream OpenReadWrite()
    {
        SeedCache();
        return new WriteBackStream(
            new FileStream(_cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
            this);
    }

    public IReadOnlyList<IFileHandle> GetSiblings()
    {
        if (Document.TreeUri is not { } treeUri || ParentDocId is not { } parentId)
            return [];

        return SafTree.EnumerateChildren(_resolver, treeUri, parentId)
            .Select(document => (IFileHandle)new SafFileHandle(_context, _resolver, document))
            .ToList();
    }

    public IFileHandle Rename(string newFileName)
    {
        if (!CanRename)
            throw new InvalidOperationException("该文件不支持重命名");

        if (string.IsNullOrWhiteSpace(newFileName) ||
            !string.Equals(Path.GetFileName(newFileName), newFileName, StringComparison.Ordinal))
            throw new ArgumentException("文件名不能包含目录", nameof(newFileName));

        var renamedUri = DocumentsContract.RenameDocument(_resolver, Document.Uri, newFileName)
            ?? throw new IOException("改名失败");

        var updated = Document with
        {
            Uri = renamedUri,
            DocId = DocumentsContract.GetDocumentId(renamedUri),
            DisplayName = newFileName,
        };

        MoveCache(updated);
        return new SafFileHandle(_context, _resolver, updated);
    }

    /// <summary>SAF 不暴露修改时间写入接口，静默忽略。</summary>
    public void SetLastWriteTimeUtc(DateTime utc)
    {
    }

    public override string ToString() => FileName;

    private Stream? TryOpenDirect()
    {
        try
        {
            using var descriptor = _resolver.OpenFileDescriptor(Document.Uri, "r");
            if (descriptor is null) return null;

            var handle = new SafeFileHandle((IntPtr)descriptor.DetachFd(), ownsHandle: true);
            var stream = new FileStream(handle, FileAccess.Read, 4096);
            try
            {
                // 非 seekable 的 provider 会在这里抛，回退到缓存
                stream.Seek(0, SeekOrigin.Current);
                return stream;
            }
            catch
            {
                stream.Dispose();
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private Stream OpenCached()
    {
        SeedCache();
        return new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private void SeedCache()
    {
        if (_cacheReady) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        using var input = _resolver.OpenInputStream(Document.Uri)
            ?? throw new IOException("无法读取所选文档");
        using var output = new FileStream(_cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush();
        _cacheReady = true;
    }

    private void MoveCache(SafDocument updated)
    {
        if (!File.Exists(_cachePath)) return;

        var target = SafCache.PathFor(_context, updated.Uri, updated.DisplayName);
        if (string.Equals(target, _cachePath, StringComparison.Ordinal)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) File.Delete(target);
        File.Move(_cachePath, target);
    }

    /// <summary>把缓存文件整体写回原文档（"wt" 为截断模式）。</summary>
    internal void CopyBack()
    {
        using var output = _resolver.OpenOutputStream(Document.Uri, "wt")
            ?? throw new IOException("无法写入所选文档，可能是只读授权");

        using var input = new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        input.CopyTo(output);
        output.Flush();
    }

    /// <summary>TagLib 关闭写流时把缓存写回文档，异常从这里抛给保存流程。</summary>
    private sealed class WriteBackStream : Stream
    {
        private readonly FileStream _inner;
        private readonly SafFileHandle _owner;
        private bool _completed;

        public WriteBackStream(FileStream inner, SafFileHandle owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed)
            {
                _completed = true;
                try
                {
                    _inner.Flush();
                    _inner.Dispose();
                    _owner.CopyBack();
                }
                catch
                {
                    _inner.Dispose();
                    throw;
                }
            }

            base.Dispose(disposing);
        }
    }
}
