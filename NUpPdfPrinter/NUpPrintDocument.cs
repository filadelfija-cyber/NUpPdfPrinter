using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Linq;
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

        // Отступы внутри ячейки (padding) — одинаковые со всех сторон.
        private readonly float _paddingHundredths;

        // Отступы от края листа A4 — раздельные по сторонам.
        private readonly float _marginLeftHundredths;
        private readonly float _marginRightHundredths;
        private readonly float _marginTopHundredths;
        private readonly float _marginBottomHundredths;

        private int[] _activeSheetIndices = Array.Empty<int>();
        private int _firstPage = 1;
        private int _lastPage = -1;

        private int _currentGroupIndex;
        private int _renderDpi;

        // ====================================================================
        //  Совмещение сторон
        // ====================================================================
        public double BackOffsetXMm { get; set; } = 0;
        public double BackOffsetYMm { get; set; } = 0;
        public bool DrawRegistrationMarks { get; set; } = false;

        // ====================================================================
        //  Выбор страниц
        // ====================================================================
        public int FirstPage
        {
            get => _firstPage;
            set { _firstPage = value; RecomputeActiveSheets(); }
        }
        public int LastPage
        {
            get => _lastPage;
            set { _lastPage = value; RecomputeActiveSheets(); }
        }

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
            double paddingMm = 2.5,
            double marginLeftMm = 5.0,
            double marginRightMm = 5.0,
            double marginTopMm = 5.0,
            double marginBottomMm = 5.0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            paddingMm = Clamp(paddingMm, 0, 50);
            marginLeftMm = Clamp(marginLeftMm, 0, 50);
            marginRightMm = Clamp(marginRightMm, 0, 50);
            marginTopMm = Clamp(marginTopMm, 0, 50);
            marginBottomMm = Clamp(marginBottomMm, 0, 50);

            _source = source;
            _pagesPerSheet = pagesPerSheet;
            _duplex = duplex;
            _longEdge = longEdge;
            _renderDpi = Math.Max(36, renderDpi);
            _cellOrientation = cellOrientation;

            _paddingHundredths = MmToHundredths(paddingMm);
            _marginLeftHundredths = MmToHundredths(marginLeftMm);
            _marginRightHundredths = MmToHundredths(marginRightMm);
            _marginTopHundredths = MmToHundredths(marginTopMm);
            _marginBottomHundredths = MmToHundredths(marginBottomMm);

            DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169)
            {
                RawKind = (int)PaperKind.A4
            };
            DefaultPageSettings.Landscape = false;

            DefaultPageSettings.Margins = new Margins(
                (int)Math.Round(_marginLeftHundredths),
                (int)Math.Round(_marginRightHundredths),
                (int)Math.Round(_marginTopHundredths),
                (int)Math.Round(_marginBottomHundredths));

            PrinterSettings.Duplex = duplex
                ? (longEdge ? Duplex.Vertical : Duplex.Horizontal)
                : Duplex.Simplex;

            int block = duplex ? 2 * pagesPerSheet : pagesPerSheet;
            int srcCount = _source.PageCount;
            _paddedPageCount = ((srcCount + block - 1) / block) * block;

            RecomputeActiveSheets();
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);

        private static float MmToHundredths(double mm) => (float)(mm * 100.0 / 25.4);

        // ====================================================================
        //  Публичные свойства
        // ====================================================================
        public int PagesPerSheet => _pagesPerSheet;
        public int OriginalPageCount => _source.PageCount;
        public int PaddedPageCount => _paddedPageCount;
        public int SheetCount => _activeSheetIndices.Length;
        public CellContentOrientation CellOrientation => _cellOrientation;

        public int RenderDpi
        {
            get => _renderDpi;
            set => _renderDpi = Math.Max(36, value);
        }

        public bool Landscape
        {
            get => DefaultPageSettings.Landscape;
            set => DefaultPageSettings.Landscape = value;
        }

        // ====================================================================
        //  Диапазон страниц → набор активных сторон
        // ====================================================================
        private void RecomputeActiveSheets()
        {
            int srcCount = _source.PageCount;
            if (srcCount <= 0) { _activeSheetIndices = Array.Empty<int>(); return; }

            int first = Math.Max(1, Math.Min(_firstPage, srcCount));
            int last = _lastPage < 0
                ? srcCount
                : Math.Max(first, Math.Min(_lastPage, srcCount));

            var active = new HashSet<int>();
            for (int p = first - 1; p <= last - 1; p++)
            {
                int sheet;
                if (_duplex)
                {
                    int L = p / (2 * _pagesPerSheet);
                    bool isBack = (p % 2) == 1;
                    sheet = 2 * L + (isBack ? 1 : 0);
                }
                else
                {
                    sheet = p / _pagesPerSheet;
                }
                active.Add(sheet);
            }

            if (_duplex)
            {
                var expanded = new HashSet<int>();
                foreach (int s in active)
                {
                    int pair = (s / 2) * 2;
                    expanded.Add(pair);
                    expanded.Add(pair + 1);
                }
                active = expanded;
            }

            _activeSheetIndices = active.OrderBy(x => x).ToArray();
        }

        private bool IsPageSelected(int page0)
        {
            if (page0 < 0 || page0 >= _source.PageCount) return false;
            int srcCount = _source.PageCount;
            int first = Math.Max(1, Math.Min(_firstPage, srcCount));
            int last = _lastPage < 0
                ? srcCount
                : Math.Max(first, Math.Min(_lastPage, srcCount));
            return page0 >= first - 1 && page0 <= last - 1;
        }

        // ====================================================================
        //  Предпросмотр
        // ====================================================================
        public Bitmap RenderSheetToBitmap(int sheetIndex, int targetDpi, int sourceDpi)
        {
            if (sheetIndex < 0 || sheetIndex >= _activeSheetIndices.Length)
                throw new ArgumentOutOfRangeException(nameof(sheetIndex));
            if (targetDpi < 24) targetDpi = 24;
            if (sourceDpi < 24) sourceDpi = 24;

            int physicalIdx = _activeSheetIndices[sheetIndex];

            int w = DefaultPageSettings.PaperSize.Width;
            int h = DefaultPageSettings.PaperSize.Height;

            Rectangle paperBounds = DefaultPageSettings.Landscape
                ? new Rectangle(0, 0, h, w)
                : new Rectangle(0, 0, w, h);

            int pxW = Math.Max(1, (int)Math.Round(paperBounds.Width / 100.0 * targetDpi));
            int pxH = Math.Max(1, (int)Math.Round(paperBounds.Height / 100.0 * targetDpi));

            var bmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb);
            bmp.SetResolution(targetDpi, targetDpi);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                // Для предпросмотра «бумага» = весь Bitmap, и мы отнимаем
                // наши заданные отступы с четырёх сторон.
                DrawSheetContent(g, physicalIdx, paperBounds, sourceDpi);
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

            if (_currentGroupIndex >= _activeSheetIndices.Length)
            {
                e.HasMorePages = false;
                return;
            }

            int physicalIdx = _activeSheetIndices[_currentGroupIndex];

            try
            {
                DrawSheetToPrinter(e, physicalIdx);
            }
            catch (ExternalException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NUpPrintDocument] GDI+ EXTERNAL EXCEPTION on sheet {physicalIdx}: " +
                    $"Code=0x{ex.ErrorCode:X}, Msg='{ex.Message}', " +
                    $"PageBounds={e.PageBounds}, DpiX={e.Graphics.DpiX}, " +
                    $"Printer={PrinterSettings.PrinterName}");

                throw new InvalidOperationException(
                    "Ошибка GDI+ при печати листа " + (physicalIdx + 1) +
                    ". Код: 0x" + ex.ErrorCode.ToString("X") + ".", ex);
            }

            _currentGroupIndex++;
            e.HasMorePages = _currentGroupIndex < _activeSheetIndices.Length;
        }

        private void DrawSheetToPrinter(PrintPageEventArgs e, int sheetIndex)
        {
            var g = e.Graphics;

            int printerDpi = (int)Math.Round(g.DpiX);
            if (printerDpi < 72) printerDpi = 150;
            if (printerDpi > 200) printerDpi = 200;   // экономия памяти

            // Размер физической бумаги в 1/100".
            var ps = DefaultPageSettings.PaperSize;
            bool landscape = DefaultPageSettings.Landscape;
            int paperW = landscape ? Math.Max(ps.Width, ps.Height) : Math.Min(ps.Width, ps.Height);
            int paperH = landscape ? Math.Min(ps.Width, ps.Height) : Math.Max(ps.Width, ps.Height);

            // Реальные непечатаемые поля драйвера.
            // PageBounds.X/Y — это HardMarginX/Y (лево/верх).
            int hardLeft = e.PageBounds.X;
            int hardTop = e.PageBounds.Y;
            int hardRight = paperW - e.PageBounds.X - e.PageBounds.Width;
            int hardBottom = paperH - e.PageBounds.Y - e.PageBounds.Height;
            if (hardRight < 0) hardRight = 0;
            if (hardBottom < 0) hardBottom = 0;

            // Наши отступы (в 1/100").
            int wantLeft = (int)Math.Round(_marginLeftHundredths);
            int wantRight = (int)Math.Round(_marginRightHundredths);
            int wantTop = (int)Math.Round(_marginTopHundredths);
            int wantBottom = (int)Math.Round(_marginBottomHundredths);

            // Итоговый отступ = максимум из желаемого и аппаратного.
            // Если пользователь поставил меньше, чем может принтер — берём аппаратный.
            int effLeft = Math.Max(wantLeft, hardLeft);
            int effRight = Math.Max(wantRight, hardRight);
            int effTop = Math.Max(wantTop, hardTop);
            int effBottom = Math.Max(wantBottom, hardBottom);

            // Внутренний прямоугольник в координатах физической бумаги.
            int innerW = paperW - effLeft - effRight;
            int innerH = paperH - effTop - effBottom;
            if (innerW < 10) innerW = 10;
            if (innerH < 10) innerH = 10;

            // Позиция относительно PageBounds (там origin для printer Graphics в Display-единицах).
            int xRel = effLeft - hardLeft;
            int yRel = effTop - hardTop;
            if (xRel < 0) xRel = 0;
            if (yRel < 0) yRel = 0;

            var paperRect = new Rectangle(0, 0, paperW, paperH);
            var innerRect = new Rectangle(effLeft, effTop, innerW, innerH);

            // Битмап рендерим по размеру ВНУТРЕННЕГО прямоугольника.
            int pxW = Math.Max(1, (int)Math.Round(innerW / 100.0 * printerDpi));
            int pxH = Math.Max(1, (int)Math.Round(innerH / 100.0 * printerDpi));

            long bytes = (long)pxW * pxH * 3;
            const long maxBytes = 120L * 1024 * 1024;
            if (bytes > maxBytes)
            {
                double k = Math.Sqrt((double)maxBytes / bytes);
                pxW = Math.Max(1, (int)(pxW * k));
                pxH = Math.Max(1, (int)(pxH * k));
            }

            using (var sheetBmp = new Bitmap(pxW, pxH, PixelFormat.Format24bppRgb))
            {
                sheetBmp.SetResolution(printerDpi, printerDpi);

                using (var gb = Graphics.FromImage(sheetBmp))
                {
                    gb.Clear(Color.White);
                    // Внутри этого битмапа контент рисуется на весь прямоугольник,
                    // без дополнительных внутренних отступов.
                    DrawContentInto(gb, sheetIndex, new Rectangle(0, 0, innerW, innerH), _renderDpi);
                }

                var savedUnit = g.PageUnit;
                var savedScale = g.PageScale;

                try
                {
                    g.PageUnit = GraphicsUnit.Display;  // = 1/100"
                    g.PageScale = 1.0f;
                    g.InterpolationMode = InterpolationMode.Default;
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.Default;

                    // Blit в позицию внутри PageBounds (Display-единицы).
                    g.DrawImage(sheetBmp, new Rectangle(xRel, yRel, innerW, innerH));
                }
                finally
                {
                    g.PageUnit = savedUnit;
                    g.PageScale = savedScale;
                }
            }
        }

        // ====================================================================
        //  Отрисовка контента в заданный прямоугольник
        // ====================================================================
        private void DrawSheetContent(Graphics g, int sheetIndex, Rectangle paperBounds, int sourceDpi)
        {
            // Для предпросмотра: paperBounds — это весь Bitmap.
            // Отнимаем наши отступы с каждой стороны.
            float mgL = _marginLeftHundredths;
            float mgR = _marginRightHundredths;
            float mgT = _marginTopHundredths;
            float mgB = _marginBottomHundredths;

            float innerX = paperBounds.X + mgL;
            float innerY = paperBounds.Y + mgT;
            float innerW = paperBounds.Width - mgL - mgR;
            float innerH = paperBounds.Height - mgT - mgB;
            if (innerW < 10 || innerH < 10) return;

            var innerRect = new Rectangle(
                (int)Math.Round(innerX),
                (int)Math.Round(innerY),
                (int)Math.Round(innerW),
                (int)Math.Round(innerH));

            DrawContentInto(g, sheetIndex, innerRect, sourceDpi);
        }

        /// <summary>
        /// Рисует сетку ячеек и страницы в заданный прямоугольник (в 1/100").
        /// Этот метод общий для предпросмотра и печати.
        /// </summary>
        private void DrawContentInto(Graphics g, int sheetIndex, Rectangle contentRect, int sourceDpi)
        {
            g.PageUnit = GraphicsUnit.Inch;
            g.PageScale = 0.01f;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Ориентация листа определяется по contentRect.
            bool sheetLandscape = contentRect.Width > contentRect.Height;

            bool isBackSide = _duplex && (sheetIndex % 2 == 1);

            GraphicsState savedState = null;
            if (isBackSide && (BackOffsetXMm != 0 || BackOffsetYMm != 0))
            {
                savedState = g.Save();
                float dx = (float)(BackOffsetXMm * 100.0 / 25.4);
                float dy = (float)(BackOffsetYMm * 100.0 / 25.4);
                g.TranslateTransform(dx, dy);
            }

            try
            {
                if (DrawRegistrationMarks)
                    DrawRegistrationFrame(g,
                        contentRect.X, contentRect.Y,
                        contentRect.Width, contentRect.Height);

                var grid = GetGrid(_pagesPerSheet, sheetLandscape);
                int rows = grid.rows;
                int cols = grid.cols;

                List<int> pageIndices = BuildPageIndices(sheetIndex, rows, cols, sheetLandscape);

                float cellW = contentRect.Width / (float)cols;
                float cellH = contentRect.Height / (float)rows;

                for (int i = 0; i < pageIndices.Count; i++)
                {
                    int pageIdx = pageIndices[i];
                    if (pageIdx < 0 || pageIdx >= _source.PageCount) continue;

                    Image img = _source.GetPage(pageIdx, sourceDpi);
                    if (img == null) continue;

                    int row = i / cols;
                    int col = i % cols;
                    var cell = new RectangleF(
                        contentRect.X + col * cellW,
                        contentRect.Y + row * cellH,
                        cellW, cellH);

                    DrawPageFit(g, img, cell, _cellOrientation, _paddingHundredths);
                    DrawCellFrame(g, cell);
                }
            }
            finally
            {
                if (savedState != null) g.Restore(savedState);
            }
        }

        // ====================================================================
        //  Метки совмещения
        // ====================================================================
        private static void DrawRegistrationFrame(Graphics g, float x, float y, float w, float h)
        {
            using (var pen = new Pen(Color.LightGray, 1f))
            {
                g.DrawRectangle(pen, x, y, w, h);

                const float tick = 60f;
                g.DrawLine(pen, x, y, x + tick, y);
                g.DrawLine(pen, x, y, x, y + tick);
                g.DrawLine(pen, x + w, y, x + w - tick, y);
                g.DrawLine(pen, x + w, y, x + w, y + tick);
                g.DrawLine(pen, x, y + h, x + tick, y + h);
                g.DrawLine(pen, x, y + h, x, y + h - tick);
                g.DrawLine(pen, x + w, y + h, x + w - tick, y + h);
                g.DrawLine(pen, x + w, y + h, x + w, y + h - tick);

                float cx = x + w / 2f;
                float cy = y + h / 2f;
                const float cross = 80f;
                g.DrawLine(pen, cx - cross, cy, cx + cross, cy);
                g.DrawLine(pen, cx, cy - cross, cx, cy + cross);
            }
        }

        // ====================================================================
        //  Раскладка страниц
        // ====================================================================
        private List<int> BuildPageIndices(int sheetIndex, int rows, int cols, bool sheetLandscape)
        {
            int n = _pagesPerSheet;
            var result = new List<int>(n);

            if (!_duplex)
            {
                int start = sheetIndex * n;
                for (int i = 0; i < n; i++)
                {
                    int page = start + i;
                    result.Add(IsPageSelected(page) ? page : -1);
                }
                return result;
            }

            bool isBack = (sheetIndex % 2) == 1;
            int pairIndex = sheetIndex / 2;
            int p0 = pairIndex * 2 * n;

            if (!isBack)
            {
                for (int i = 0; i < n; i++)
                {
                    int page = p0 + 2 * i;
                    result.Add(IsPageSelected(page) ? page : -1);
                }
                return result;
            }

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
                result.Add(IsPageSelected(page) ? page : -1);
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
                case 2: shortDiv = 1; longDiv = 2; break;
                case 3: shortDiv = 1; longDiv = 3; break;
                case 4: shortDiv = 2; longDiv = 2; break;
                case 6: shortDiv = 2; longDiv = 3; break;
                case 8: shortDiv = 2; longDiv = 4; break;
                case 9: shortDiv = 3; longDiv = 3; break;
                default:
                    throw new ArgumentException(
                        "Поддерживаются только 2, 3, 4, 6, 8 или 9 страниц на листе.", nameof(n));
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