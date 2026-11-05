namespace Melotag.UI.ViewModels;

using Avalonia.Media.Imaging;

/// <summary>
/// 移动端缩略图的小型 LRU 缓存（最多 256 条 / 约 6 MB）。
/// 淘汰时只移除引用、不立即 Dispose：位图可能仍被可见的 Image 绑定持有。
/// Avalonia 的 Bitmap.PlatformImpl 是 RefCountable.Ref&lt;T&gt;（CriticalFinalizerObject），
/// Bitmap 不可达后其终结器会 Release 并 Dispose 底层 IBitmapImpl/SKBitmap，
/// 因此缓存强引用之外的原生内存会随 GC 回收；行对象本身也只持有 WeakReference。
/// 行被移除/封面变化时仍会显式 Dispose 当前缓存项。
/// </summary>
internal static class ThumbnailCache
{
    private const int MaxEntries = 256;
    private const long MaxBytes = 6 * 1024 * 1024;

    private sealed record Entry(string Key, Bitmap Bitmap, long Bytes);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> Map = new(StringComparer.Ordinal);
    private static readonly LinkedList<Entry> Lru = new();
    private static long _bytes;

    public static bool TryGet(string key, out Bitmap bitmap)
    {
        lock (Sync)
        {
            if (Map.TryGetValue(key, out var node))
            {
                Lru.Remove(node);
                Lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null!;
        return false;
    }

    public static void Put(string key, Bitmap bitmap)
    {
        lock (Sync)
        {
            if (Map.TryGetValue(key, out var existing))
            {
                if (ReferenceEquals(existing.Value.Bitmap, bitmap))
                {
                    Lru.Remove(existing);
                    Lru.AddFirst(existing);
                    return;
                }

                RemoveNode(existing);
            }

            var bytes = EstimateBytes(bitmap);
            var node = new LinkedListNode<Entry>(new Entry(key, bitmap, bytes));
            Lru.AddFirst(node);
            Map[key] = node;
            _bytes += bytes;

            while (Lru.Count > MaxEntries || _bytes > MaxBytes)
            {
                var last = Lru.Last;
                if (last is null) break;
                RemoveNode(last);
            }
        }
    }

    /// <summary>移除并返回缓存中的位图；调用方决定是否释放（避免释放仍显示在界面上的图像）。</summary>
    public static Bitmap? Remove(string key)
    {
        lock (Sync)
        {
            if (!Map.TryGetValue(key, out var node)) return null;
            RemoveNode(node);
            return node.Value.Bitmap;
        }
    }

    private static void RemoveNode(LinkedListNode<Entry> node)
    {
        Lru.Remove(node);
        Map.Remove(node.Value.Key);
        _bytes = Math.Max(0, _bytes - node.Value.Bytes);
    }

    private static long EstimateBytes(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        return Math.Max(1, (long)size.Width * size.Height * 4);
    }
}
