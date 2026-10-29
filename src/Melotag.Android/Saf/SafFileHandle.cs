using Android.Content;
using Melotag.Core.Abstractions;

namespace Melotag.Droid;

/// <summary>
/// SAF 文档句柄：content:// 不能随机访问，TagLib 需要可 seek 的流，
/// 因此选择后复制到应用私有缓存里读写，写回时整体覆盖原文档。
/// </summary>
public sealed class SafFileHandle : IFileHandle
{
    private readonly ContentResolver _resolver;
    private readonly Android.Net.Uri _uri;
    private readonly string _cachePath;

    public SafFileHandle(
        ContentResolver resolver,
        Android.Net.Uri uri,
        string displayName,
        string cachePath,
        long length,
        bool writable)
    {
        _resolver = resolver;
        _uri = uri;
        _cachePath = cachePath;

        FileName = displayName;
        FileNameWithoutExtension = Path.GetFileNameWithoutExtension(displayName);
        Extension = Path.GetExtension(displayName).ToLowerInvariant();
        Length = length;
        LastWriteTimeUtc = File.Exists(cachePath) ? File.GetLastWriteTimeUtc(cachePath) : DateTime.UtcNow;
        IsWritable = writable;
    }

    /// <summary>SAF 下没有真正的路径，展示文件名。</summary>
    public string DisplayPath => FileName;

    public string FileName { get; }

    public string FileNameWithoutExtension { get; }

    public string Extension { get; }

    public long Length { get; }

    public DateTime LastWriteTimeUtc { get; }

    public string Key => _uri.ToString()!;

    public bool IsWritable { get; }

    public Stream OpenRead() =>
        new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public Stream OpenReadWrite() =>
        new WriteBackStream(
            new FileStream(_cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
            this);

    /// <summary>SAF 只授权单个文档，无法枚举同目录兄弟文件。</summary>
    public IFileHandle? GetSibling(string fileName) => null;

    /// <summary>SAF 不暴露修改时间写入接口，静默忽略。</summary>
    public void SetLastWriteTimeUtc(DateTime utc)
    {
    }

    /// <summary>把缓存文件整体写回原文档（"wt" 为截断模式）。</summary>
    internal void CopyBack()
    {
        using var output = _resolver.OpenOutputStream(_uri, "wt")
                           ?? throw new IOException("无法写入所选文档，可能是只读授权");

        using var input = new FileStream(_cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        input.CopyTo(output);
        output.Flush();
    }

    public override string ToString() => FileName;

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
