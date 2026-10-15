using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using PdfiumViewer;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Источник страниц одного PDF-файла с ленивым открытием PdfDocument.
    ///
    /// Ключевые свойства:
    ///   • При конструировании читается только PageCount — файл открывается,
    ///     читается число страниц, документ сразу закрывается. Нативная
    ///     память не остаётся.
    ///   • PdfDocument открывается при первом GetPage и удерживается до
    ///     простоя 30 секунд, после чего закрывается автоматически.
    ///   • Есть ReleaseNativeResources() для немедленного закрытия по
    ///     требованию (после предпросмотра/печати).
    ///   • Все вызовы Pdfium сериализованы глобальным lock — сборка
    ///     PdfiumViewer.Native.x86_64.v8-xfa не полностью потокобезопасна.
    ///   • Размеры страниц кэшируются локально — после первого GetPage
    ///     горячий путь не дёргает Pdfium для метаданных.
    /// </summary>
    public sealed class PdfPageImageSource : IPageImageSource
    {
        private sealed class Entry
        {
            public Image Image;
            public long Bytes;
        }

        private sealed class SuppressionHandle : IDisposable
        {
            private PdfPageImageSource _owner;
            public SuppressionHandle(PdfPageImageSource owner) { _owner = owner; }
            public void Dispose()
            {
                var o = Interlocked.Exchange(ref _owner, null);
                o?.EndSuppressEviction();
            }
        }

        private readonly string _filePath;
        private readonly long _cacheBudgetBytes;
        private readonly int _pageCount;

        // ---------- Lazy reader ----------
        private readonly object _readerSync = new object();
        private PdfDocument _reader;
        private DateTime _lastAccess = DateTime.MinValue;
        private readonly Timer _idleTimer;

        private const int IdleTimeoutMs = 30_000;        // 30 сек
        private const long MaxPixelsPerPage = 6_000_000L;   // ≈ 24 МБ @ 32bpp

        // ---------- Кэш страниц ----------
        private readonly object _cacheSync = new object();
        private readonly Dictionary<long, Entry> _cache = new Dictionary<long, Entry>();
        private readonly LinkedList<long> _lru = new LinkedList<long>();
        private long _cacheBytes;

        // ---------- Кэш размеров страниц ----------
        private readonly Dictionary<int, SizeF> _pageSizeCache = new Dictionary<int, SizeF>();
        private readonly object _pageSizeSync = new object();

        // ---------- Подавление эвикции ----------
        private readonly object _evictionSync = new object();
        private int _evictionSuppressCount;

        private bool _disposed;

        /// <summary>Глобальный lock на все вызовы Pdfium.</summary>
        private static readonly object PdfiumLock = new object();

        public PdfPageImageSource(string filePath, long cacheBudgetBytes = 32L * 1024 * 1024,
                                  int maxReaders = 0)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF-файл не найден.", filePath);

            _filePath = filePath;
            _cacheBudgetBytes = Math.Max(0, cacheBudgetBytes);

            // Peek PageCount. Открываем, читаем, закрываем — нативная
            // память не удерживается.
            lock (PdfiumLock)
            {
                using (var tmp = PdfDocument.Load(filePath))
                {
                    _pageCount = tmp.PageCount;
                }
            }

            // Таймер закрытия простаивающего ридера.
            _idleTimer = new Timer(
                callback: _ => CloseIdleReader(),
                state: null,
                dueTime: IdleTimeoutMs,
                period: IdleTimeoutMs);
        }

        public int PageCount => _pageCount;

        public long CacheBytes
        {
            get { lock (_cacheSync) return _cacheBytes; }
        }

        // ====================================================================
        //  Подавление эвикции
        // ====================================================================
        public IDisposable SuppressEviction()
        {
            lock (_evictionSync) _evictionSuppressCount++;
            return new SuppressionHandle(this);
        }

        private void EndSuppressEviction()
        {
            bool resume = false;
            lock (_evictionSync)
            {
                _evictionSuppressCount--;
                if (_evictionSuppressCount <= 0)
                {
                    _evictionSuppressCount = 0;
                    resume = true;
                }
            }
            if (resume)
                lock (_cacheSync) EvictUntilBudgetInternal();
        }

        private bool IsEvictionSuppressed
        {
            get { lock (_evictionSync) return _evictionSuppressCount > 0; }
        }

        // ====================================================================
        //  GetPage
        // ====================================================================
        public Image GetPage(int index, int dpi)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PdfPageImageSource));
            if (index < 0 || index >= _pageCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            long key = ((long)dpi << 32) | (uint)index;

            // 1. Cache hit
            lock (_cacheSync)
            {
                if (_cache.TryGetValue(key, out var hit))
                {
                    _lru.Remove(key);
                    _lru.AddLast(key);
                    return hit.Image;
                }
            }

            // 2. Render
            var img = RenderPageBitmap(index, dpi);
            long bytes = EstimateBytes(img);

            // 3. Store in cache
            lock (_cacheSync)
            {
                if (_cache.TryGetValue(key, out var race))
                {
                    img.Dispose();
                    _lru.Remove(key);
                    _lru.AddLast(key);
                    return race.Image;
                }

                _cache[key] = new Entry { Image = img, Bytes = bytes };
                _lru.AddLast(key);
                _cacheBytes += bytes;

                if (!IsEvictionSuppressed)
                    EvictUntilBudgetInternal();
            }
            return img;
        }

        // ====================================================================
        //  Cache
        // ====================================================================
        public void ClearCache()
        {
            long freed = 0;
            lock (_cacheSync)
            {
                foreach (var e in _cache.Values)
                {
                    freed += e.Bytes;
                    e.Image.Dispose();
                }
                _cache.Clear();
                _lru.Clear();
                _cacheBytes = 0;
            }
            if (freed > 1L * 1024 * 1024)
                ForceGc();
        }

        /// <summary>
        /// Немедленно освобождает нативные ресурсы: закрывает PdfDocument,
        /// очищает кэш страниц и кэш размеров.
        /// </summary>
        public void ReleaseNativeResources()
        {
            if (_disposed) return;

            ClearCache();

            lock (_pageSizeSync)
                _pageSizeCache.Clear();

            lock (_readerSync)
            {
                if (_reader != null)
                {
                    lock (PdfiumLock)
                    {
                        try { _reader.Dispose(); } catch { }
                        _reader = null;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[PdfPageImageSource] Released reader for " +
                        $"{Path.GetFileName(_filePath)}");
                }
            }

            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
        }

        // ====================================================================
        //  Lazy reader
        // ====================================================================
        private PdfDocument EnsureReader()
        {
            lock (_readerSync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PdfPageImageSource));

                if (_reader == null)
                {
                    lock (PdfiumLock)
                        _reader = PdfDocument.Load(_filePath);

                    System.Diagnostics.Debug.WriteLine(
                        $"[PdfPageImageSource] Opened reader for " +
                        $"{Path.GetFileName(_filePath)}");
                }

                _lastAccess = DateTime.UtcNow;
                return _reader;
            }
        }

        private void CloseIdleReader()
        {
            if (_disposed) return;

            bool closed = false;

            lock (_readerSync)
            {
                if (_reader == null) return;

                var idle = DateTime.UtcNow - _lastAccess;
                if (idle.TotalMilliseconds < IdleTimeoutMs) return;

                lock (PdfiumLock)
                {
                    try { _reader.Dispose(); } catch { }
                    _reader = null;
                }
                closed = true;
            }

            if (closed)
            {
                ClearCache();

                lock (_pageSizeSync)
                    _pageSizeCache.Clear();

                System.Diagnostics.Debug.WriteLine(
                    $"[PdfPageImageSource] Idle-closed reader for " +
                    $"{Path.GetFileName(_filePath)}");

                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
            }
        }

        // ====================================================================
        //  Render
        // ====================================================================
        private SizeF GetPageSize(int index)
        {
            lock (_pageSizeSync)
            {
                if (_pageSizeCache.TryGetValue(index, out var s)) return s;
            }

            var reader = EnsureReader();
            SizeF size;
            lock (PdfiumLock)
                size = reader.PageSizes[index];

            lock (_pageSizeSync)
                _pageSizeCache[index] = size;

            return size;
        }

        private Image RenderPageBitmap(int index, int dpi)
        {
            var size = GetPageSize(index);

            int width = Math.Max(1, (int)Math.Round(size.Width / 72.0 * dpi));
            int height = Math.Max(1, (int)Math.Round(size.Height / 72.0 * dpi));

            if ((long)width * height > MaxPixelsPerPage)
            {
                double k = Math.Sqrt((double)MaxPixelsPerPage / ((long)width * height));
                width = Math.Max(1, (int)(width * k));
                height = Math.Max(1, (int)(height * k));

                System.Diagnostics.Debug.WriteLine(
                    $"[PdfPageImageSource] Page {index}: capped to {width}×{height} " +
                    $"(orig {size.Width:F0}×{size.Height:F0} pt @ {dpi} DPI)");
            }

            var reader = EnsureReader();

            Exception lastEx;

            // Последовательные попытки с разными флагами.
            if (TryRender(reader, index, width, height, dpi,
                          PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations,
                          out var img, out lastEx))
                return img;

            if (TryRender(reader, index, width, height, dpi,
                          PdfRenderFlags.ForPrinting, out img, out lastEx))
                return img;

            if (TryRender(reader, index, width, height, dpi,
                          PdfRenderFlags.Annotations, out img, out lastEx))
                return img;

            if (TryRender(reader, index, width, height, dpi,
                          PdfRenderFlags.None, out img, out lastEx))
                return img;

            // Пересоздать ридер и попробовать ещё раз.
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PdfPageImageSource] Page {index}: reopening file for retry");

                lock (_readerSync)
                {
                    lock (PdfiumLock)
                    {
                        try { _reader?.Dispose(); } catch { }
                        _reader = PdfDocument.Load(_filePath);
                    }

                    if (TryRender(_reader, index, width, height, dpi,
                                  PdfRenderFlags.ForPrinting,
                                  out img, out lastEx))
                        return img;
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[PdfPageImageSource] Page {index} failed permanently: " +
                $"{lastEx?.Message}. Returning placeholder.");

            return MakePlaceholder(width, height, dpi, index, lastEx?.Message);
        }

        private static bool TryRender(
            PdfDocument reader, int index, int width, int height, int dpi,
            PdfRenderFlags flags, out Image image, out Exception lastEx)
        {
            image = null;
            lastEx = null;

            if (reader == null) return false;

            try
            {
                lock (PdfiumLock)
                    image = reader.Render(index, width, height, dpi, dpi, flags);
                return image != null;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                System.Diagnostics.Debug.WriteLine(
                    $"[PdfPageImageSource] Render page {index} flags={flags} " +
                    $"failed: {ex.Message}");
                return false;
            }
        }

        private static Bitmap MakePlaceholder(
            int width, int height, int dpi, int pageIndex, string reason)
        {
            var bmp = new Bitmap(width, height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            bmp.SetResolution(dpi, dpi);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);

                using (var pen = new Pen(Color.LightGray, 2f))
                    g.DrawRectangle(pen, 4, 4, width - 8, height - 8);

                using (var font = new Font("Segoe UI", Math.Max(10f, height / 60f)))
                using (var small = new Font("Segoe UI", Math.Max(8f, height / 90f)))
                using (var brush = new SolidBrush(Color.Gray))
                using (var fmt = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisWord
                })
                {
                    var rect = new RectangleF(20, 20, width - 40, height - 40);
                    g.DrawString(
                        "Страница " + (pageIndex + 1) + "\r\nне удалось отрисовать",
                        font, brush, rect, fmt);

                    if (!string.IsNullOrEmpty(reason))
                    {
                        var lower = new RectangleF(20, height * 0.65f,
                                                   width - 40, height * 0.3f);
                        g.DrawString(reason, small, brush, lower, fmt);
                    }
                }
            }
            return bmp;
        }

        // ====================================================================
        //  Eviction
        // ====================================================================
        private void EvictUntilBudgetInternal()
        {
            if (_cacheBudgetBytes <= 0)
            {
                while (_cacheBytes > 0 && _lru.Count > 1)
                    EvictOldest(out _);
                return;
            }

            long freedTotal = 0;
            while (_cacheBytes > _cacheBudgetBytes && _lru.Count > 1)
            {
                EvictOldest(out long freed);
                freedTotal += freed;
            }

            long gcThreshold = Math.Max(2L * 1024 * 1024, _cacheBudgetBytes / 8);
            if (freedTotal >= gcThreshold)
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                ForceGc();
            }
        }

        private void EvictOldest(out long freed)
        {
            freed = 0;
            if (_lru.Count == 0) return;

            long key = _lru.First.Value;
            _lru.RemoveFirst();

            if (_cache.TryGetValue(key, out var e))
            {
                _cache.Remove(key);
                _cacheBytes -= e.Bytes;
                freed = e.Bytes;
                e.Image.Dispose();
            }
        }

        private static void ForceGc()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        }

        private static long EstimateBytes(Image img)
        {
            int bpp = Image.GetPixelFormatSize(img.PixelFormat);
            long pixels = (long)img.Width * img.Height;
            return pixels * bpp / 8L + 1024;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _idleTimer?.Dispose();

            ClearCache();

            lock (_pageSizeSync)
                _pageSizeCache.Clear();

            lock (_readerSync)
            {
                if (_reader != null)
                {
                    lock (PdfiumLock)
                    {
                        try { _reader.Dispose(); } catch { }
                        _reader = null;
                    }
                }
            }

            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
        }
    }
}