using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    public sealed class MainForm : Form
    {
        private IPageImageSource _source;      // владеем на протяжении работы с PDF
        private string _pdfPath;
        private int _pageCount;

        // --- UI ---
        private Label _lblFile;
        private Button _btnOpen;
        private Label _lblDpi;
        private NumericUpDown _numDpi;

        private Label _lblPagesPerSheet;
        private ComboBox _cmbPagesPerSheet;

        private Label _lblOrientation;
        private ComboBox _cmbOrientation;         // формат листа: книжный / альбомный

        private CheckBox _chkDuplex;
        private Label _lblEdge;
        private ComboBox _cmbDuplexEdge;

        private Label _lblCellOrientation;
        private ComboBox _cmbCellOrientation;     // поворот страниц PDF внутри ячейки

        private Label _lblPadding;
        private NumericUpDown _numPadding;
        private Label _lblPaddingMm;

        private Label _lblPageMargin;
        private NumericUpDown _numPageMargin;
        private Label _lblPageMarginMm;

        private Label _lblCache;
        private ComboBox _cmbCacheBudget;
        private Button _btnClearCache;

        private Label _lblPreview;

        private Button _btnPreview;
        private Button _btnExit;
        private Button _btnPrint;

        public MainForm()
        {
            InitializeUi();
        }

        // ====================================================================
        //  UI
        // ====================================================================
        private void InitializeUi()
        {
            Text = "N-up печать PDF (4 / 6 / 8 / 9 на листе A4)";
            ClientSize = new Size(580, 570);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            // ---------- строка файла ----------
            // Фиксированная ширина + AutoEllipsis — обрезает «…» при переполнении.
            _lblFile = new Label
            {
                Text = "Файл не выбран",
                Location = new Point(12, 12),
                Size = new Size(556, 20),
                AutoSize = false,
                AutoEllipsis = true
            };

            _btnOpen = new Button
            {
                Text = "Открыть PDF…",
                Location = new Point(12, 40),
                Size = new Size(130, 30)
            };
            _btnOpen.Click += BtnOpen_Click;

            _lblDpi = new Label
            {
                Text = "DPI рендеринга:",
                Location = new Point(160, 45),
                AutoSize = true
            };

            _numDpi = new NumericUpDown
            {
                Location = new Point(280, 43),
                Size = new Size(80, 24),
                Minimum = 72,
                Maximum = 400,
                Value = 150,
                Increment = 50
            };

            // ---------- страниц на листе ----------
            _lblPagesPerSheet = new Label
            {
                Text = "Страниц на листе:",
                Location = new Point(12, 90),
                AutoSize = true
            };

            _cmbPagesPerSheet = new ComboBox
            {
                Location = new Point(150, 87),
                Size = new Size(100, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbPagesPerSheet.Items.AddRange(new object[] { 4, 6, 8, 9 });
            _cmbPagesPerSheet.SelectedIndex = 0;
            _cmbPagesPerSheet.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            // ---------- формат листа ----------
            _lblOrientation = new Label
            {
                Text = "Формат листа:",
                Location = new Point(265, 90),
                AutoSize = true
            };

            _cmbOrientation = new ComboBox
            {
                Location = new Point(380, 87),
                Size = new Size(188, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbOrientation.Items.AddRange(new object[]
            {
                "Книжный (портрет)",
                "Альбомный (ландшафт)"
            });
            _cmbOrientation.SelectedIndex = 0;
            _cmbOrientation.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            var tipOrientation = new ToolTip();
            tipOrientation.SetToolTip(_cmbOrientation,
                "Как ориентирован физический лист A4 при печати.\r\n" +
                "Книжный — вертикально (портрет).\r\n" +
                "Альбомный — горизонтально (ландшафт).");

            // ---------- дуплекс ----------
            _chkDuplex = new CheckBox
            {
                Text = "Двусторонняя печать",
                Location = new Point(12, 125),
                Size = new Size(220, 24),
                Checked = true
            };
            _chkDuplex.CheckedChanged += (s, e) =>
            {
                _cmbDuplexEdge.Enabled = _chkDuplex.Checked;
                UpdatePreviewText();
            };

            _lblEdge = new Label
            {
                Text = "Переворот листа:",
                Location = new Point(12, 160),
                AutoSize = true
            };

            _cmbDuplexEdge = new ComboBox
            {
                Location = new Point(150, 157),
                Size = new Size(230, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDuplexEdge.Items.AddRange(new object[]
            {
                "По длинному краю (стандарт)",
                "По короткому краю"
            });
            _cmbDuplexEdge.SelectedIndex = 0;
            _cmbDuplexEdge.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            // ---------- поворот страниц PDF ----------
            _lblCellOrientation = new Label
            {
                Text = "Поворот страниц PDF:",
                Location = new Point(12, 195),
                AutoSize = true
            };

            _cmbCellOrientation = new ComboBox
            {
                Location = new Point(170, 192),
                Size = new Size(230, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCellOrientation.Items.AddRange(new object[]
            {
                "Авто (по форме ячейки)",
                "Вертикально (книжная)",
                "Горизонтально (альбомная)"
            });
            _cmbCellOrientation.SelectedIndex = 0;
            _cmbCellOrientation.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            var tipCellOrientation = new ToolTip();
            tipCellOrientation.SetToolTip(_cmbCellOrientation,
                "Как исходная страница PDF ориентирована внутри своей ячейки.\r\n" +
                "Авто — подбирается по форме ячейки (обычно оптимально).\r\n" +
                "Вертикально — страница всегда книжная.\r\n" +
                "Горизонтально — страница всегда альбомная.");

            // ---------- зазор между страницами ----------
            _lblPadding = new Label
            {
                Text = "Зазор между страницами:",
                Location = new Point(12, 230),
                AutoSize = true
            };

            _numPadding = new NumericUpDown
            {
                Location = new Point(180, 228),
                Size = new Size(70, 24),
                Minimum = 0m,
                Maximum = 20m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 2.5m
            };
            _numPadding.ValueChanged += (s, e) => UpdatePreviewText();

            _lblPaddingMm = new Label
            {
                Text = "мм (0…20, шаг 0,5)",
                Location = new Point(258, 231),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipPadding = new ToolTip();
            tipPadding.SetToolTip(_numPadding,
                "Отступ вокруг каждой страницы внутри её ячейки.\r\n" +
                "Реальное расстояние между соседними страницами = 2 × значение.");

            // ---------- отступ от края листа A4 ----------
            _lblPageMargin = new Label
            {
                Text = "Отступ от края листа:",
                Location = new Point(12, 262),
                AutoSize = true
            };

            _numPageMargin = new NumericUpDown
            {
                Location = new Point(180, 260),
                Size = new Size(70, 24),
                Minimum = 0m,
                Maximum = 30m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 6.5m          // ≈ 0.25″ — типовая непечатаемая зона
            };
            _numPageMargin.ValueChanged += (s, e) => UpdatePreviewText();

            _lblPageMarginMm = new Label
            {
                Text = "мм (0…30, шаг 0,5)",
                Location = new Point(258, 263),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipPageMargin = new ToolTip();
            tipPageMargin.SetToolTip(_numPageMargin,
                "Поля листа A4 со всех сторон — как в Word.\r\n" +
                "Сужает рабочую область для сетки страниц.\r\n" +
                "Для лазерных принтеров безопасно 6–8 мм;\r\n" +
                "для струйных без полей можно 0, но край может обрезаться.");

            // ---------- кэш ----------
            _lblCache = new Label
            {
                Text = "Кэш страниц:",
                Location = new Point(12, 295),
                AutoSize = true
            };

            _cmbCacheBudget = new ComboBox
            {
                Location = new Point(130, 292),
                Size = new Size(200, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCacheBudget.Items.AddRange(new object[]
            {
                "Без кэша (мин. память)",
                "16 МБ",
                "32 МБ",
                "48 МБ (по умолчанию)",
                "96 МБ",
                "192 МБ"
            });
            _cmbCacheBudget.SelectedIndex = 3;
            _cmbCacheBudget.SelectedIndexChanged += (s, e) =>
            {
                if (_source != null) ReopenSourceWithCurrentBudget();
            };

            _btnClearCache = new Button
            {
                Text = "Очистить кэш",
                Location = new Point(340, 291),
                Size = new Size(120, 28),
                Enabled = false
            };
            _btnClearCache.Click += BtnClearCache_Click;

            // ---------- инфо-метка ----------
            // Многострочный текст, ширина фиксированная.
            _lblPreview = new Label
            {
                Location = new Point(12, 330),
                Size = new Size(556, 140),
                AutoSize = false,
                ForeColor = Color.DimGray
            };

            // ---------- кнопки ----------
            _btnPreview = new Button
            {
                Text = "Предпросмотр…",
                Location = new Point(12, 490),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPreview.Click += BtnPreview_Click;

            _btnExit = new Button
            {
                Text = "Выход",
                Location = new Point(220, 490),
                Size = new Size(140, 34)
            };
            _btnExit.Click += (s, e) => Close();

            _btnPrint = new Button
            {
                Text = "Печать…",
                Location = new Point(428, 490),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPrint.Click += BtnPrint_Click;

            Controls.AddRange(new Control[]
            {
                _lblFile, _btnOpen, _lblDpi, _numDpi,
                _lblPagesPerSheet, _cmbPagesPerSheet,
                _lblOrientation, _cmbOrientation,
                _chkDuplex, _lblEdge, _cmbDuplexEdge,
                _lblCellOrientation, _cmbCellOrientation,
                _lblPadding, _numPadding, _lblPaddingMm,
                _lblPageMargin, _numPageMargin, _lblPageMarginMm,
                _lblCache, _cmbCacheBudget, _btnClearCache,
                _lblPreview,
                _btnPreview, _btnExit, _btnPrint
            });
        }

        // ====================================================================
        //  Файл
        // ====================================================================
        private void BtnOpen_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "PDF-файлы (*.pdf)|*.pdf|Все файлы (*.*)|*.*",
                Title = "Выберите PDF-документ"
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                _pdfPath = ofd.FileName;
            }

            DisposeSource();

            try
            {
                _source = new PdfPageImageSource(_pdfPath, GetCacheBudgetBytes());
                _pageCount = _source.PageCount;

                _lblFile.Text = Path.GetFileName(_pdfPath) + " — " + _pageCount + " стр.";
                _btnPrint.Enabled = true;
                _btnPreview.Enabled = true;
                _btnClearCache.Enabled = true;
                UpdatePreviewText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Не удалось открыть PDF:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

                _lblFile.Text = "Файл не выбран";
                _lblPreview.Text = string.Empty;
                _btnPrint.Enabled = false;
                _btnPreview.Enabled = false;
                _btnClearCache.Enabled = false;
            }
        }

        private void ReopenSourceWithCurrentBudget()
        {
            if (string.IsNullOrEmpty(_pdfPath)) return;

            DisposeSource();
            try
            {
                _source = new PdfPageImageSource(_pdfPath, GetCacheBudgetBytes());
                _pageCount = _source.PageCount;
                _btnClearCache.Enabled = true;
                UpdatePreviewText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Не удалось переоткрыть PDF:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private long GetCacheBudgetBytes()
        {
            switch (_cmbCacheBudget.SelectedIndex)
            {
                case 0: return 0L;                              // без кэша
                case 1: return 16L * 1024 * 1024;
                case 2: return 32L * 1024 * 1024;
                case 3: return 48L * 1024 * 1024;
                case 4: return 96L * 1024 * 1024;
                case 5: return 192L * 1024 * 1024;
                default: return 48L * 1024 * 1024;
            }
        }

        private void BtnClearCache_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            _source.ClearCache();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            UpdatePreviewText();
        }

        // ====================================================================
        //  Инфо-строка
        // ====================================================================
        private void UpdatePreviewText()
        {
            if (_source == null || _pageCount == 0)
            {
                _lblPreview.Text = string.Empty;
                return;
            }

            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool dup = _chkDuplex.Checked;
            int block = dup ? 2 * n : n;
            int sheets = (_pageCount + block - 1) / block;
            int padded = sheets * block;

            string sheetFormat = _cmbOrientation.SelectedIndex == 1
                ? "A4 альбомный" : "A4 книжный";

            string cellMode;
            switch (_cmbCellOrientation.SelectedIndex)
            {
                case 1: cellMode = "вертикально (книжная)"; break;
                case 2: cellMode = "горизонтально (альбомная)"; break;
                default: cellMode = "авто (по форме ячейки)"; break;
            }

            string edge = _cmbDuplexEdge.SelectedIndex == 0
                ? "длинный край" : "короткий край";

            double padMm = (double)_numPadding.Value;
            double betweenMm = padMm * 2.0;
            double marginMm = (double)_numPageMargin.Value;

            _lblPreview.Text =
                "Страниц в PDF: " + _pageCount + "\r\n" +
                "Листов: " + sheets +
                " (логических страниц после дополнения: " + padded + ")\r\n" +
                "Формат листа: " + sheetFormat +
                ";  страницы PDF на листе: " + cellMode + "\r\n" +
                (dup
                    ? "Дуплекс: включён, переворот по " + edge
                    : "Дуплекс: выключен") + "\r\n" +
                "Поля листа: " + marginMm.ToString("0.0") + " мм со всех сторон\r\n" +
                "Зазор между страницами: " + betweenMm.ToString("0.0") + " мм " +
                "(отступ внутри ячейки " + padMm.ToString("0.0") + " мм × 2)\r\n" +
                "Кэш страниц: " + (_source.CacheBytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
        }

        // ====================================================================
        //  Параметры → документ
        // ====================================================================
        private CellContentOrientation GetCellOrientation()
        {
            switch (_cmbCellOrientation.SelectedIndex)
            {
                case 1: return CellContentOrientation.Portrait;   // вертикально
                case 2: return CellContentOrientation.Landscape;  // горизонтально
                default: return CellContentOrientation.Auto;
            }
        }

        private double GetPaddingMm() => (double)_numPadding.Value;
        private double GetPageMarginMm() => (double)_numPageMargin.Value;

        private bool GetSheetLandscape() => _cmbOrientation.SelectedIndex == 1;

        private NUpPrintDocument CreateDocument(bool duplex, bool longEdge)
        {
            int n = (int)_cmbPagesPerSheet.SelectedItem;
            int dpi = (int)_numDpi.Value;

            var doc = new NUpPrintDocument(
                _source,
                n,
                duplex,
                longEdge,
                dpi,
                GetCellOrientation(),
                GetPaddingMm(),
                GetPageMarginMm());

            // Ориентация листа идёт и в предпросмотр, и в печать.
            doc.Landscape = GetSheetLandscape();
            return doc;
        }

        // ====================================================================
        //  Предпросмотр
        // ====================================================================
        private void BtnPreview_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            // Превью рендерится в 96 DPI — этого достаточно для экрана.
            const int previewDpi = 96;

            using (var doc = CreateDocument(
                       _chkDuplex.Checked,
                       _cmbDuplexEdge.SelectedIndex == 0))
            using (var dlg = new PreviewForm(doc, _source, previewDpi, previewDpi))
            {
                dlg.ShowDialog(this);
            }

            UpdatePreviewText();
        }

        // ====================================================================
        //  Печать
        // ====================================================================
        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool userWantsDuplex = _chkDuplex.Checked;
            bool userWantsLongEdge = _cmbDuplexEdge.SelectedIndex == 0;
            var orient = GetCellOrientation();
            int dpi = (int)_numDpi.Value;
            double padMm = GetPaddingMm();
            double pageMarginMm = GetPageMarginMm();
            bool sheetLandscape = GetSheetLandscape();

            using (var doc = new NUpPrintDocument(
                       _source, n, userWantsDuplex, userWantsLongEdge,
                       dpi, orient, padMm, pageMarginMm))
            {
                doc.Landscape = sheetLandscape;

                using (var dlg = new PrintDialog
                {
                    Document = doc,
                    UseEXDialog = true,
                    AllowSomePages = false,
                    AllowSelection = false,
                    AllowPrintToFile = false
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    try
                    {
                        doc.Print();

                        MessageBox.Show(this,
                            "Документ отправлен на печать.",
                            "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (System.Runtime.InteropServices.ExternalException ex)
                    {
                        MessageBox.Show(this,
                            "Ошибка GDI+ при печати (код 0x" + ex.ErrorCode.ToString("X") + ").\r\n" +
                            "Наиболее вероятные причины:\r\n" +
                            " • мало свободного места на диске C:\\ (спулер печати);\r\n" +
                            " • проблема с драйвером принтера;\r\n" +
                            " • принтер офлайн или недоступен.\r\n\r\n" +
                            "Освободите место на диске и повторите печать.",
                            "Ошибка печати",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            "Ошибка печати:\r\n" + ex.Message,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            UpdatePreviewText();
        }

        // ====================================================================
        //  Время жизни источника
        // ====================================================================
        private void DisposeSource()
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            _pageCount = 0;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisposeSource();
            base.OnFormClosing(e);
        }
    }
}