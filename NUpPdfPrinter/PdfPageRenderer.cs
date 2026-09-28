using System;
using System.Collections.Generic;
using System.Drawing;
using PdfiumViewer;

namespace NUpPdfPrinter
{
    public static class PdfPageRenderer
    {
        /// <summary>
        /// Рендерит все страницы PDF в список изображений.
        /// </summary>
        /// <param name="filePath">Путь к PDF</param>
        /// <param name="dpi">Разрешение рендеринга (150–200 достаточно для печати)</param>
        public static List<Image> RenderPages(string filePath, int dpi = 150)
        {
            var images = new List<Image>();
            using var doc = PdfDocument.Load(filePath);

            for (int i = 0; i < doc.PageCount; i++)
            {
                var size = doc.PageSizes[i];
                int width = (int)Math.Round(size.Width / 72.0 * dpi);
                int height = (int)Math.Round(size.Height / 72.0 * dpi);

                var img = doc.Render(
                    i,
                    width,
                    height,
                    dpi,
                    dpi,
                    PdfRenderFlags.ForPrinting | PdfRenderFlags.Annotations);

                images.Add(img);
            }
            return images;
        }
    }
}