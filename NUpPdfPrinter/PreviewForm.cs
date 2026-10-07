using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Просмотрщик печати: одна сторона A4 за раз.
    /// Поддерживает режимы отображения:
    ///   «Страница целиком» — вписывает лист в окно;
    ///   «По ширине»         — масштабирует по ширине окна с прокруткой;
    ///   «100%»              — 1:1 к битмапу листа с прокруткой.
    /// Источник PDF НЕ dispose'ится — им владеет MainForm.
    /// </summary>
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
        private Label _lblPages;
        private ComboBox _cmbZoom;
        private Timer _cacheTimer;

        private int _currentIndex;
        private Bitmap _currentBitmap;
        private bool _suppressNumEvent;
        private bool _suppressZoomEvent;
        private ZoomMode _zoomMode = ZoomMode.FitPage;
        private Bitmap _sheetBuffer;
        private int _sheetBufferW, _sheetBufferH;

        public PreviewForm(
            NUpPrintDocument doc,
            IPageImageSource source,
            int targetDpi = 150,
            int sourceDpi = 150)
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

            RenderCurrent();

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

            // ---------- панель информации о страницах листа ----------
            _infoPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 26
            };

            _lblPages = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                ForeColor = Color.FromArgb(60, 60, 60),
                Text = "—"
            };
            _infoPanel.Controls.Add(_lblPages);

            // ---------- панель навигации ----------
            _navPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44
            };

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

            var lblZoom = new Label
            {
                Location = new Point(390, 13),
                AutoSize = true,
                Text = "Масштаб:"
            };

            _cmbZoom = new ComboBox
            {
                Location = new Point(455, 10),
                Size = new Size(160, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbZoom.Items.AddRange(new object[]
            {
                "Страница целиком",
                "По ширине",
                "100%"
            });
            _cmbZoom.SelectedIndex = 0;
            _cmbZoom.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressZoomEvent) return;
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
                _lblCache, _btnClose
            });

            // ---------- область просмотра ----------
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

            // Порядок добавления: Fill-элемент первым, Bottom-панели после.
            Controls.Add(_scrollPanel);
            Controls.Add(_infoPanel);
            Controls.Add(_navPanel);

            // Начальный размер PictureBox для «целиком» — Dock Fill подстроит.
        }

        // ====================================================================
        //  Масштабирование
        // ====================================================================
        /// <summary>
        /// Применяет выбранный режим масштабирования к PictureBox.
        /// В режимах «По ширине» и «100%» картинка может быть больше окна —
        /// тогда _scrollPanel показывает полосы прокрутки.
        /// </summary>
        private void ApplyZoomMode()
        {
            if (_currentBitmap == null) return;

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
                        int h = Math.Max(1,
                            (int)Math.Round((double)_currentBitmap.Height *
                                            w / _currentBitmap.Width));
                        _picture.Location = new Point(2, 2);
                        _picture.Size = new Size(w, h);
                        break;
                    }

                case ZoomMode.ActualSize:
                    {
                        _picture.Dock = DockStyle.None;
                        _picture.SizeMode = PictureBoxSizeMode.AutoSize;
                        _picture.Location = new Point(2, 2);
                        _picture.Size = _currentBitmap.Size;
                        break;
                    }
            }
        }

        // ====================================================================
        //  Индикатор кэша
        // ====================================================================
        private void OnCacheTimerTick(object sender, EventArgs e)
        {
            long bytes = 0;
            try { bytes = _source.CacheBytes; } catch { /* источник мог быть закрыт */ }

            _lblCache.Text = "Кэш: " +
                (bytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
        }

        // ====================================================================
        //  Навигация
        // ====================================================================
        private void GoTo(int index)
        {
            if (index < 0) index = 0;
            if (index >= _totalSheets) index = _totalSheets - 1;
            if (index == _currentIndex) return;

            _currentIndex = index;
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            EnsureSheetBuffer();

            Cursor = Cursors.WaitCursor;
            try
            {
                _picture.Image = null;   // отсоединяем перед перерисовкой
                _doc.RenderSheetInto(_sheetBuffer, _currentIndex, _sourceDpi);
                _picture.Image = _sheetBuffer;
                _picture.Invalidate();   // форсируем repaint, т.к. ссылка та же
                ApplyZoomMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Не удалось отрисовать лист:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            _suppressNumEvent = true;
            _numPage.Value = _currentIndex + 1;
            _suppressNumEvent = false;

            _btnFirst.Enabled = _btnPrev.Enabled = _currentIndex > 0;
            _btnNext.Enabled = _btnLast.Enabled = _currentIndex < _totalSheets - 1;

            Text = "Предпросмотр — лист " + (_currentIndex + 1) + " из " + _totalSheets;

            UpdatePagesLabel();
            OnCacheTimerTick(this, EventArgs.Empty);
        }

        /// <summary>
        /// Обновляет подпись под навигацией: какие PDF-страницы попали
        /// в текущий лист, в порядке ячеек (слева-направо, сверху-вниз).
        /// </summary>
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
                    sb.Append(pages[i] + 1);   // 1-based для пользователя
                    shown++;
                }

                if (shown == 0)
                    sb.Append("нет");
                else
                    sb.Append("   (всего ").Append(shown).Append(")");

                _lblPages.Text = sb.ToString();
            }
            catch
            {
                _lblPages.Text = "";
            }
        }

        private void EnsureSheetBuffer()
        {
            int w = _doc.DefaultPageSettings.PaperSize.Width;
            int h = _doc.DefaultPageSettings.PaperSize.Height;
            if (_doc.Landscape) { int t = w; w = h; h = t; }

            int pxW = Math.Max(1, (int)Math.Round(w / 100.0 * _targetDpi));
            int pxH = Math.Max(1, (int)Math.Round(h / 100.0 * _targetDpi));

            if (_sheetBuffer == null || _sheetBufferW != pxW || _sheetBufferH != pxH)
            {
                if (_sheetBuffer != null)
                {
                    _picture.Image = null;
                    _sheetBuffer.Dispose();
                }
                _sheetBuffer = new Bitmap(pxW, pxH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                _sheetBuffer.SetResolution(_targetDpi, _targetDpi);
                _sheetBufferW = pxW;
                _sheetBufferH = pxH;
            }
        }

        // ====================================================================
        //  Завершение
        // ====================================================================
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_cacheTimer != null)
            {
                _cacheTimer.Stop();
                _cacheTimer.Tick -= OnCacheTimerTick;
                _cacheTimer.Dispose();
                _cacheTimer = null;
            }

            if (_picture != null)
                _picture.Image = null;

            if (_sheetBuffer != null)
            {
                _sheetBuffer.Dispose();
                _sheetBuffer = null;
            }

            base.OnFormClosed(e);
            // _source и _doc НЕ dispose'им — ими владеет MainForm.
        }
    }
}