using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    public sealed class MainForm : Form
    {
        private IPageImageSource _source; // владелец PDF-документа
        private string _pdfPath;
        private int _pageCount;

        private Label _lblFile;
        private Label _lblPreview;
        private Button _btnOpen;
        private Button _btnPrint;
        private Button _btnPreview;
        private ComboBox _cmbPagesPerSheet;
        private CheckBox _chkDuplex;
        private ComboBox _cmbDuplexEdge;
        private NumericUpDown _numDpi;

        public MainForm()
        {
            InitializeUi();
        }

        private void InitializeUi()
        {
            // ... идентично предыдущей версии ...
            // (см. предыдущий ответ — код UI не изменился, кроме удаления _progress)
            // Ниже — краткая версия для полноты примера:
            Text = "N-up печать PDF (4 / 6 / 8 / 9 на листе A4)";
            ClientSize = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            _lblFile = new Label { Text = "Файл не выбран", Location = new Point(12, 12), Size = new Size(536, 20), AutoEllipsis = true };

            _btnOpen = new Button { Text = "Открыть PDF…", Location = new Point(12, 40), Size = new Size(130, 30) };
            _btnOpen.Click += BtnOpen_Click;

            var lblDpi = new Label { Text = "DPI рендеринга:", Location = new Point(160, 45), Size = new Size(100, 22) };
            _numDpi = new NumericUpDown { Location = new Point(270, 43), Size = new Size(80, 24), Minimum = 72, Maximum = 400, Value = 150, Increment = 50 };

            var lblN = new Label { Text = "Страниц на листе:", Location = new Point(12, 90), Size = new Size(130, 22) };
            _cmbPagesPerSheet = new ComboBox { Location = new Point(150, 87), Size = new Size(100, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbPagesPerSheet.Items.AddRange(new object[] { 4, 6, 8, 9 });
            _cmbPagesPerSheet.SelectedIndex = 0;

            _chkDuplex = new CheckBox { Text = "Двусторонняя печать", Location = new Point(12, 125), Size = new Size(220, 24), Checked = true };
            _chkDuplex.CheckedChanged += (s, e) => { _cmbDuplexEdge.Enabled = _chkDuplex.Checked; UpdatePreviewText(); };

            var lblEdge = new Label { Text = "Переворот листа:", Location = new Point(12, 160), Size = new Size(130, 22) };
            _cmbDuplexEdge = new ComboBox { Location = new Point(150, 157), Size = new Size(230, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDuplexEdge.Items.AddRange(new object[] { "По длинному краю (стандарт)", "По короткому краю" });
            _cmbDuplexEdge.SelectedIndex = 0;
            _cmbDuplexEdge.SelectedIndexChanged += (s, e) => UpdatePreviewText();
            _cmbPagesPerSheet.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            _lblPreview = new Label { Location = new Point(12, 200), Size = new Size(536, 60), ForeColor = Color.DimGray };

            _btnPreview = new Button { Text = "Предпросмотр…", Location = new Point(12, 280), Size = new Size(140, 34), Enabled = false };
            _btnPreview.Click += BtnPreview_Click;

            _btnPrint = new Button { Text = "Печать…", Location = new Point(408, 280), Size = new Size(140, 34), Enabled = false };
            _btnPrint.Click += BtnPrint_Click;

            var btnExit = new Button { Text = "Выход", Location = new Point(258, 280), Size = new Size(140, 34) };
            btnExit.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                _lblFile, _btnOpen, lblDpi, _numDpi,
                lblN, _cmbPagesPerSheet,
                _chkDuplex, lblEdge, _cmbDuplexEdge,
                _lblPreview, _btnPreview, _btnPrint, btnExit
            });
        }

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
                // Открытие PdfDocument — быстрая операция (mmap), рендеринга нет.
                _source = new PdfPageImageSource(_pdfPath);
                _pageCount = _source.PageCount;

                _lblFile.Text = Path.GetFileName(_pdfPath) + " — " + _pageCount + " стр.";
                _btnPrint.Enabled = true;
                _btnPreview.Enabled = true;
                UpdatePreviewText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Не удалось открыть PDF:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblFile.Text = "Файл не выбран";
                _lblPreview.Text = string.Empty;
                _btnPrint.Enabled = _btnPreview.Enabled = false;
            }
        }

        private void UpdatePreviewText()
        {
            if (_source == null || _pageCount == 0) { _lblPreview.Text = string.Empty; return; }

            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool dup = _chkDuplex.Checked;
            int block = dup ? 2 * n : n;
            int sheets = (_pageCount + block - 1) / block;
            int padded = sheets * block;

            _lblPreview.Text =
                "Страниц в PDF: " + _pageCount + "\r\n" +
                "Листов A4: " + sheets + " (логических страниц после дополнения: " + padded + ")\r\n" +
                "В памяти одновременно: не более " + n + " страниц вместо " + _pageCount + ".";
        }

        private NUpPrintDocument CreateDocument()
        {
            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool dup = _chkDuplex.Checked;
            bool longEdge = _cmbDuplexEdge.SelectedIndex == 0;
            int dpi = (int)_numDpi.Value;

            return new NUpPrintDocument(_source, n, dup, longEdge, dpi);
        }

        private void BtnPreview_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            using (var doc = CreateDocument())
            using (var dlg = new PrintPreviewDialog
            {
                Document = doc,
                Width = 1000,
                Height = 800,
                StartPosition = FormStartPosition.CenterParent,
                UseAntiAlias = true
            })
            {
                dlg.ShowDialog(this);
            }
        }

        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            int n = (int)_cmbPagesPerSheet.SelectedItem;
            bool dup = _chkDuplex.Checked;
            bool longEdge = _cmbDuplexEdge.SelectedIndex == 0;

            using (var doc = CreateDocument())
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
                        string printerName = doc.PrinterSettings.PrinterName;
                        using (var redoc = new NUpPrintDocument(_source, n, actualDuplex, actualLong, (int)_numDpi.Value))
                        {
                            redoc.PrinterSettings.PrinterName = printerName;
                            redoc.Print();
                        }
                    }
                    else
                    {
                        doc.Print();
                    }

                    MessageBox.Show(this, "Документ отправлен на печать.",
                        "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка печати:\r\n" + ex.Message,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

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