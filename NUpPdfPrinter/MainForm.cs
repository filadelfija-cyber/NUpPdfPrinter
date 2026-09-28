using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    public class MainForm : Form
    {
        private List<Image> _pageImages = new();
        private string? _pdfPath;

        private Label _lblFile = null!;
        private Button _btnOpen = null!;
        private Button _btnPrint = null!;
        private ComboBox _cmbPagesPerSheet = null!;
        private CheckBox _chkDuplex = null!;
        private ComboBox _cmbDuplexEdge = null!;
        private ProgressBar _progress = null!;

        public MainForm()
        {
            InitializeUi();
        }

        private void InitializeUi()
        {
            Text = "N-up печать PDF (4/6/8/9 на листе A4)";
            ClientSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            _lblFile = new Label
            {
                Text = "Файл не выбран",
                Location = new Point(12, 12),
                Size = new Size(496, 20),
                AutoEllipsis = true
            };

            _btnOpen = new Button
            {
                Text = "Открыть PDF…",
                Location = new Point(12, 40),
                Size = new Size(130, 30)
            };
            _btnOpen.Click += BtnOpen_Click;

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
                Size = new Size(200, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDuplexEdge.Items.AddRange(new object[]
            {
                "По длинному краю (стандарт)",
                "По короткому краю"
            });
            _cmbDuplexEdge.SelectedIndex = 0;

            _progress = new ProgressBar
            {
                Location = new Point(12, 200),
                Size = new Size(496, 20),
                Visible = false
            };

            _btnPrint = new Button
            {
                Text = "Печать…",
                Location = new Point(378, 235),
                Size = new Size(130, 34),
                Enabled = false
            };
            _btnPrint.Click += BtnPrint_Click;

            var btnCancel = new Button
            {
                Text = "Выход",
                Location = new Point(242, 235),
                Size = new Size(130, 34)
            };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                _lblFile, _btnOpen, lblN, _cmbPagesPerSheet,
                _chkDuplex, lblEdge, _cmbDuplexEdge,
                _progress, _btnPrint, btnCancel
            });
        }

        private async void BtnOpen_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "PDF-файлы (*.pdf)|*.pdf|Все файлы (*.*)|*.*",
                Title = "Выберите PDF-документ"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            _pdfPath = ofd.FileName;
            _btnOpen.Enabled = false;
            _btnPrint.Enabled = false;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _lblFile.Text = $"Загрузка: {Path.GetFileName(_pdfPath)}…";
            Text = $"N-up печать PDF — загрузка…";

            try
            {
                ClearImages();
                int dpi = 150;
                var images = await Task.Run(() => PdfPageRenderer.RenderPages(_pdfPath!, dpi));
                _pageImages = images;

                _lblFile.Text = $"{Path.GetFileName(_pdfPath)} — {_pageImages.Count} стр.";
                Text = $"N-up печать PDF — {Path.GetFileName(_pdfPath)}";
                _btnPrint.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Не удалось загрузить PDF:\n{ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblFile.Text = "Файл не выбран";
                Text = "N-up печать PDF";
            }
            finally
            {
                _progress.Visible = false;
                _btnOpen.Enabled = true;
            }
        }

        private void BtnPrint_Click(object? sender, EventArgs e)
        {
            if (_pageImages.Count == 0)
            {
                MessageBox.Show(this, "Сначала загрузите PDF.", "Нет данных",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int pagesPerSheet = (int)_cmbPagesPerSheet.SelectedItem!;
            bool duplex = _chkDuplex.Checked;
            bool longEdge = _cmbDuplexEdge.SelectedIndex == 0;

            using var doc = new NUpPrintDocument(_pageImages, pagesPerSheet, duplex, longEdge);

            using var dlg = new PrintDialog
            {
                Document = doc,
                UseEXDialog = true,
                AllowSomePages = false,
                AllowSelection = false,
                AllowPrintToFile = false
            };

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                // Актуализируем дуплекс — пользователь мог изменить в диалоге
                bool actualDuplex = doc.PrinterSettings.Duplex != Duplex.Simplex;
                if (actualDuplex != duplex)
                {
                    // Пересоздаём документ с настройками из диалога
                    var ps = doc.PrinterSettings;
                    using var redoc = new NUpPrintDocument(_pageImages, pagesPerSheet,
                        actualDuplex,
                        ps.Duplex == Duplex.Vertical)
                    {
                        PrinterSettings = { PrinterName = ps.PrinterName }
                    };
                    redoc.Print();
                }
                else
                {
                    doc.Print();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Ошибка печати:\n{ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearImages()
        {
            foreach (var img in _pageImages) img?.Dispose();
            _pageImages.Clear();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearImages();
            base.OnFormClosing(e);
        }
    }
}