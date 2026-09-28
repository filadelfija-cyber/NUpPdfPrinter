using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumViewer;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Рендерит страницы PDF по требованию из уже открытого PdfDocument.
    /// Не хранит изображения — вызывающий код сам решает, когда Dispose.
    /// </summary>
    public sealed class PdfPageImageSource : IPageImageSource
    {
        private readonly PdfDocument _doc;
        private bool _disposed;

        public PdfPageImageSource(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PDF-файл не найден.", filePath);

            // Pdfium держит файл через mmap — heap не растёт.
            _doc = PdfDocument.Load(filePath);
        }

        public int PageCount => _doc.PageCount;

        public Image RenderPage(int index, int dpi)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PdfPageImageSource));
            if (index < 0 || index >= _doc.PageCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var size = _doc.PageSizes[index]; // в точках (1/72")
            int width = Math.Max(1, (int)Math.Round(size.Width / 72.0 * dpi));
            int height = Math.Max(1, (int)Math.Round(size.Height / 72.0 * dpi));

            // Рендерим в 24bpp — этого достаточно для печати, память −25%.
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _doc?.Dispose();
        }
    }
}