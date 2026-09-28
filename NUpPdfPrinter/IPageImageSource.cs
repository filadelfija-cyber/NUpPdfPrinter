using System;
using System.Drawing;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Источник изображений страниц. Вызывающий код НЕ должен Dispose'ить
    /// возвращённые изображения — их временем жизни управляет источник.
    /// </summary>
    public interface IPageImageSource : IDisposable
    {
        int PageCount { get; }

        /// <summary>Занято кэшем, в байтах.</summary>
        long CacheBytes { get; }

        /// <summary>Возвращает изображение страницы. Не Dispose'ить!</summary>
        Image GetPage(int index, int dpi);

        /// <summary>Полностью очищает кэш.</summary>
        void ClearCache();
    }
}