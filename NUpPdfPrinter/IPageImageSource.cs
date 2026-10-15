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
        long CacheBytes { get; }

        /// <summary>Возвращает изображение страницы. Не Dispose'ить!</summary>
        Image GetPage(int index, int dpi);

        /// <summary>Полностью очищает кэш страниц.</summary>
        void ClearCache();

        /// <summary>
        /// Временно отключает вытеснение из кэша. Вызывать перед параллельным
        /// батчем GetPage и освобождать (Dispose) после батча.
        /// </summary>
        IDisposable SuppressEviction();

        /// <summary>
        /// Немедленно освобождает все нативные ресурсы: закрывает PdfDocument
        /// (если открыт), очищает кэш страниц и кэш размеров. Следующий
        /// GetPage автоматически переоткроет файл.
        /// Использовать после закрытия предпросмотра/печати для возврата
        /// памяти ОС, не дожидаясь idle-таймаута.
        /// </summary>
        void ReleaseNativeResources();
    }
}