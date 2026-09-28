using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace NUpPdfPrinter
{
    public enum CellContentOrientation
    {
        Auto = 0,
        Portrait = 1,
        Landscape = 2
    }

    public sealed class NUpPrintDocument : PrintDocument
    {
        private readonly IPageImageSource _source;
        private readonly int _pagesPerSheet;
        private readonly bool _duplex;
        private readonly bool _longEdge;
        private readonly int _paddedPageCount;
        private readonly CellContentOrientation _cellOrientation;

        /// <summary>Отступ вокруг страницы внутри ячейки, в сотых долях дюйма.</summary>
        private readonly float _paddingHundredths;

        /// <summary>Отступ от края листа A4, в сотых долях дюйма.</summary>
        private readonly float _pageMarginHundredths;

        private int _currentGroupIndex;
        private int _renderDpi;

        // ====================================================================
        //  Конструктор
        // ====================================================================
        /// <param name="pageMarginMm">
        /// Отступ от края листа A4 в мм (0…30). Аналог «полей» в Word.
        /// Реальная рабочая область = A4 минус 2 × pageMarginMm с каждой стороны.
        /// </param>
        /// <param name="paddingMm">
        /// Отступ вокруг страницы внутри ячейки в мм (0…20).
        /// </param>
        public NUpPrintDocument(
            IPageImageSource source,
            int pagesPerSheet,
            bool duplex,
            bool longEdge,
            int renderDpi = 150,
            CellContentOrientation cellOrientation = CellContentOrientation.Auto,
            double paddingMm = 2.5,
            double pageMarginMm = 6.35)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (paddingMm < 0) paddingMm = 0;
            if (paddingMm > 50) paddingMm = 50;
            if (pageMarginMm < 0) pageMarginMm = 0;
            if (pageMarginMm > 50) pageMarginMm = 50;

            _source = source;
            _pagesPerSheet = pagesPerSheet;
            _duplex = duplex;
            _longEdge = longEdge;
            _renderDpi = Math.Max(36, renderDpi);
            _cellOrientation = cellOrientation;

            // 1 мм = 1/25.4" = 100/25.4 сотых дюйма ≈ 3.937
            _paddingHundredths = (float)(paddingMm * 100.0 / 25.4);
            _pageMarginHundredths = (float)(pageMarginMm * 100.0 / 25.4);

            // A4 в сотых долях дюйма. Ориентация по умолчанию — портрет.
            DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169)
            {
                RawKind = (int)PaperKind.A4
            };
            DefaultPageSettings.Landscape = false;

            // Margins в сотых долях дюйма; применяются драйвером, если он умеет.
            int mg = (int)Math.Round(_pageMarginHundredths);
            DefaultPageSettings.Margins = new Margins(mg, mg, mg, mg);

            PrinterSettings.Duplex = duplex
                ? (longEdge ? Duplex.Vertical : Duplex.Horizontal)
                : Duplex.Simplex;

            int block = duplex ? 2 * pagesPerSheet : pagesPerSheet;
            int srcCount = _source.PageCount;
            _paddedPageCount = ((srcCount + block - 1) / block) * block;
        }

        // ====================================================================
        //  Публичные свойства
        // ====================================================================
        public int PagesPerSheet => _pagesPerSheet;
        public int OriginalPageCount => _source.PageCount;
        public int PaddedPageCount => _paddedPageCount;
        public int SheetCount => _paddedPageCount / _pagesPerSheet;
        public CellContentOrientation CellOrientation => _cellOrientation;

        public int RenderDpi
        {
            get => _renderDpi;
            set => _renderDpi = Math.Max(36, value);
        }

        /// <summary>
        /// Ориентация листа. Влияет и на предпросмотр, и на печать.
        /// </summary>
        public bool Landscape
        {
            get => DefaultPageSettings.Landscape;
            set => DefaultPageSettings.Landscape = value;
        }

        // ====================================================================
        //  Публичный API для предпросмотра
        // ====================================================================
        public Bitmap RenderSheetToBitmap(int sheetIndex, int targetDpi, int sourceDpi)
        {
            if (sheetIndex < 0 || sheetIndex >= SheetCount)
                throw new ArgumentOutOfRangeException(nameof(sheetIndex));
            if (targetDpi < 24) targetDpi = 24;
            if (sourceDpi < 24) sourceDpi = 24;

            // Прямоугольник листа в 1/100". При ландшафте меняем стороны местами.
            int w = DefaultPageSettings.PaperSize.Width;
            int h = DefaultPageSettings.PaperSize.Height;

            Rectangle bounds = DefaultPageSettings.Landscape
                ? new Rectangle(0, 0, h, w)
                : new Rectangle(0, 0, w, h);

            int pxW = Math.Max(1, (int)Math.Round(bounds.Width / 100.0 * targetDpi));
            int pxH = Math.Max(1, (int)Math.Round(bounds.Height / 100.0 * targetDpi));

            var bmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb);
            bmp.SetResolution(targetDpi, targetDpi);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                RenderSheet(g, sheetIndex, bounds, sourceDpi);
            }
            return bmp;
        }

        // ====================================================================
        //  Печать
        // ====================================================================
        protected override void OnBeginPrint(PrintEventArgs e)
        {
            base.OnBeginPrint(e);
            _currentGroupIndex = 0;
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            base.OnPrintPage(e);

            try
            {
                DrawSheetToPrinter(e, _currentGroupIndex);
            }
            catch (ExternalException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NUpPrintDocument] GDI+ EXTERNAL EXCEPTION on sheet {_currentGroupIndex}: " +
                    $"Code=0x{ex.ErrorCode:X}, Msg='{ex.Message}', " +
                    $"PageBounds={e.PageBounds}, PageUnit={e.Graphics.PageUnit}, " +
                    $"DpiX={e.Graphics.DpiX}, DpiY={e.Graphics.DpiY}, " +
                    $"Printer={PrinterSettings.PrinterName}");

                throw new InvalidOperationException(
                    "Ошибка GDI+ при печати листа " + (_currentGroupIndex + 1) +
                    ". Код: 0x" + ex.ErrorCode.ToString("X") + ". " +
                    "Подробности — в окне Output → Debug.", ex);
            }

            _currentGroupIndex++;
            e.HasMorePages = _currentGroupIndex * _pagesPerSheet < _paddedPageCount;
        }

        private void DrawSheetToPrinter(PrintPageEventArgs e, int sheetIndex)
        {
            var g = e.Graphics;

            int printerDpi = (int)Math.Round(g.DpiX);
            if (printerDpi < 72) printerDpi = 150;
            if (printerDpi > 300) printerDpi = 300;

            var bounds = e.PageBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                int w = DefaultPageSettings.PaperSize.Width;
                int h = DefaultPageSettings.PaperSize.Height;
                bounds = DefaultPageSettings.Landscape
                    ? new Rectangle(0, 0, h, w)
                    : new Rectangle(0, 0, w, h);
            }

            int pxW = Math.Max(1, (int)Math.Round(bounds.Width / 100.0 * printerDpi));
            int pxH = Math.Max(1, (int)Math.Round(bounds.Height / 100.0 * printerDpi));

            long bytes = (long)pxW * pxH * 3;
            const long maxBytes = 120L * 1024 * 1024;
            if (bytes > maxBytes)
            {
                double k = Math.Sqrt((double)maxBytes / bytes);
                pxW = Math.Max(1, (int)(pxW * k));
                pxH = Math.Max(1, (int)(pxH * k));
                System.Diagnostics.Debug.WriteLine(
                    $"[NUpPrintDocument] Sheet bitmap reduced to {pxW}x{pxH}");
            }

            using (var sheetBmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb))
            {
                sheetBmp.SetResolution(printerDpi, printerDpi);

                using (var gb = Graphics.FromImage(sheetBmp))
                {
                    gb.Clear(Color.White);
                    RenderSheet(gb, sheetIndex, bounds, _renderDpi);
                }

                var savedUnit = g.PageUnit;
                var savedScale = g.PageScale;

                try
                {
                    g.PageUnit = GraphicsUnit.Display;
                    g.PageScale = 1.0f;
                    g.InterpolationMode = InterpolationMode.Default;
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.Default;

                    g.DrawImage(sheetBmp, bounds);
                }
                finally
                {
                    g.PageUnit = savedUnit;
                    g.PageScale = savedScale;
                }
            }
        }

        // ====================================================================
        //  Отрисовка одного листа (всегда в memory bitmap)
        // ====================================================================
        private void RenderSheet(Graphics g, int sheetIndex, Rectangle pageBounds, int sourceDpi)
        {
            g.PageUnit = GraphicsUnit.Inch;
            g.PageScale = 0.01f;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // --- Рабочая область внутри листа с учётом полей ---
            float mgX = _pageMarginHundredths;
            float mgY = _pageMarginHundredths;

            // Ландшафтная бумага: ширины и высоты уже поменяны в pageBounds,
            // поэтому просто вычитаем одинаковый отступ со всех четырёх сторон.
            float innerX = pageBounds.X + mgX;
            float innerY = pageBounds.Y + mgY;
            float innerW = pageBounds.Width - mgX * 2f;
            float innerH = pageBounds.Height - mgY * 2f;

            if (innerW <= 1f || innerH <= 1f) return; // отступы съели лист

            // Ориентация листа для сетки определяется по исходным pageBounds.
            bool sheetLandscape = pageBounds.Width > pageBounds.Height;

            var grid = GetGrid(_pagesPerSheet, sheetLandscape);
            int rows = grid.rows;
            int cols = grid.cols;

            List<int> pageIndices = BuildPageIndices(sheetIndex, rows, cols, sheetLandscape);

            // Ячейки рассчитываются от рабочей области, а не от краёв листа.
            float cellW = innerW / cols;
            float cellH = innerH / rows;

            for (int i = 0; i < pageIndices.Count; i++)
            {
                int pageIdx = pageIndices[i];
                if (pageIdx < 0 || pageIdx >= _source.PageCount)
                    continue;

                Image img = _source.GetPage(pageIdx, sourceDpi);
                if (img == null) continue;

                int row = i / cols;
                int col = i % cols;
                var cell = new RectangleF(
                    innerX + col * cellW,
                    innerY + row * cellH,
                    cellW, cellH);

                DrawPageFit(g, img, cell, _cellOrientation, _paddingHundredths);
                DrawCellFrame(g, cell);
            }
        }

        // ====================================================================
        //  Раскладка страниц (нечётные на лице, чётные на обороте)
        // ====================================================================
        private List<int> BuildPageIndices(int sheetIndex, int rows, int cols, bool sheetLandscape)
        {
            int n = _pagesPerSheet;
            var result = new List<int>(n);

            // ---------- Симплекс ----------
            if (!_duplex)
            {
                int start = sheetIndex * n;
                for (int i = 0; i < n; i++)
                {
                    int page = start + i;
                    result.Add(page < _source.PageCount ? page : -1);
                }
                return result;
            }

            // ---------- Дуплекс ----------
            bool isBack = (sheetIndex % 2) == 1;
            int pairIndex = sheetIndex / 2;
            int p0 = pairIndex * 2 * n;

            if (!isBack)
            {
                for (int i = 0; i < n; i++)
                {
                    int page = p0 + 2 * i;
                    result.Add(page < _source.PageCount ? page : -1);
                }
                return result;
            }

            // Обратная сторона: выбираем направление зеркала.
            bool mirrorColumns = sheetLandscape ? !_longEdge : _longEdge;

            for (int i = 0; i < n; i++)
            {
                int r = i / cols;
                int c = i % cols;

                int viewingIndex;
                if (mirrorColumns)
                {
                    int vc = cols - 1 - c;
                    viewingIndex = r * cols + vc;
                }
                else
                {
                    int vr = rows - 1 - r;
                    viewingIndex = vr * cols + c;
                }

                int page = p0 + 2 * viewingIndex + 1;
                result.Add(page < _source.PageCount ? page : -1);
            }

            return result;
        }

        // ====================================================================
        //  Сетка
        // ====================================================================
        private static (int rows, int cols) GetGrid(int n, bool sheetLandscape)
        {
            int shortDiv, longDiv;
            switch (n)
            {
                case 4: shortDiv = 2; longDiv = 2; break;
                case 6: shortDiv = 2; longDiv = 3; break;
                case 8: shortDiv = 2; longDiv = 4; break;
                case 9: shortDiv = 3; longDiv = 3; break;
                default:
                    throw new ArgumentException(
                        "Поддерживаются только 4, 6, 8 или 9 страниц на листе.", nameof(n));
            }

            return sheetLandscape
                ? (rows: shortDiv, cols: longDiv)
                : (rows: longDiv, cols: shortDiv);
        }

        // ====================================================================
        //  Отрисовка страницы в ячейке
        // ====================================================================
        private static void DrawPageFit(
            Graphics g, Image img, RectangleF cell,
            CellContentOrientation orientation, float paddingHundredths)
        {
            float availW = cell.Width - paddingHundredths * 2f;
            float availH = cell.Height - paddingHundredths * 2f;
            if (availW <= 1f || availH <= 1f) return;

            float srcW = img.Width;
            float srcH = img.Height;
            if (srcW <= 0 || srcH <= 0) return;

            bool rotate;
            switch (orientation)
            {
                case CellContentOrientation.Portrait: rotate = srcW > srcH; break;
                case CellContentOrientation.Landscape: rotate = srcH > srcW; break;
                default:
                    float sNo = Math.Min(availW / srcW, availH / srcH);
                    float sRo = Math.Min(availW / srcH, availH / srcW);
                    rotate = sRo > sNo * 1.001f;
                    break;
            }

            float visW = rotate ? srcH : srcW;
            float visH = rotate ? srcW : srcH;
            float scale = Math.Min(availW / visW, availH / visH);

            float finalW = srcW * scale;
            float finalH = srcH * scale;

            float cx = cell.X + cell.Width / 2f;
            float cy = cell.Y + cell.Height / 2f;

            var state = g.Save();
            try
            {
                g.TranslateTransform(cx, cy);
                if (rotate) g.RotateTransform(90f);
                g.DrawImage(img, -finalW / 2f, -finalH / 2f, finalW, finalH);
            }
            finally { g.Restore(state); }
        }

        private static void DrawCellFrame(Graphics g, RectangleF cell)
        {
            using (var pen = new Pen(Color.FromArgb(180, 180, 180), 1f))
                g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
        }
    }
}