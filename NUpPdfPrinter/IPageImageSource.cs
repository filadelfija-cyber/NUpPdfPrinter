using System;
using System.Drawing;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Источник изображений страниц. Позволяет рендерить страницы по требованию,
    /// а не держать все в памяти одновременно.
    /// </summary>
    public interface IPageImageSource : IDisposable
    {
        int PageCount { get; }
        Image RenderPage(int index, int dpi);
    }
}