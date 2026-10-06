using System;
using System.Drawing;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Просмотрщик печати: одна сторона A4 за раз.
    /// В памяти держится ровно один Bitmap листа; страницы источника — в LRU-кэше
    /// PdfPageImageSource. Источник НЕ dispose'ится — им владеет MainForm.
    /// </summary>
    public sealed class PreviewForm : Form
    {
        private readonly NUpPrintDocument _doc;
        private readonly IPageImageSource _source;
        private readonly int _totalSheets;
        private readonly int _targetDpi;
        private readonly int _sourceDpi;

        private PictureBox _picture;
        private Panel _navPanel;
        private Button _btnFirst;
        private Button _btnPrev;
        private Button _btnNext;
        private Button _btnLast;
        private Button _btnClose;
        private NumericUpDown _numPage;
        private Label _lblOf;
        private Label _lblCache;
        private Timer _cacheTimer;

        private int _currentIndex;
        private Bitmap _currentBitmap;
        private bool _suppressNumEvent;

        public PreviewForm(
            NUpPrintDocument doc,
            IPageImageSource source,
            int targetDpi = 96,
            int sourceDpi = 96)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _source = source ?? throw new ArgumentNullException(nameof(source));

            _totalSheets = doc.SheetCount;
            _targetDpi = Math.Max(48, targetDpi);
            _sourceDpi = Math.Max(48, sourceDpi);

            if (_totalSheets <= 0)
                throw new InvalidOperationException("Документ не содержит листов.");

            BuildUi();

            // После лэйаута панели пересчитываем позиции закреплённых кнопок.
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
            ClientSize = new Size(900, 800);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 520);
            Font = new Font("Segoe UI", 9F);

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

            _lblCache = new Label
            {
                Location = new Point(400, 13),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Text = "Кэш: —"
            };

            _btnClose = new Button
            {
                Text = "Закрыть",
                Location = new Point(900, 8),       // скорректируется в Layout
                Size = new Size(96, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnClose.Click += (s, e) => Close();

            _navPanel.Controls.AddRange(new Control[]
            {
                _btnFirst, _btnPrev, _btnNext, _btnLast,
                _numPage, _lblOf, _lblCache, _btnClose
            });

            // ---------- область просмотра ----------
            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(64, 64, 64)
            };

            Controls.Add(_picture);
            Controls.Add(_navPanel);
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
            // Освобождаем предыдущий битмап ДО рендеринга нового.
            if (_currentBitmap != null)
            {
                _picture.Image = null;
                _currentBitmap.Dispose();
                _currentBitmap = null;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                _currentBitmap = _doc.RenderSheetToBitmap(
                    _currentIndex, _targetDpi, _sourceDpi);
                _picture.Image = _currentBitmap;
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

            // Обновим индикатор кэша сразу, не дожидаясь таймера.
            OnCacheTimerTick(this, EventArgs.Empty);
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

            if (_currentBitmap != null)
            {
                _currentBitmap.Dispose();
                _currentBitmap = null;
            }

            base.OnFormClosed(e);
            // _source и _doc НЕ dispose'им — ими владеет MainForm.
        }
    }
}