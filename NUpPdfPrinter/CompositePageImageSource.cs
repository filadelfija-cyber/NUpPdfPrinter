using System;
using System.Collections.Generic;
using System.Drawing;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Объединяет несколько IPageImageSource в один «виртуальный документ»:
    /// страницы нумеруются сквозным образом через все файлы по порядку добавления.
    ///
    /// Все механизмы NUpPrintDocument (диапазон страниц, дуплекс, N-up,
    /// предпросмотр, кэш) работают с глобальными номерами страниц этого
    /// источника и не знают, сколько внутри файлов.
    /// </summary>
    public sealed class CompositePageImageSource : IPageImageSource
    {
        private readonly List<IPageImageSource> _sources = new List<IPageImageSource>();

        /// <summary>Глобальный индекс первой страницы каждого источника.</summary>
        private readonly List<int> _startIndices = new List<int>();

        private int _totalPages;
        private bool _disposed;

        /// <summary>
        /// Добавить документ в конец виртуального списка.
        /// Вызывать до первого GetPage — после начала работы список не меняется.
        /// </summary>
        public void Add(IPageImageSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (_disposed) throw new ObjectDisposedException(nameof(CompositePageImageSource));

            _startIndices.Add(_totalPages);
            _sources.Add(source);
            _totalPages += source.PageCount;
        }

        public int PageCount => _totalPages;

        /// <summary>Суммарный объём кэша всех вложенных источников.</summary>
        public long CacheBytes
        {
            get
            {
                long total = 0;
                foreach (var s in _sources)
                {
                    try { total += s.CacheBytes; }
                    catch { /* источник мог быть закрыт */ }
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

        /// <summary>
        /// Находит индекс источника, которому принадлежит глобальная страница index.
        /// Линейный поиск с конца — файлов обычно немного.
        /// </summary>
        private int FindSourceIndex(int index)
        {
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                if (_startIndices[i] <= index) return i;
            }
            return 0;
        }
    }
}