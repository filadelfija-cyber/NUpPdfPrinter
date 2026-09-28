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
        private readonly float _paddingHundredths;

        private int _currentGroupIndex;
        private int _renderDpi;

        // ====================================================================
        //  Конструктор
        // ====================================================================
        public NUpPrintDocument(
            IPageImageSource source,
            int pagesPerSheet,
            bool duplex,
            bool longEdge,
            int renderDpi = 150,
            CellContentOrientation cellOrientation = CellContentOrientation.Auto,
            double paddingMm = 2.5)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (paddingMm < 0) paddingMm = 0;
            if (paddingMm > 50) paddingMm = 50;

            _source = source;
            _pagesPerSheet = pagesPerSheet;
            _duplex = duplex;
            _longEdge = longEdge;
            _renderDpi = Math.Max(36, renderDpi);
            _cellOrientation = cellOrientation;
            _paddingHundredths = (float)(paddingMm * 100.0 / 25.4);

            DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169)
            {
                RawKind = (int)PaperKind.A4
            };
            DefaultPageSettings.Margins = new Margins(25, 25, 25, 25);
            DefaultPageSettings.Landscape = false;

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

        // ====================================================================
        //  Публичный API для предпросмотра
        // ====================================================================
        public Bitmap RenderSheetToBitmap(int sheetIndex, int targetDpi, int sourceDpi)
        {
            if (sheetIndex < 0 || sheetIndex >= SheetCount)
                throw new ArgumentOutOfRangeException(nameof(sheetIndex));
            if (targetDpi < 24) targetDpi = 24;
            if (sourceDpi < 24) sourceDpi = 24;

            int w = DefaultPageSettings.PaperSize.Width;
            int h = DefaultPageSettings.PaperSize.Height;

            int pxW = Math.Max(1, (int)Math.Round(w / 100f * targetDpi));
            int pxH = Math.Max(1, (int)Math.Round(h / 100f * targetDpi));

            var bmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb);
            bmp.SetResolution(targetDpi, targetDpi);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                RenderSheet(g, sheetIndex, new Rectangle(0, 0, w, h), sourceDpi);
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

                // Дадим пользователю шанс увидеть детали, а не только «generic error».
                throw new InvalidOperationException(
                    "Ошибка GDI+ при печати листа " + (_currentGroupIndex + 1) +
                    ". Код: 0x" + ex.ErrorCode.ToString("X") + ". " +
                    "Подробности — в окне Output → Debug.", ex);
            }

            _currentGroupIndex++;
            e.HasMorePages = _currentGroupIndex * _pagesPerSheet < _paddedPageCount;
        }

        /// <summary>
        /// Изолирует драйвер принтера от сложных операций:
        /// лист сначала рисуется в memory bitmap (безопасно),
        /// затем одним DrawImage переносится на DC принтера.
        /// </summary>
        private void DrawSheetToPrinter(PrintPageEventArgs e, int sheetIndex)
        {
            var g = e.Graphics;

            // Определяем DPI для промежуточного bitmap.
            // Cap на 300 — выше качество не растёт, а память растёт квадратично.
            int printerDpi = (int)Math.Round(g.DpiX);
            if (printerDpi < 72) printerDpi = 150;
            if (printerDpi > 300) printerDpi = 300;

            // Размер листа в сотых дюйма.
            var bounds = e.PageBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = new Rectangle(0, 0,
                    DefaultPageSettings.PaperSize.Width,
                    DefaultPageSettings.PaperSize.Height);

            int pxW = Math.Max(1, (int)Math.Round(bounds.Width / 100.0 * printerDpi));
            int pxH = Math.Max(1, (int)Math.Round(bounds.Height / 100.0 * printerDpi));

            // Защита от «монструозных» bitmap — максимум ~120 МБ на кадр.
            long bytes = (long)pxW * pxH * 3;
            const long maxBytes = 120L * 1024 * 1024;
            if (bytes > maxBytes)
            {
                double k = Math.Sqrt((double)maxBytes / bytes);
                pxW = Math.Max(1, (int)(pxW * k));
                pxH = Math.Max(1, (int)(pxH * k));
                System.Diagnostics.Debug.WriteLine(
                    $"[NUpPrintDocument] Sheet bitmap reduced to {pxW}x{pxH} " +
                    $"({printerDpi} DPI → cap)");
            }

            using (var sheetBmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb))
            {
                sheetBmp.SetResolution(printerDpi, printerDpi);

                using (var gb = Graphics.FromImage(sheetBmp))
                {
                    gb.Clear(Color.White);
                    RenderSheet(gb, sheetIndex, bounds, _renderDpi);
                }

                // --- Blit на DC принтера — максимально просто и безопасно ---
                var savedUnit = g.PageUnit;
                var savedScale = g.PageScale;

                try
                {
                    g.PageUnit = GraphicsUnit.Display; // = 1/100" на принтере
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
            // Единицы всегда 1/100 дюйма — совпадает с PaperSize и PageBounds.
            g.PageUnit = GraphicsUnit.Inch;
            g.PageScale = 0.01f;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool sheetLandscape = pageBounds.Width > pageBounds.Height;
            var grid = GetGrid(_pagesPerSheet, sheetLandscape);
            int rows = grid.rows;
            int cols = grid.cols;

            List<int> pageIndices = BuildPageIndices(sheetIndex, rows, cols);

            float cellW = pageBounds.Width / (float)cols;
            float cellH = pageBounds.Height / (float)rows;

            for (int i = 0; i < pageIndices.Count; i++)
            {
                int pageIdx = pageIndices[i];
                if (pageIdx < 0 || pageIdx >= _source.PageCount)
                    continue;

                Image img = _source.GetPage(pageIdx, sourceDpi);
                if (img == null) continue;

                int row = i / cols;
                int col = i % cols;
                var cell = new RectangleF(col * cellW, row * cellH, cellW, cellH);

                DrawPageFit(g, img, cell, _cellOrientation, _paddingHundredths);
                DrawCellFrame(g, cell);
            }
        }

        // ====================================================================
        //  Раскладка страниц (нечётные на лице, чётные на обороте)
        // ====================================================================
        private List<int> BuildPageIndices(int sheetIndex, int rows, int cols)
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
                // Лицевая: 1, 3, 5, … (1-indexed) в порядке чтения.
                for (int i = 0; i < n; i++)
                {
                    int page = p0 + 2 * i;
                    result.Add(page < _source.PageCount ? page : -1);
                }
            }
            else
            {
                // Обратная: печатаем зеркально, чтобы после переворота листа
                // чётные страницы встали ровно за своими нечётными.
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols;
                    int c = i % cols;

                    int viewingIndex;
                    if (_longEdge)
                    {
                        int vc = cols - 1 - c;         // зеркалим столбцы
                        viewingIndex = r * cols + vc;
                    }
                    else
                    {
                        int vr = rows - 1 - r;         // зеркалим строки
                        viewingIndex = vr * cols + c;
                    }

                    int page = p0 + 2 * viewingIndex + 1;
                    result.Add(page < _source.PageCount ? page : -1);
                }
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