using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System;
using System.IO;

namespace PdfTestGenerator
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            GlobalFontSettings.FontResolver = new WindowsFontResolver();

            // Параметры по умолчанию
            int pageCount = 32;
            string output = "test-32-pages.pdf";
            string fontName = "Arial";
            double fontSize = 32.0;
            string orientStr = "portrait";

            // Простой разбор аргументов: [output.pdf] [count] [fontSize]
            if (args.Length >= 1) output = args[0];
            if (args.Length >= 2 && int.TryParse(args[1], out var n) && n > 0)
                pageCount = n;
            if (args.Length >= 3 && double.TryParse(args[2],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fs) && fs > 0)
                fontSize = fs;

            try
            {
                Generate(output, pageCount, fontName, fontSize, orientStr);
                Console.WriteLine($"Создан файл: {Path.GetFullPath(output)}");
                Console.WriteLine($"Страниц: {pageCount}, шрифт: {fontName} {fontSize}pt");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ошибка: " + ex.Message);
                return 1;
            }
        }

        private static void Generate(
            string outputPath, int pageCount, string fontName, double fontSize, string orientStr)
        {
            // Шрифт с поддержкой кириллицы, если потребуется.
            var font = new XFont(fontName, fontSize, XFontStyleEx.Regular);

            using (var doc = new PdfDocument())
            {
                doc.Info.Title = $"Test document — {pageCount} pages";
                doc.Info.Author = "NUpPdfPrinter test generator";
                doc.Info.Subject = "Page numbers centered";
                doc.Info.Creator = "PdfSharp";

                for (int i = 1; i <= pageCount; i++)
                {
                    var page = doc.AddPage();
                    page.Size = PageSize.A4;
                    page.Orientation = PageOrientation.Portrait;

                    using (var gfx = XGraphics.FromPdfPage(page))
                    {
                        string text = i.ToString();

                        // Прямоугольник во всю страницу, выравнивание по центру.
                        var rect = new XRect(0, 0, page.Width.Point, page.Height.Point);

                        gfx.DrawString(
                            text,
                            font,
                            XBrushes.Black,
                            rect,
                            XStringFormats.Center);
                    }
                }

                // Гарантируем корректную запись независимо от того, есть ли каталог.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                doc.Save(outputPath);
            }
        }
    }
}