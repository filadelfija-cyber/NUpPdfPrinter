using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumViewer;

namespace NUpPdfPrinter
{
    public sealed class PdfPageImageSource : IPageImageSource
    {
        private sealed class Entry
        {
            public Image Image;
            public long Bytes;
        }

        private readonly PdfDocument _doc;
        private readonly long _cacheBudgetBytes;
        private readonly Dictionary<long, Entry> _cache = new Dictionary<long, Entry>();
        private readonly LinkedList<long> _lru = new LinkedList<long>();

        private long _cacheBytes;
        private bool _disposed;

        public PdfPageImageSource(string filePath, long cacheBudgetBytes = 48L * 1024 * 1024)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF-файл не найден.", filePath);

            _doc = PdfDocument.Load(filePath);
            _cacheBudgetBytes = Math.Max(0, cacheBudgetBytes);
        }

        public int PageCount => _doc.PageCount;
        public long CacheBytes => _cacheBytes;

        public Image GetPage(int index, int dpi)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PdfPageImageSource));
            if (index < 0 || index >= _doc.PageCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            long key = ((long)dpi << 32) | (uint)index;

            // ИСПРАВЛЕНО: сначала проверяем кэш — даже при бюджете 0.
            if (_cache.TryGetValue(key, out var hit))
            {
                _lru.Remove(key);
                _lru.AddLast(key);
                return hit.Image;
            }

            var img = RenderPageBitmap(index, dpi);
            long bytes = EstimateBytes(img);
            AddToCache(key, img, bytes);

            // При бюджете 0 эффективный бюджет — ровно один текущий кадр.
            // Так эвикция снесёт всё, кроме только что добавленного.
            long effectiveBudget = _cacheBudgetBytes == 0 ? bytes : _cacheBudgetBytes;
            EvictUntilBudget(effectiveBudget);

            return img;
        }

        public void ClearCache()
        {
            long freed = 0;
            foreach (var e in _cache.Values)
            {
                freed += e.Bytes;
                e.Image.Dispose();
            }
            _cache.Clear();
            _lru.Clear();
            _cacheBytes = 0;

            // ИСПРАВЛЕНО: понижен порог; GDI+ освобождает нативные копии
            // только после сборки мусора.
            if (freed > 1L * 1024 * 1024)
                ForceGc();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearCache();
            _doc?.Dispose();
        }

        // ---------- внутреннее ----------

        private void AddToCache(long key, Image img, long bytes)
        {
            _cache[key] = new Entry { Image = img, Bytes = bytes };
            _lru.AddLast(key);
            _cacheBytes += bytes;
        }

        private void EvictUntilBudget(long budget)
        {
            long freedTotal = 0;

            // Эвиктим с головы (самая старая), пока не уложимся в бюджет.
            // Останавливаемся, когда останется ровно один элемент — тот,
            // который только что добавили (он всегда в хвосте списка).
            while (_cacheBytes > budget && _lru.Count > 1)
            {
                long oldest = _lru.First.Value;
                _lru.RemoveFirst();

                if (_cache.TryGetValue(oldest, out var e))
                {
                    _cache.Remove(oldest);
                    _cacheBytes -= e.Bytes;
                    freedTotal += e.Bytes;
                    e.Image.Dispose();
                }
            }

            // ИСПРАВЛЕНО: порог GC привязан к бюджету (не менее 4 MB).
            long gcThreshold = Math.Max(4L * 1024 * 1024, _cacheBudgetBytes / 4);
            if (freedTotal >= gcThreshold)
                ForceGc();
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
            return pixels * bpp / 8L + 1024; // +1 KB на заголовки GDI+
        }

        private Image RenderPageBitmap(int index, int dpi)
        {
            var size = _doc.PageSizes[index];
            int width = Math.Max(1, (int)Math.Round(size.Width / 72.0 * dpi));
            int height = Math.Max(1, (int)Math.Round(size.Height / 72.0 * dpi));

            using (var raw = _doc.Render(
                index, width, height, dpi, dpi,
                PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations))
            {
                var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                bmp.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.DrawImageUnscaled(raw, 0, 0);
                }
                return bmp;
            }
        }
    }
}