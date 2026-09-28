using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;

namespace NUpPdfPrinter
{
    public class NUpPrintDocument : PrintDocument
    {
        private readonly List<Image> _pageImages;
        private readonly int _pagesPerSheet;
        private readonly int _rows;
        private readonly int _cols;
        private readonly bool _duplex;
        private readonly bool _longEdge;
        private int _currentGroupIndex;

        public NUpPrintDocument(List<Image> pageImages, int pagesPerSheet, bool duplex, bool longEdge)
        {
            _pageImages = new List<Image>(pageImages);
            _pagesPerSheet = pagesPerSheet;
            _duplex = duplex;
            _longEdge = longEdge;
            (_rows, _cols) = GetGrid(pagesPerSheet);

            DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169); // A4 в 1/100 дюйма
            DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);
            DefaultPageSettings.Landscape = false;

            PrinterSettings.Duplex = duplex
                ? (longEdge ? Duplex.Vertical : Duplex.Horizontal)
                : Duplex.Simplex;

            // Дополняем пустыми страницами до кратности блока
            int total = _pageImages.Count;
            int blockSize = duplex ? 2 * pagesPerSheet : pagesPerSheet;
            int needed = ((total + blockSize - 1) / blockSize) * blockSize;
            for (int i = total; i < needed; i++)
                _pageImages.Add(null!);
        }

        private static (int rows, int cols) GetGrid(int n) => n switch
        {
            4 => (2, 2),
            6 => (2, 3),
            8 => (2, 4),
            9 => (3, 3),
            _ => throw new ArgumentException("Поддерживаются только 4, 6, 8, 9 страниц на листе.")
        };

        protected override void OnBeginPrint(PrintEventArgs e)
        {
            base.OnBeginPrint(e);
            _currentGroupIndex = 0;
        }

        protected override void OnPrintPage(PrintPageEventArgs e)
        {
            base.OnPrintPage(e);
            e.Graphics.PageUnit = GraphicsUnit.Point;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            // На листе с дуплексом: чётные группы — лицевая сторона, нечётные — обратная
            bool isBackSide = _duplex && (_currentGroupIndex % 2 == 1);

            int startPage = _currentGroupIndex * _pagesPerSheet;
            var pageIndices = new List<int>(_pagesPerSheet);
            for (int i = 0; i < _pagesPerSheet; i++)
            {
                int idx = startPage + i;
                pageIndices.Add(idx < _pageImages.Count ? idx : -1);
            }

            if (isBackSide)
                pageIndices = ReorderForDuplex(pageIndices, _longEdge);

            float cellWidth  = e.PageBounds.Width  / (float)_cols;
            float cellHeight = e.PageBounds.Height / (float)_rows;

            for (int i = 0; i < pageIndices.Count; i++)
            {
                int pageIdx = pageIndices[i];
                if (pageIdx < 0 || pageIdx >= _pageImages.Count || _pageImages[pageIdx] == null)
                    continue;

                int row = i / _cols;
                int col = i % _cols;

                var destRect = new RectangleF(
                    col * cellWidth,
                    row * cellHeight,
                    cellWidth,
                    cellHeight);

                DrawPageFit(e.Graphics, _pageImages[pageIdx], destRect);
            }

            _currentGroupIndex++;
            e.HasMorePages = _currentGroupIndex * _pagesPerSheet < _pageImages.Count;
        }

        /// <summary>
        /// Вписывает изображение страницы в ячейку с сохранением пропорций и по центру.
        /// </summary>
        private static void DrawPageFit(Graphics g, Image img, RectangleF cell)
        {
            float imgW = img.Width;
            float imgH = img.Height;
            float scale = Math.Min(cell.Width / imgW, cell.Height / imgH);
            float drawW = imgW * scale;
            float drawH = imgH * scale;
            float x = cell.X + (cell.Width  - drawW) / 2f;
            float y = cell.Y + (cell.Height - drawH) / 2f;

            g.DrawImage(img, x, y, drawW, drawH);
        }

        /// <summary>
        /// Переупорядочивает страницы на обратной стороне,
        /// чтобы после переворота листа они шли в правильном порядке.
        /// </summary>
        private List<int> ReorderForDuplex(List<int> indices, bool longEdge)
        {
            var result = new List<int>(indices);

            if (longEdge) // переворот по длинному краю → зеркалим столбцы
            {
                for (int r = 0; r < _rows; r++)
                {
                    for (int c = 0; c < _cols / 2; c++)
                    {
                        int a = r * _cols + c;
                        int b = r * _cols + (_cols - 1 - c);
                        (result[a], result[b]) = (result[b], result[a]);
                    }
                }
            }
            else // переворот по короткому краю → зеркалим строки
            {
                for (int r = 0; r < _rows / 2; r++)
                {
                    for (int c = 0; c < _cols; c++)
                    {
                        int a = r * _cols + c;
                        int b = (_rows - 1 - r) * _cols + c;
                        (result[a], result[b]) = (result[b], result[a]);
                    }
                }
            }
            return result;
        }
    }
}