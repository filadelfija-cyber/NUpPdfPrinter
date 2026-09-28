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
        private NumericUpDown _numDpi;

        private ComboBox _cmbPagesPerSheet;

        private CheckBox _chkDuplex;
        private ComboBox _cmbDuplexEdge;

        private ComboBox _cmbCellOrientation;
        private NumericUpDown _numPadding;

        private ComboBox _cmbCacheBudget;
        private Button _btnClearCache;

        private Label _lblPreview;

        private Button _btnPreview;
        private Button _btnPrint;
        private Button _btnExit;

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
            ClientSize = new Size(580, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            // ---------- строка файла ----------
            _lblFile = new Label
            {
                Text = "Файл не выбран",
                Location = new Point(12, 12),
                Size = new Size(556, 20),
                AutoEllipsis = true
            };

            _btnOpen = new Button
            {
                Text = "Открыть PDF…",
                Location = new Point(12, 40),
                Size = new Size(130, 30)
            };
            _btnOpen.Click += BtnOpen_Click;

            var lblDpi = new Label
            {
                Text = "DPI рендеринга:",
                Location = new Point(160, 45),
                Size = new Size(110, 22)
            };

            _numDpi = new NumericUpDown
            {
                Location = new Point(275, 43),
                Size = new Size(80, 24),
                Minimum = 72,
                Maximum = 400,
                Value = 150,
                Increment = 50
            };

            // ---------- страниц на листе ----------
            var lblN = new Label
            {
                Text = "Страниц на листе:",
                Location = new Point(12, 90),
                Size = new Size(130, 22)
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

            var lblEdge = new Label
            {
                Text = "Переворот листа:",
                Location = new Point(12, 160),
                Size = new Size(130, 22)
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

            // ---------- ориентация страниц в ячейках ----------
            var lblCellOrientation = new Label
            {
                Text = "Ориентация страниц:",
                Location = new Point(12, 195),
                Size = new Size(130, 22)
            };

            _cmbCellOrientation = new ComboBox
            {
                Location = new Point(150, 192),
                Size = new Size(230, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbCellOrientation.Items.AddRange(new object[]
            {
                "Авто (по ячейке)",
                "Книжная (портрет)",
                "Альбомная (ландшафт)"
            });
            _cmbCellOrientation.SelectedIndex = 0;
            _cmbCellOrientation.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            // ---------- отступ ----------
            var lblPadding = new Label
            {
                Text = "Отступ вокруг страницы:",
                Location = new Point(12, 230),
                Size = new Size(150, 22)
            };

            _numPadding = new NumericUpDown
            {
                Location = new Point(170, 228),
                Size = new Size(70, 24),
                Minimum = 0m,
                Maximum = 20m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 2.5m
            };
            _numPadding.ValueChanged += (s, e) => UpdatePreviewText();

            var lblPaddingMm = new Label
            {
                Text = "мм (0…20, шаг 0,5)",
                Location = new Point(245, 231),
                Size = new Size(160, 22),
                ForeColor = Color.DimGray
            };

            // ---------- кэш ----------
            var lblCache = new Label
            {
                Text = "Кэш страниц:",
                Location = new Point(12, 265),
                Size = new Size(110, 22)
            };

            _cmbCacheBudget = new ComboBox
            {
                Location = new Point(130, 262),
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
                Location = new Point(340, 261),
                Size = new Size(120, 28),
                Enabled = false
            };
            _btnClearCache.Click += BtnClearCache_Click;

            // ---------- инфо-метка ----------
            _lblPreview = new Label
            {
                Location = new Point(12, 300),
                Size = new Size(556, 100),
                ForeColor = Color.DimGray
            };

            // ---------- кнопки ----------
            _btnPreview = new Button
            {
                Text = "Предпросмотр…",
                Location = new Point(12, 435),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPreview.Click += BtnPreview_Click;

            _btnExit = new Button
            {
                Text = "Выход",
                Location = new Point(220, 435),
                Size = new Size(140, 34)
            };
            _btnExit.Click += (s, e) => Close();

            _btnPrint = new Button
            {
                Text = "Печать…",
                Location = new Point(428, 435),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPrint.Click += BtnPrint_Click;

            Controls.AddRange(new Control[]
            {
                _lblFile, _btnOpen, lblDpi, _numDpi,
                lblN, _cmbPagesPerSheet,
                _chkDuplex, lblEdge, _cmbDuplexEdge,
                lblCellOrientation, _cmbCellOrientation,
                lblPadding, _numPadding, lblPaddingMm,
                lblCache, _cmbCacheBudget, _btnClearCache,
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

            string orientText;
            switch (_cmbCellOrientation.SelectedIndex)
            {
                case 1: orientText = "Книжная"; break;
                case 2: orientText = "Альбомная"; break;
                default: orientText = "Авто"; break;
            }

            double padMm = (double)_numPadding.Value;
            double betweenMm = padMm * 2.0;

            _lblPreview.Text =
                "Страниц в PDF: " + _pageCount + "\r\n" +
                "Листов A4: " + sheets +
                " (логических страниц после дополнения: " + padded + ")\r\n" +
                "Ориентация страниц: " + orientText +
                ";  в памяти одновременно: не более " + n + " страниц\r\n" +
                "Отступ до края ячейки: " + padMm.ToString("0.0") +
                " мм;  между соседними страницами: " + betweenMm.ToString("0.0") + " мм\r\n" +
                "Кэш страниц: " + (_source.CacheBytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
        }

        // ====================================================================
        //  Параметры → документ
        // ====================================================================
        private CellContentOrientation GetCellOrientation()
        {
            switch (_cmbCellOrientation.SelectedIndex)
            {
                case 1: return CellContentOrientation.Portrait;
                case 2: return CellContentOrientation.Landscape;
                default: return CellContentOrientation.Auto;
            }
        }

        private double GetPaddingMm() => (double)_numPadding.Value;

        private NUpPrintDocument CreateDocument(bool duplex, bool longEdge)
        {
            int n = (int)_cmbPagesPerSheet.SelectedItem;
            int dpi = (int)_numDpi.Value;

            return new NUpPrintDocument(
                _source,
                n,
                duplex,
                longEdge,
                dpi,
                GetCellOrientation(),
                GetPaddingMm());
        }

        // ====================================================================
        //  Предпросмотр
        // ====================================================================
        private void BtnPreview_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            // Превью рендерится в 96 DPI — этого достаточно для экрана,
            // а память в 2–3 раза меньше, чем при 150 DPI.
            const int previewDpi = 96;

            using (var doc = CreateDocument(
                       _chkDuplex.Checked,
                       _cmbDuplexEdge.SelectedIndex == 0))
            using (var dlg = new PreviewForm(doc, _source, previewDpi, previewDpi))
            {
                dlg.ShowDialog(this);
            }

            // После закрытия превью обновим индикатор кэша.
            UpdatePreviewText();
        }

        // ====================================================================
        //  Печать
        // ====================================================================
        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool dup = _chkDuplex.Checked;
            bool longEdge = _cmbDuplexEdge.SelectedIndex == 0;
            var orient = GetCellOrientation();
            int dpi = (int)_numDpi.Value;
            double padMm = GetPaddingMm();

            using (var doc = new NUpPrintDocument(_source, n, dup, longEdge, dpi, orient, padMm))
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
                    bool actualDuplex = doc.PrinterSettings.Duplex != Duplex.Simplex;
                    bool actualLong = doc.PrinterSettings.Duplex != Duplex.Horizontal;

                    if (actualDuplex != dup || (actualDuplex && actualLong != longEdge))
                    {
                        // Драйвер поменял дуплекс — пересобираем документ.
                        string printerName = doc.PrinterSettings.PrinterName;
                        using (var redoc = new NUpPrintDocument(
                                   _source, n, actualDuplex, actualLong, dpi, orient, padMm))
                        {
                            redoc.PrinterSettings.PrinterName = printerName;
                            redoc.Print();
                        }
                    }
                    else
                    {
                        doc.Print();
                    }

                    MessageBox.Show(this,
                        "Документ отправлен на печать.",
                        "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Ошибка печати:\r\n" + ex.Message,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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