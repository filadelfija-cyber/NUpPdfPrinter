using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;

namespace NUpPdfPrinter
{
    public sealed class NUpPrintDocument : PrintDocument
    {
        private readonly IPageImageSource _source;
        private readonly int _pagesPerSheet;
        private readonly int _rows;
        private readonly int _cols;
        private readonly bool _duplex;
        private readonly bool _longEdge;

        // Итоговое число логических страниц (с учётом дополнения пустыми).
        private readonly int _paddedPageCount;

        private int _currentGroupIndex;
        private int _renderDpi;

        public NUpPrintDocument(
            IPageImageSource source,
            int pagesPerSheet,
            bool duplex,
            bool longEdge,
            int renderDpi = 150)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            _source = source;
            _pagesPerSheet = pagesPerSheet;
            _duplex = duplex;
            _longEdge = longEdge;
            _renderDpi = renderDpi;

            var grid = GetGrid(pagesPerSheet);
            _rows = grid.rows;
            _cols = grid.cols;

            DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
            DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);
            DefaultPageSettings.Landscape = false;

            PrinterSettings.Duplex = duplex
                ? (longEdge ? Duplex.Vertical : Duplex.Horizontal)
                : Duplex.Simplex;

            int block = duplex ? 2 * pagesPerSheet : pagesPerSheet;
            _paddedPageCount = ((_source.PageCount + block - 1) / block) * block;
        }

        public int PagesPerSheet => _pagesPerSheet;
        public int OriginalPageCount => _source.PageCount;
        public int PaddedPageCount => _paddedPageCount;
        public int SheetCount => _paddedPageCount / _pagesPerSheet;
        public int RenderDpi
        {
            get => _renderDpi;
            set => _renderDpi = Math.Max(36, value);
        }

        private static (int rows, int cols) GetGrid(int n)
        {
            switch (n)
            {
                case 4: return (2, 2);
                case 6: return (2, 3);
                case 8: return (2, 4);
                case 9: return (3, 3);
                default:
                    throw new ArgumentException(
                        "Поддерживаются только 4, 6, 8 или 9 страниц на листе.", nameof(n));
            }
        }

        protected override void OnBeginPrint(PrintEventArgs e)
        {
            base.OnBeginPrint(e);
            _currentGroupIndex = 0;
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            base.OnPrintPage(e);

            var g = e.Graphics;
            g.PageUnit = GraphicsUnit.Point;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool isBackSide = _duplex && (_currentGroupIndex % 2 == 1);

            int startPage = _currentGroupIndex * _pagesPerSheet;

            // Список логических индексов для текущего листа.
            var pageIndices = new List<int>(_pagesPerSheet);
            for (int i = 0; i < _pagesPerSheet; i++)
            {
                int idx = startPage + i;
                pageIndices.Add(idx < _paddedPageCount ? idx : -1);
            }

            if (isBackSide)
                pageIndices = ReorderForDuplex(pageIndices, _longEdge);

            float cellW = e.PageBounds.Width / (float)_cols;
            float cellH = e.PageBounds.Height / (float)_rows;

            // === КЛЮЧЕВОЕ: рендерим только нужные страницы и сразу освобождаем ===
            var rendered = new Dictionary<int, Image>();

            try
            {
                for (int i = 0; i < pageIndices.Count; i++)
                {
                    int pageIdx = pageIndices[i];
                    if (pageIdx < 0 || pageIdx >= _source.PageCount)
                        continue; // пустая страница-заполнитель

                    Image img;
                    if (!rendered.TryGetValue(pageIdx, out img))
                    {
                        img = _source.RenderPage(pageIdx, _renderDpi);
                        rendered[pageIdx] = img;
                    }

                    int row = i / _cols;
                    int col = i % _cols;
                    var cell = new RectangleF(col * cellW, row * cellH, cellW, cellH);

                    DrawPageFit(g, img, cell);
                    DrawCellFrame(g, cell);
                }
            }
            finally
            {
                // Немедленно освобождаем память до перехода к следующему листу.
                foreach (var img in rendered.Values)
                    img.Dispose();
            }

            _currentGroupIndex++;
            e.HasMorePages = _currentGroupIndex * _pagesPerSheet < _paddedPageCount;
        }

        private static void DrawPageFit(Graphics g, Image img, RectangleF cell)
        {
            const float padding = 4f;
            float availW = cell.Width - padding * 2;
            float availH = cell.Height - padding * 2;

            float scale = Math.Min(availW / img.Width, availH / img.Height);
            float drawW = img.Width * scale;
            float drawH = img.Height * scale;
            float x = cell.X + (cell.Width - drawW) / 2f;
            float y = cell.Y + (cell.Height - drawH) / 2f;

            g.DrawImage(img, x, y, drawW, drawH);
        }

        private static void DrawCellFrame(Graphics g, RectangleF cell)
        {
            using (var pen = new Pen(Color.FromArgb(180, 180, 180), 0.5f))
                g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
        }

        private List<int> ReorderForDuplex(List<int> indices, bool longEdge)
        {
            var result = new List<int>(indices);

            if (longEdge)
            {
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _cols / 2; c++)
                    {
                        int a = r * _cols + c;
                        int b = r * _cols + (_cols - 1 - c);
                        int t = result[a]; result[a] = result[b]; result[b] = t;
                    }
            }
            else
            {
                for (int r = 0; r < _rows / 2; r++)
                    for (int c = 0; c < _cols; c++)
                    {
                        int a = r * _cols + c;
                        int b = (_rows - 1 - r) * _cols + c;
                        int t = result[a]; result[a] = result[b]; result[b] = t;
                    }
            }
            return result;
        }
    }
}