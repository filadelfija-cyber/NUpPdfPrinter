using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Fonts;

namespace PdfTestGenerator
{
    /// <summary>
    /// Font resolver для Core-сборки PDFsharp: находит TTF в системной папке Windows.
    /// Для Windows-only приложений достаточно; на Linux/Mac потребуется другой путь.
    /// </summary>
    public sealed class WindowsFontResolver : IFontResolver
    {
        // Соответствие: имя семейства → имя файла шрифта
        private static readonly Dictionary<string, string> RegularMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arial"] = "arial.ttf",
                ["Times New Roman"] = "times.ttf",
                ["Courier New"] = "cour.ttf",
                ["Verdana"] = "verdana.ttf",
                ["Calibri"] = "calibri.ttf",
                ["Segoe UI"] = "segoeui.ttf",
                ["Tahoma"] = "tahoma.ttf",
            };

        private static readonly Dictionary<string, string> BoldMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arial"] = "arialbd.ttf",
                ["Times New Roman"] = "timesbd.ttf",
                ["Courier New"] = "courbd.ttf",
                ["Verdana"] = "verdanab.ttf",
                ["Calibri"] = "calibrib.ttf",
                ["Segoe UI"] = "segoeuib.ttf",
                ["Tahoma"] = "tahomabd.ttf",
            };

        private static readonly Dictionary<string, string> ItalicMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arial"] = "ariali.ttf",
                ["Times New Roman"] = "timesi.ttf",
                ["Courier New"] = "couri.ttf",
                ["Verdana"] = "verdanai.ttf",
                ["Calibri"] = "calibrii.ttf",
                ["Segoe UI"] = "segoeuii.ttf",
                ["Tahoma"] = "tahoma.ttf",  // симулируется
            };

        private static readonly Dictionary<string, string> BoldItalicMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arial"] = "arialbi.ttf",
                ["Times New Roman"] = "timesbi.ttf",
                ["Courier New"] = "courbi.ttf",
                ["Verdana"] = "verdanaz.ttf",
                ["Calibri"] = "calibriz.ttf",
                ["Segoe UI"] = "segoeuiz.ttf",
                ["Tahoma"] = "tahomabd.ttf", // симулируется
            };

        private readonly string _fontsFolder;
        private readonly Dictionary<string, byte[]> _fontCache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public WindowsFontResolver()
        {
            // Папка системных шрифтов. Можно переопределить на пользовательскую.
            _fontsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Fonts");
        }

        /// <summary>
        /// Возвращает информацию о физическом шрифте для запрошенного семейства.
        /// </summary>
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        {
            string fileName;
            var map = (bold, italic) switch
            {
                (true, true) => BoldItalicMap,
                (true, false) => BoldMap,
                (false, true) => ItalicMap,
                _ => RegularMap,
            };

            if (!map.TryGetValue(familyName, out fileName))
            {
                // Fallback на Arial, чтобы не падать на неизвестных именах.
                map = RegularMap;
                if (!map.TryGetValue("Arial", out fileName))
                    return null;
            }

            // Проверяем, что файл существует.
            string fullPath = Path.Combine(_fontsFolder, fileName);
            if (!File.Exists(fullPath))
            {
                // Пробуем обычный Arial как последний шанс.
                fullPath = Path.Combine(_fontsFolder, "arial.ttf");
                if (!File.Exists(fullPath)) return null;
                fileName = "arial.ttf";
            }

            // faceName — произвольная строка; PDFsharp передаст её в GetFont.
            return new FontResolverInfo(fileName);
        }

        /// <summary>
        /// Отдаёт байты шрифта по имени, полученному из ResolveTypeface.
        /// </summary>
        public byte[] GetFont(string faceName)
        {
            if (_fontCache.TryGetValue(faceName, out var cached))
                return cached;

            string fullPath = Path.Combine(_fontsFolder, faceName);
            if (!File.Exists(fullPath))
                throw new InvalidOperationException(
                    "Файл шрифта не найден: " + fullPath);

            byte[] data = File.ReadAllBytes(fullPath);
            _fontCache[faceName] = data;
            return data;
        }
    }
}