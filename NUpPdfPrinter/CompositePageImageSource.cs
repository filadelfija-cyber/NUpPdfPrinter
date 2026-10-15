using System;
using System.Collections.Generic;
using System.Drawing;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Объединяет несколько IPageImageSource в один «виртуальный документ»:
    /// сквозная нумерация страниц через все файлы по порядку добавления.
    /// Все механизмы NUpPrintDocument работают с глобальными номерами страниц
    /// и не знают, сколько внутри файлов.
    /// </summary>
    public sealed class CompositePageImageSource : IPageImageSource
    {
        private readonly List<IPageImageSource> _sources = new List<IPageImageSource>();
        private readonly List<int> _startIndices = new List<int>();

        private int _totalPages;
        private bool _disposed;

        public void Add(IPageImageSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (_disposed) throw new ObjectDisposedException(nameof(CompositePageImageSource));

            _startIndices.Add(_totalPages);
            _sources.Add(source);
            _totalPages += source.PageCount;
        }

        public int PageCount => _totalPages;

        public long CacheBytes
        {
            get
            {
                long total = 0;
                foreach (var s in _sources)
                {
                    try { total += s.CacheBytes; } catch { }
                }
                return total;
            }
        }

        public Image GetPage(int index, int dpi)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CompositePageImageSource));
            if (index < 0 || index >= _totalPages)
                throw new ArgumentOutOfRangeException(nameof(index));

            int srcIdx = FindSourceIndex(index);
            int localIndex = index - _startIndices[srcIdx];
            return _sources[srcIdx].GetPage(localIndex, dpi);
        }

        public void ClearCache()
        {
            foreach (var s in _sources) s.ClearCache();
        }

        public void ReleaseNativeResources()
        {
            foreach (var s in _sources)
            {
                try { s.ReleaseNativeResources(); } catch { }
            }
        }

        public IDisposable SuppressEviction()
        {
            var handles = new List<IDisposable>(_sources.Count);
            foreach (var s in _sources)
            {
                try { handles.Add(s.SuppressEviction()); } catch { }
            }
            return new CompositeHandle(handles);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var s in _sources)
            {
                try { s.Dispose(); } catch { }
            }
            _sources.Clear();
            _startIndices.Clear();
            _totalPages = 0;
        }

        private int FindSourceIndex(int index)
        {
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                if (_startIndices[i] <= index) return i;
            }
            return 0;
        }

        private sealed class CompositeHandle : IDisposable
        {
            private List<IDisposable> _handles;
            public CompositeHandle(List<IDisposable> handles) { _handles = handles; }
            public void Dispose()
            {
                var list = System.Threading.Interlocked.Exchange(ref _handles, null);
                if (list == null) return;
                foreach (var h in list)
                {
                    try { h.Dispose(); } catch { }
                }
            }
        }
    }
}