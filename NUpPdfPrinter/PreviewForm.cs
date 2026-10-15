using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    public sealed class PreviewForm : Form
    {
        private enum ZoomMode { FitPage, FitWidth, ActualSize }

        private readonly NUpPrintDocument _doc;
        private readonly IPageImageSource _source;
        private readonly int _totalSheets;
        private readonly int _targetDpi;
        private readonly int _sourceDpi;

        private Panel _scrollPanel;
        private PictureBox _picture;
        private Panel _navPanel;
        private Panel _infoPanel;
        private Button _btnFirst;
        private Button _btnPrev;
        private Button _btnNext;
        private Button _btnLast;
        private Button _btnClose;
        private NumericUpDown _numPage;
        private Label _lblOf;
        private Label _lblCache;
        private Label _lblMemory;
        private Label _lblPages;
        private ComboBox _cmbZoom;
        private Timer _cacheTimer;

        private int _currentIndex;
        private bool _suppressNumEvent;
        private ZoomMode _zoomMode = ZoomMode.FitPage;

        // Ping-pong буферы: один показывается, второй рендерится в фоне.
        // Так картинка никогда не пропадает между навигациями.
        private readonly Bitmap[] _sheetBuffers = new Bitmap[2];
        private int _frontBuffer = 0;
        private int _backBuffer = 1;
        private int _bufferW, _bufferH;

        // Защита от параллельного рендера и от обращения к disposed-ресурсам.
        private bool _isRendering;
        private bool _closing;

        public PreviewForm(
            NUpPrintDocument doc,
            IPageImageSource source,
            int targetDpi = 120,
            int sourceDpi = 120)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _source = source ?? throw new ArgumentNullException(nameof(source));

            _totalSheets = doc.SheetCount;
            _targetDpi = Math.Max(48, targetDpi);
            _sourceDpi = Math.Max(48, sourceDpi);

            if (_totalSheets <= 0)
                throw new InvalidOperationException("Документ не содержит листов.");

            BuildUi();

            _scrollPanel.Layout += (s, e) => ApplyZoomMode();
            _navPanel.Layout += (s, e) =>
            {
                _btnClose.Left = Math.Max(0, _navPanel.ClientSize.Width - _btnClose.Width - 8);
            };

            EnsureSheetBuffers();
            RenderCurrentAsync();

            _cacheTimer = new Timer { Interval = 500 };
            _cacheTimer.Tick += OnCacheTimerTick;
            _cacheTimer.Start();
        }

        // ====================================================================
        //  UI
        // ====================================================================
        private void BuildUi()
        {
            Text = "Предпросмотр печати";
            ClientSize = new Size(1000, 850);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 600);
            Font = new Font("Segoe UI", 9F);

            _infoPanel = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            _lblPages = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                ForeColor = Color.FromArgb(60, 60, 60),
                Text = "—"
            };
            _infoPanel.Controls.Add(_lblPages);

            _navPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };

            _btnFirst = new Button { Text = "|<", Location = new Point(8, 8), Size = new Size(40, 28) };
            _btnPrev = new Button { Text = "<", Location = new Point(52, 8), Size = new Size(40, 28) };
            _btnNext = new Button { Text = ">", Location = new Point(96, 8), Size = new Size(40, 28) };
            _btnLast = new Button { Text = ">|", Location = new Point(140, 8), Size = new Size(40, 28) };

            _btnFirst.Click += (s, e) => GoTo(0);
            _btnPrev.Click += (s, e) => GoTo(_currentIndex - 1);
            _btnNext.Click += (s, e) => GoTo(_currentIndex + 1);
            _btnLast.Click += (s, e) => GoTo(_totalSheets - 1);

            _numPage = new NumericUpDown
            {
                Location = new Point(200, 10),
                Size = new Size(70, 24),
                Minimum = 1,
                Maximum = _totalSheets,
                Value = 1,
                TextAlign = HorizontalAlignment.Right
            };
            _numPage.ValueChanged += (s, e) =>
            {
                if (_suppressNumEvent) return;
                GoTo((int)_numPage.Value - 1);
            };

            _lblOf = new Label
            {
                Location = new Point(276, 13),
                AutoSize = true,
                Text = "из " + _totalSheets
            };

            var lblZoom = new Label { Location = new Point(390, 13), AutoSize = true, Text = "Масштаб:" };

            _cmbZoom = new ComboBox
            {
                Location = new Point(455, 10),
                Size = new Size(160, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbZoom.Items.AddRange(new object[] { "Страница целиком", "По ширине", "100%" });
            _cmbZoom.SelectedIndex = 0;
            _cmbZoom.SelectedIndexChanged += (s, e) =>
            {
                _zoomMode = (ZoomMode)_cmbZoom.SelectedIndex;
                ApplyZoomMode();
            };

            _lblCache = new Label
            {
                Location = new Point(640, 13),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "Кэш: —"
            };

            _lblMemory = new Label
            {
                Location = new Point(790, 13),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "—"
            };

            _btnClose = new Button
            {
                Text = "Закрыть",
                Location = new Point(1000, 8),
                Size = new Size(96, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnClose.Click += (s, e) => Close();

            _navPanel.Controls.AddRange(new Control[]
            {
                _btnFirst, _btnPrev, _btnNext, _btnLast,
                _numPage, _lblOf,
                lblZoom, _cmbZoom,
                _lblCache, _lblMemory, _btnClose
            });

            _scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(64, 64, 64)
            };

            _picture = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Dock = DockStyle.Fill
            };

            _scrollPanel.Controls.Add(_picture);

            Controls.Add(_scrollPanel);
            Controls.Add(_infoPanel);
            Controls.Add(_navPanel);
        }

        // ====================================================================
        //  Ping-pong буферы
        // ====================================================================
        private void EnsureSheetBuffers()
        {
            int w = _doc.DefaultPageSettings.PaperSize.Width;
            int h = _doc.DefaultPageSettings.PaperSize.Height;
            if (_doc.Landscape) { int t = w; w = h; h = t; }

            int pxW = Math.Max(1, (int)Math.Round(w / 100.0 * _targetDpi));
            int pxH = Math.Max(1, (int)Math.Round(h / 100.0 * _targetDpi));

            if (_sheetBuffers[0] != null
                && _bufferW == pxW && _bufferH == pxH)
                return;

            for (int i = 0; i < 2; i++)
            {
                _sheetBuffers[i]?.Dispose();
                _sheetBuffers[i] = new Bitmap(pxW, pxH,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                _sheetBuffers[i].SetResolution(_targetDpi, _targetDpi);
            }
            _bufferW = pxW;
            _bufferH = pxH;
        }

        // ====================================================================
        //  Масштаб
        // ====================================================================
        private void ApplyZoomMode()
        {
            var bmp = _sheetBuffers[_frontBuffer];
            if (bmp == null) return;

            switch (_zoomMode)
            {
                case ZoomMode.FitPage:
                    _picture.Dock = DockStyle.Fill;
                    _picture.SizeMode = PictureBoxSizeMode.Zoom;
                    break;

                case ZoomMode.FitWidth:
                    {
                        _picture.Dock = DockStyle.None;
                        _picture.SizeMode = PictureBoxSizeMode.StretchImage;
                        int w = Math.Max(1, _scrollPanel.ClientSize.Width - 4);
                        int h = Math.Max(1, (int)Math.Round((double)bmp.Height * w / bmp.Width));
                        _picture.Location = new Point(2, 2);
                        _picture.Size = new Size(w, h);
                        break;
                    }

                case ZoomMode.ActualSize:
                    _picture.Dock = DockStyle.None;
                    _picture.SizeMode = PictureBoxSizeMode.AutoSize;
                    _picture.Location = new Point(2, 2);
                    _picture.Size = bmp.Size;
                    break;
            }
        }

        // ====================================================================
        //  Индикаторы
        // ====================================================================
        private void OnCacheTimerTick(object sender, EventArgs e)
        {
            long bytes = 0;
            try { bytes = _source.CacheBytes; } catch { }

            _lblCache.Text = "Кэш: " +
                (bytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";

            try
            {
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    p.Refresh();
                    double ws = p.WorkingSet64 / (1024.0 * 1024.0);
                    double heap = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                    _lblMemory.Text = "WS: " + ws.ToString("0") +
                                      " / heap: " + heap.ToString("0") + " МБ";
                }
            }
            catch { }
        }

        // ====================================================================
        //  Навигация
        // ====================================================================
        private void GoTo(int index)
        {
            if (index < 0) index = 0;
            if (index >= _totalSheets) index = _totalSheets - 1;
            if (index == _currentIndex) return;
            if (_isRendering) return;   // игнорируем клики во время рендера

            _currentIndex = index;
            RenderCurrentAsync();
        }

        private void SetNavEnabled(bool enabled)
        {
            _btnFirst.Enabled = enabled && _currentIndex > 0;
            _btnPrev.Enabled = enabled && _currentIndex > 0;
            _btnNext.Enabled = enabled && _currentIndex < _totalSheets - 1;
            _btnLast.Enabled = enabled && _currentIndex < _totalSheets - 1;
            _numPage.Enabled = enabled;
        }

        // ====================================================================
        //  Асинхронный рендер
        // ====================================================================
        /// <summary>
        /// Рендерит текущий лист в фоновом потоке.
        /// Результат кладётся в задний буфер, затем буферы меняются местами —
        /// картинка не пропадает во время рендера.
        /// </summary>
        private async void RenderCurrentAsync()
        {
            if (_isRendering || _closing) return;

            _isRendering = true;
            int idx = _currentIndex;
            SetNavEnabled(false);
            Cursor = Cursors.WaitCursor;

            try
            {
                EnsureSheetBuffers();
                var backBmp = _sheetBuffers[_backBuffer];
                var doc = _doc;
                var srcDpi = _sourceDpi;

                await Task.Run(() =>
                {
                    if (_closing) return;
                    doc.RenderSheetInto(backBmp, idx, srcDpi);
                });

                if (_closing) return;

                // Swap front/back и присвоить картинку.
                int tmp = _frontBuffer;
                _frontBuffer = _backBuffer;
                _backBuffer = tmp;

                _picture.Image = _sheetBuffers[_frontBuffer];
                _picture.Invalidate();
                ApplyZoomMode();
            }
            catch (Exception ex)
            {
                if (!_closing)
                {
                    MessageBox.Show(this,
                        "Не удалось отрисовать лист:\r\n" + ex.Message,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!_closing)
                {
                    _isRendering = false;
                    Cursor = Cursors.Default;
                    SetNavEnabled(true);

                    _suppressNumEvent = true;
                    _numPage.Value = _currentIndex + 1;
                    _suppressNumEvent = false;

                    Text = "Предпросмотр — лист " + (_currentIndex + 1) + " из " + _totalSheets;

                    UpdatePagesLabel();
                    OnCacheTimerTick(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Обновляет строку «PDF-страницы на листе».</summary>
        private void UpdatePagesLabel()
        {
            try
            {
                int[] pages = _doc.GetSheetPageNumbers(_currentIndex);
                var sb = new StringBuilder();
                sb.Append("PDF-страницы на листе (по ячейкам): ");
                int shown = 0;
                for (int i = 0; i < pages.Length; i++)
                {
                    if (pages[i] < 0) continue;
                    if (shown > 0) sb.Append(", ");
                    sb.Append(pages[i] + 1);
                    shown++;
                }
                if (shown == 0) sb.Append("нет");
                else sb.Append("   (всего ").Append(shown).Append(")");
                _lblPages.Text = sb.ToString();
            }
            catch { _lblPages.Text = ""; }
        }

        // ====================================================================
        //  Завершение
        // ====================================================================
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closing = true;

            if (_cacheTimer != null)
            {
                _cacheTimer.Stop();
                _cacheTimer.Tick -= OnCacheTimerTick;
                _cacheTimer.Dispose();
                _cacheTimer = null;
            }

            if (_picture != null)
                _picture.Image = null;

            for (int i = 0; i < 2; i++)
            {
                if (_sheetBuffers[i] != null)
                {
                    _sheetBuffers[i].Dispose();
                    _sheetBuffers[i] = null;
                }
            }

            base.OnFormClosed(e);
            // _source и _doc НЕ dispose'им — ими владеет MainForm.
        }
    }
}