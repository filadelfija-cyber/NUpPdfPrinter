using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Главная форма приложения: открытие PDF, настройка N-up печати,
    /// предпросмотр и печать.
    ///
    /// Компоновка: горизонтальная (шире, чем выше), две колонки групп.
    /// Слева — параметры документа и печати, справа — раскладка, отступы, кэш.
    /// </summary>
    public sealed class MainForm : Form
    {
        // ====================================================================
        //  Состояние
        // ====================================================================
        private IPageImageSource _source;
        private string _pdfPath;
        private int _pageCount;

        private bool _suppressRangeUpdate;
        private bool _suppressMarginSync;

        // ====================================================================
        //  UI-элементы
        // ====================================================================
        // Группа «Документ PDF»
        private Label _lblFile;
        private Button _btnOpen;
        private Label _lblDpi;
        private NumericUpDown _numDpi;

        // Группа «Страницы для печати»
        private CheckBox _chkAllPages;
        private Label _lblRangeFrom;
        private NumericUpDown _numFirstPage;
        private Label _lblRangeTo;
        private NumericUpDown _numLastPage;

        // Группа «Двусторонняя печать»
        private CheckBox _chkDuplex;
        private Label _lblEdge;
        private ComboBox _cmbDuplexEdge;

        // Группа «Совмещение сторон»
        private CheckBox _chkRegistrationMarks;
        private Label _lblBackOffset;
        private Label _lblBackOffsetX;
        private NumericUpDown _numBackOffsetX;
        private Label _lblBackOffsetXmm;
        private Label _lblBackOffsetY;
        private NumericUpDown _numBackOffsetY;
        private Label _lblBackOffsetYmm;

        // Группа «Раскладка листа»
        private Label _lblPagesPerSheet;
        private ComboBox _cmbPagesPerSheet;
        private Label _lblOrientation;
        private ComboBox _cmbOrientation;
        private Label _lblCellOrientation;
        private ComboBox _cmbCellOrientation;
        private Label _lblPadding;
        private NumericUpDown _numPadding;
        private Label _lblPaddingMm;

        // Группа «Отступы от края A4»
        private CheckBox _chkSameMargins;
        private Label _lblMarginLeft;
        private NumericUpDown _numMarginLeft;
        private Label _lblMarginLeftMm;
        private Label _lblMarginTop;
        private NumericUpDown _numMarginTop;
        private Label _lblMarginTopMm;
        private Label _lblMarginRight;
        private NumericUpDown _numMarginRight;
        private Label _lblMarginRightMm;
        private Label _lblMarginBottom;
        private NumericUpDown _numMarginBottom;
        private Label _lblMarginBottomMm;

        // Группа «Кэш страниц»
        private Label _lblCache;
        private ComboBox _cmbCacheBudget;
        private Button _btnClearCache;

        // Сводка и кнопки
        private Label _lblPreview;
        private Button _btnPreview;
        private Button _btnExit;
        private Button _btnPrint;

        // ====================================================================
        //  Конструктор
        // ====================================================================
        public MainForm()
        {
            InitializeUi();
        }

        // ====================================================================
        //  Построение интерфейса
        // ====================================================================
        private void InitializeUi()
        {
            Text = "N-up печать PDF (4 / 6 / 8 / 9 на листе A4)";
            ClientSize = new Size(920, 640);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            // ---------- Левая колонка x=12, ширина 440 ----------
            BuildGroupDocument();
            BuildGroupPages();
            BuildGroupDuplex();
            BuildGroupAlign();

            // ---------- Правая колонка x=468, ширина 440 ----------
            BuildGroupLayout();
            BuildGroupMargins();
            BuildGroupCache();

            // ---------- Сводка во всю ширину ----------
            _lblPreview = new Label
            {
                Location = new Point(12, 462),
                Size = new Size(896, 110),
                AutoSize = false,
                ForeColor = Color.DimGray
            };
            Controls.Add(_lblPreview);

            // ---------- Кнопки внизу, по центру ----------
            _btnPreview = new Button
            {
                Text = "Предпросмотр…",
                Location = new Point(238, 582),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPreview.Click += BtnPreview_Click;

            _btnExit = new Button
            {
                Text = "Выход",
                Location = new Point(390, 582),
                Size = new Size(140, 34)
            };
            _btnExit.Click += (s, e) => Close();

            _btnPrint = new Button
            {
                Text = "Печать…",
                Location = new Point(542, 582),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPrint.Click += BtnPrint_Click;

            Controls.AddRange(new Control[] { _btnPreview, _btnExit, _btnPrint });
        }

        // ====================================================================
        //  Группа «Документ PDF» (левая колонка, y=12)
        // ====================================================================
        private void BuildGroupDocument()
        {
            var grp = new GroupBox
            {
                Text = "Документ PDF",
                Location = new Point(12, 12),
                Size = new Size(440, 95)
            };

            _lblFile = new Label
            {
                Text = "Файл не выбран",
                Location = new Point(10, 22),
                Size = new Size(420, 20),
                AutoSize = false,
                AutoEllipsis = true
            };

            _btnOpen = new Button
            {
                Text = "Открыть PDF…",
                Location = new Point(10, 50),
                Size = new Size(130, 30)
            };
            _btnOpen.Click += BtnOpen_Click;

            _lblDpi = new Label
            {
                Text = "DPI рендеринга:",
                Location = new Point(155, 56),
                AutoSize = true
            };

            _numDpi = new NumericUpDown
            {
                Location = new Point(275, 53),
                Size = new Size(80, 24),
                Minimum = 72,
                Maximum = 400,
                Value = 150,
                Increment = 50
            };

            grp.Controls.AddRange(new Control[]
            {
                _lblFile, _btnOpen, _lblDpi, _numDpi
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Страницы для печати» (левая колонка, y=117)
        // ====================================================================
        private void BuildGroupPages()
        {
            var grp = new GroupBox
            {
                Text = "Страницы для печати",
                Location = new Point(12, 117),
                Size = new Size(440, 65)
            };

            _chkAllPages = new CheckBox
            {
                Text = "Все страницы",
                Location = new Point(10, 22),
                Size = new Size(115, 24),
                Checked = true
            };
            _chkAllPages.CheckedChanged += (s, e) =>
            {
                bool enabled = !_chkAllPages.Checked;
                _numFirstPage.Enabled = enabled;
                _numLastPage.Enabled = enabled;
                UpdatePreviewText();
            };

            _lblRangeFrom = new Label
            {
                Text = "с:",
                Location = new Point(135, 25),
                AutoSize = true
            };

            _numFirstPage = new NumericUpDown
            {
                Location = new Point(155, 21),
                Size = new Size(65, 24),
                Minimum = 1,
                Maximum = 1,
                Value = 1,
                Enabled = false,
                TextAlign = HorizontalAlignment.Right
            };
            _numFirstPage.ValueChanged += (s, e) =>
            {
                if (_suppressRangeUpdate) return;
                if (_numLastPage.Value < _numFirstPage.Value)
                {
                    _suppressRangeUpdate = true;
                    _numLastPage.Value = _numFirstPage.Value;
                    _suppressRangeUpdate = false;
                }
                UpdatePreviewText();
            };

            _lblRangeTo = new Label
            {
                Text = "по:",
                Location = new Point(228, 25),
                AutoSize = true
            };

            _numLastPage = new NumericUpDown
            {
                Location = new Point(256, 21),
                Size = new Size(65, 24),
                Minimum = 1,
                Maximum = 1,
                Value = 1,
                Enabled = false,
                TextAlign = HorizontalAlignment.Right
            };
            _numLastPage.ValueChanged += (s, e) =>
            {
                if (_suppressRangeUpdate) return;
                if (_numLastPage.Value < _numFirstPage.Value)
                {
                    _suppressRangeUpdate = true;
                    _numFirstPage.Value = _numLastPage.Value;
                    _suppressRangeUpdate = false;
                }
                UpdatePreviewText();
            };

            var tipRange = new ToolTip();
            tipRange.SetToolTip(_numFirstPage, "Начальная страница (включительно).");
            tipRange.SetToolTip(_numLastPage, "Конечная страница (включительно).");

            grp.Controls.AddRange(new Control[]
            {
                _chkAllPages, _lblRangeFrom, _numFirstPage, _lblRangeTo, _numLastPage
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Двусторонняя печать» (левая колонка, y=192)
        // ====================================================================
        private void BuildGroupDuplex()
        {
            var grp = new GroupBox
            {
                Text = "Двусторонняя печать",
                Location = new Point(12, 192),
                Size = new Size(440, 85)
            };

            _chkDuplex = new CheckBox
            {
                Text = "Печатать с двух сторон",
                Location = new Point(10, 22),
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
                Location = new Point(10, 52),
                AutoSize = true
            };

            _cmbDuplexEdge = new ComboBox
            {
                Location = new Point(150, 49),
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

            var tipEdge = new ToolTip();
            tipEdge.SetToolTip(_cmbDuplexEdge,
                "Как принтер переворачивает лист.\r\n" +
                "Длинный край — «книжка».\r\n" +
                "Короткий край — «блокнот».");

            grp.Controls.AddRange(new Control[]
            {
                _chkDuplex, _lblEdge, _cmbDuplexEdge
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Совмещение сторон» (левая колонка, y=287)
        // ====================================================================
        private void BuildGroupAlign()
        {
            var grp = new GroupBox
            {
                Text = "Совмещение сторон",
                Location = new Point(12, 287),
                Size = new Size(440, 105)
            };

            _chkRegistrationMarks = new CheckBox
            {
                Text = "Метки совмещения (рамка и крест на обеих сторонах)",
                Location = new Point(10, 22),
                Size = new Size(420, 24),
                Checked = false
            };
            _chkRegistrationMarks.CheckedChanged += (s, e) => UpdatePreviewText();

            var tipReg = new ToolTip();
            tipReg.SetToolTip(_chkRegistrationMarks,
                "Печатает рамку и крест на обеих сторонах листа.\r\n" +
                "На просвет видно, куда смещён оборот.\r\n" +
                "Выключите для чистовой печати.");

            _lblBackOffset = new Label
            {
                Text = "Сдвиг оборота:",
                Location = new Point(10, 55),
                AutoSize = true
            };

            _lblBackOffsetX = new Label
            {
                Text = "X",
                Location = new Point(110, 55),
                AutoSize = true
            };

            _numBackOffsetX = new NumericUpDown
            {
                Location = new Point(128, 53),
                Size = new Size(60, 24),
                Minimum = -10m,
                Maximum = 10m,
                DecimalPlaces = 1,
                Increment = 0.1m,
                Value = 0m
            };
            _numBackOffsetX.ValueChanged += (s, e) => UpdatePreviewText();

            _lblBackOffsetXmm = new Label
            {
                Text = "мм",
                Location = new Point(193, 55),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _lblBackOffsetY = new Label
            {
                Text = "Y",
                Location = new Point(225, 55),
                AutoSize = true
            };

            _numBackOffsetY = new NumericUpDown
            {
                Location = new Point(243, 53),
                Size = new Size(60, 24),
                Minimum = -10m,
                Maximum = 10m,
                DecimalPlaces = 1,
                Increment = 0.1m,
                Value = 0m
            };
            _numBackOffsetY.ValueChanged += (s, e) => UpdatePreviewText();

            _lblBackOffsetYmm = new Label
            {
                Text = "мм",
                Location = new Point(308, 55),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipOffset = new ToolTip();
            tipOffset.SetToolTip(_numBackOffsetX,
                "Сдвиг содержимого обратной стороны по X (мм).\r\n" +
                "+ вправо, − влево.");
            tipOffset.SetToolTip(_numBackOffsetY,
                "Сдвиг содержимого обратной стороны по Y (мм).\r\n" +
                "+ вниз, − вверх.");

            grp.Controls.AddRange(new Control[]
            {
                _chkRegistrationMarks,
                _lblBackOffset,
                _lblBackOffsetX, _numBackOffsetX, _lblBackOffsetXmm,
                _lblBackOffsetY, _numBackOffsetY, _lblBackOffsetYmm
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Раскладка листа» (правая колонка, y=12)
        // ====================================================================
        private void BuildGroupLayout()
        {
            var grp = new GroupBox
            {
                Text = "Раскладка листа",
                Location = new Point(468, 12),
                Size = new Size(440, 175)
            };

            _lblPagesPerSheet = new Label
            {
                Text = "Страниц на листе:",
                Location = new Point(10, 22),
                AutoSize = true
            };

            _cmbPagesPerSheet = new ComboBox
            {
                Location = new Point(170, 19),
                Size = new Size(100, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbPagesPerSheet.Items.AddRange(new object[] { 4, 6, 8, 9 });
            _cmbPagesPerSheet.SelectedIndex = 0;
            _cmbPagesPerSheet.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            _lblOrientation = new Label
            {
                Text = "Формат листа:",
                Location = new Point(10, 55),
                AutoSize = true
            };

            _cmbOrientation = new ComboBox
            {
                Location = new Point(170, 52),
                Size = new Size(200, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbOrientation.Items.AddRange(new object[]
            {
                "Книжный (портрет)",
                "Альбомный (ландшафт)"
            });
            _cmbOrientation.SelectedIndex = 0;
            _cmbOrientation.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            _lblCellOrientation = new Label
            {
                Text = "Поворот страниц PDF:",
                Location = new Point(10, 88),
                AutoSize = true
            };

            _cmbCellOrientation = new ComboBox
            {
                Location = new Point(170, 85),
                Size = new Size(200, 24),
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

            _lblPadding = new Label
            {
                Text = "Зазор между страницами:",
                Location = new Point(10, 121),
                AutoSize = true
            };

            _numPadding = new NumericUpDown
            {
                Location = new Point(170, 118),
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
                Text = "мм (0…20)",
                Location = new Point(248, 121),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipPadding = new ToolTip();
            tipPadding.SetToolTip(_numPadding,
                "Отступ вокруг каждой страницы внутри её ячейки.\r\n" +
                "Реальное расстояние между соседними страницами = 2 × значение.");

            var tipOrientation = new ToolTip();
            tipOrientation.SetToolTip(_cmbOrientation,
                "Как ориентирован физический лист A4 при печати.");
            tipOrientation.SetToolTip(_cmbCellOrientation,
                "Как исходная страница PDF ориентирована внутри ячейки.");

            grp.Controls.AddRange(new Control[]
            {
                _lblPagesPerSheet, _cmbPagesPerSheet,
                _lblOrientation, _cmbOrientation,
                _lblCellOrientation, _cmbCellOrientation,
                _lblPadding, _numPadding, _lblPaddingMm
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Отступы от края A4» (правая колонка, y=197)
        // ====================================================================
        private void BuildGroupMargins()
        {
            var grp = new GroupBox
            {
                Text = "Отступы от края листа A4",
                Location = new Point(468, 197),
                Size = new Size(440, 160)
            };

            _chkSameMargins = new CheckBox
            {
                Text = "Одинаковые со всех сторон",
                Location = new Point(10, 22),
                Size = new Size(420, 24),
                Checked = true
            };
            _chkSameMargins.CheckedChanged += ChkSameMargins_CheckedChanged;

            var tipMargins = new ToolTip();
            tipMargins.SetToolTip(_chkSameMargins,
                "Если снять — можно задать разные отступы по сторонам.\r\n" +
                "Полезно, если принтер не может печатать ближе 5 мм\r\n" +
                "к правому/нижнему краю.");

            // ----- Сетка 2×2: Слева / Сверху / Справа / Снизу -----
            _lblMarginLeft = new Label
            {
                Text = "Слева:",
                Location = new Point(10, 58),
                AutoSize = true
            };
            _numMarginLeft = new NumericUpDown
            {
                Location = new Point(65, 55),
                Size = new Size(65, 24),
                Minimum = 0m,
                Maximum = 30m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m,
                TextAlign = HorizontalAlignment.Right
            };
            _lblMarginLeftMm = new Label
            {
                Text = "мм",
                Location = new Point(135, 58),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _lblMarginTop = new Label
            {
                Text = "Сверху:",
                Location = new Point(175, 58),
                AutoSize = true
            };
            _numMarginTop = new NumericUpDown
            {
                Location = new Point(240, 55),
                Size = new Size(65, 24),
                Minimum = 0m,
                Maximum = 30m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m,
                TextAlign = HorizontalAlignment.Right,
                Enabled = false
            };
            _lblMarginTopMm = new Label
            {
                Text = "мм",
                Location = new Point(310, 58),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _lblMarginRight = new Label
            {
                Text = "Справа:",
                Location = new Point(10, 91),
                AutoSize = true
            };
            _numMarginRight = new NumericUpDown
            {
                Location = new Point(65, 88),
                Size = new Size(65, 24),
                Minimum = 0m,
                Maximum = 30m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m,
                TextAlign = HorizontalAlignment.Right,
                Enabled = false
            };
            _lblMarginRightMm = new Label
            {
                Text = "мм",
                Location = new Point(135, 91),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _lblMarginBottom = new Label
            {
                Text = "Снизу:",
                Location = new Point(175, 91),
                AutoSize = true
            };
            _numMarginBottom = new NumericUpDown
            {
                Location = new Point(240, 88),
                Size = new Size(65, 24),
                Minimum = 0m,
                Maximum = 30m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m,
                TextAlign = HorizontalAlignment.Right,
                Enabled = false
            };
            _lblMarginBottomMm = new Label
            {
                Text = "мм",
                Location = new Point(310, 91),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            // Синхронизация значений при включённой галочке.
            EventHandler sync = MarginNumeric_ValueChanged;
            _numMarginLeft.ValueChanged += sync;
            _numMarginTop.ValueChanged += sync;
            _numMarginRight.ValueChanged += sync;
            _numMarginBottom.ValueChanged += sync;

            tipMargins.SetToolTip(_numMarginRight,
                "У большинства принтеров аппаратный предел ~5 мм справа.");
            tipMargins.SetToolTip(_numMarginBottom,
                "У большинства принтеров аппаратный предел ~5 мм снизу.");

            grp.Controls.AddRange(new Control[]
            {
                _chkSameMargins,
                _lblMarginLeft,   _numMarginLeft,   _lblMarginLeftMm,
                _lblMarginTop,    _numMarginTop,    _lblMarginTopMm,
                _lblMarginRight,  _numMarginRight,  _lblMarginRightMm,
                _lblMarginBottom, _numMarginBottom, _lblMarginBottomMm
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Кэш страниц» (правая колонка, y=367)
        // ====================================================================
        private void BuildGroupCache()
        {
            var grp = new GroupBox
            {
                Text = "Кэш страниц",
                Location = new Point(468, 367),
                Size = new Size(440, 85)
            };

            _lblCache = new Label
            {
                Text = "Бюджет:",
                Location = new Point(10, 25),
                AutoSize = true
            };

            _cmbCacheBudget = new ComboBox
            {
                Location = new Point(110, 22),
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
                Location = new Point(320, 21),
                Size = new Size(110, 28),
                Enabled = false
            };
            _btnClearCache.Click += BtnClearCache_Click;

            var tipCache = new ToolTip();
            tipCache.SetToolTip(_cmbCacheBudget,
                "Сколько страниц PDF держать в памяти для быстрого листания.\r\n" +
                "Меньше — экономнее, но медленнее.");

            grp.Controls.AddRange(new Control[]
            {
                _lblCache, _cmbCacheBudget, _btnClearCache
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Обработчики отступов
        // ====================================================================
        /// <summary>
        /// Включает/выключает поля «Сверху», «Справа», «Снизу» в зависимости
        /// от галочки «Одинаковые со всех сторон». При включении синхронизирует
        /// их значения со значением поля «Слева».
        /// </summary>
        private void ChkSameMargins_CheckedChanged(object sender, EventArgs e)
        {
            bool individual = !_chkSameMargins.Checked;

            _numMarginLeft.Enabled = true;
            _numMarginTop.Enabled = individual;
            _numMarginRight.Enabled = individual;
            _numMarginBottom.Enabled = individual;

            if (!individual)
            {
                _suppressMarginSync = true;
                _numMarginTop.Value = _numMarginLeft.Value;
                _numMarginRight.Value = _numMarginLeft.Value;
                _numMarginBottom.Value = _numMarginLeft.Value;
                _suppressMarginSync = false;
            }

            UpdatePreviewText();
        }

        /// <summary>
        /// При включённой галочке «Одинаковые со всех сторон» протягивает
        /// значение любого изменённого поля на все остальные.
        /// </summary>
        private void MarginNumeric_ValueChanged(object sender, EventArgs e)
        {
            if (_suppressMarginSync) return;

            if (_chkSameMargins.Checked)
            {
                var src = (NumericUpDown)sender;
                decimal v = src.Value;

                _suppressMarginSync = true;
                if (!ReferenceEquals(src, _numMarginLeft)) _numMarginLeft.Value = v;
                if (!ReferenceEquals(src, _numMarginTop)) _numMarginTop.Value = v;
                if (!ReferenceEquals(src, _numMarginRight)) _numMarginRight.Value = v;
                if (!ReferenceEquals(src, _numMarginBottom)) _numMarginBottom.Value = v;
                _suppressMarginSync = false;
            }

            UpdatePreviewText();
        }

        // ====================================================================
        //  Открытие PDF
        // ====================================================================
        /// <summary>
        /// Спрашивает файл, открывает PdfDocument через PdfPageImageSource,
        /// обновляет UI (имя файла, границы диапазона, кнопки).
        /// </summary>
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

                _suppressRangeUpdate = true;
                _numFirstPage.Maximum = _pageCount;
                _numLastPage.Maximum = _pageCount;
                _numFirstPage.Value = 1;
                _numLastPage.Value = _pageCount;
                _suppressRangeUpdate = false;

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

        /// <summary>
        /// Переоткрывает PDF при смене бюджета кэша: PdfPageImageSource
        /// читает бюджет только в конструкторе.
        /// </summary>
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

        /// <summary>Возвращает бюджет кэша в байтах по выбору в комбобоксе.</summary>
        private long GetCacheBudgetBytes()
        {
            switch (_cmbCacheBudget.SelectedIndex)
            {
                case 0: return 0L;
                case 1: return 16L * 1024 * 1024;
                case 2: return 32L * 1024 * 1024;
                case 3: return 48L * 1024 * 1024;
                case 4: return 96L * 1024 * 1024;
                case 5: return 192L * 1024 * 1024;
                default: return 48L * 1024 * 1024;
            }
        }

        /// <summary>
        /// Очищает кэш страниц и принудительно запускает GC,
        /// чтобы GDI+ вернул нативные буферы ОС.
        /// </summary>
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
        //  Сводка
        // ====================================================================
        /// <summary>
        /// Обновляет многострочную сводку под группами: параметры,
        /// итоговое число сторон A4 с учётом диапазона, состояние кэша.
        /// </summary>
        private void UpdatePreviewText()
        {
            if (_source == null || _pageCount == 0)
            {
                _lblPreview.Text = string.Empty;
                return;
            }

            string sheetFormat = _cmbOrientation.SelectedIndex == 1
                ? "A4 альбомный" : "A4 книжный";

            string cellMode;
            switch (_cmbCellOrientation.SelectedIndex)
            {
                case 1: cellMode = "вертикально"; break;
                case 2: cellMode = "горизонтально"; break;
                default: cellMode = "авто"; break;
            }

            string edge = _cmbDuplexEdge.SelectedIndex == 0
                ? "длинный край" : "короткий край";

            double padMm = (double)_numPadding.Value;
            double betweenMm = padMm * 2.0;

            string marginsText;
            if (_chkSameMargins.Checked)
            {
                marginsText = ((double)_numMarginLeft.Value).ToString("0.0") +
                              " мм со всех сторон";
            }
            else
            {
                marginsText =
                    "Л " + ((double)_numMarginLeft.Value).ToString("0.0") +
                    ", П " + ((double)_numMarginRight.Value).ToString("0.0") +
                    ", В " + ((double)_numMarginTop.Value).ToString("0.0") +
                    ", Н " + ((double)_numMarginBottom.Value).ToString("0.0") + " мм";
            }

            double bx = (double)_numBackOffsetX.Value;
            double by = (double)_numBackOffsetY.Value;

            int sheetsSelected;
            using (var doc = CreateDocument(_chkDuplex.Checked, _cmbDuplexEdge.SelectedIndex == 0))
            {
                sheetsSelected = doc.SheetCount;
            }

            string rangeText;
            if (_chkAllPages.Checked)
                rangeText = "все страницы (1—" + _pageCount + ")";
            else
                rangeText = "страницы " + (int)_numFirstPage.Value +
                            "—" + (int)_numLastPage.Value;

            _lblPreview.Text =
                "PDF: " + Path.GetFileName(_pdfPath ?? "") +
                "  —  " + _pageCount + " стр.;  диапазон: " + rangeText + "\r\n" +
                "Формат листа: " + sheetFormat +
                ";  N = " + _cmbPagesPerSheet.SelectedItem +
                ";  поворот страниц: " + cellMode + "\r\n" +
                (_chkDuplex.Checked
                    ? "Дуплекс: включён, переворот по " + edge
                    : "Дуплекс: выключен") + "\r\n" +
                "К печати: " + sheetsSelected + " стор. A4;  " +
                "поля листа: " + marginsText + ";  " +
                "зазор: " + betweenMm.ToString("0.0") + " мм\r\n" +
                "Сдвиг оборота: X = " + bx.ToString("0.0") +
                " мм, Y = " + by.ToString("0.0") + " мм" +
                (_chkRegistrationMarks.Checked ? "  •  метки совмещения ВКЛ" : "") + "\r\n" +
                "Кэш страниц: " +
                (_source.CacheBytes / (1024.0 * 1024.0)).ToString("0.0") + " МБ";
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
        private double GetMarginLeftMm() => (double)_numMarginLeft.Value;
        private double GetMarginRightMm() => (double)_numMarginRight.Value;
        private double GetMarginTopMm() => (double)_numMarginTop.Value;
        private double GetMarginBottomMm() => (double)_numMarginBottom.Value;
        private bool GetSheetLandscape() => _cmbOrientation.SelectedIndex == 1;

        /// <summary>
        /// Собирает NUpPrintDocument из текущих настроек UI.
        /// Один и тот же метод используется и для предпросмотра, и для печати,
        /// поэтому геометрия обеих операций идентична.
        /// </summary>
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
                GetMarginLeftMm(),
                GetMarginRightMm(),
                GetMarginTopMm(),
                GetMarginBottomMm());

            doc.Landscape = GetSheetLandscape();

            if (_chkAllPages.Checked)
            {
                doc.FirstPage = 1;
                doc.LastPage = -1;
            }
            else
            {
                doc.FirstPage = (int)_numFirstPage.Value;
                doc.LastPage = (int)_numLastPage.Value;
            }

            doc.BackOffsetXMm = (double)_numBackOffsetX.Value;
            doc.BackOffsetYMm = (double)_numBackOffsetY.Value;
            doc.DrawRegistrationMarks = _chkRegistrationMarks.Checked;

            return doc;
        }

        // ====================================================================
        //  Предпросмотр
        // ====================================================================
        /// <summary>
        /// Открывает PreviewForm с текущими настройками. Рендеринг в 72 DPI —
        /// достаточно для экрана, в 4 раза меньше памяти, чем 150 DPI.
        /// После закрытия — принудительный GC для возврата GDI+ буферов.
        /// </summary>
        private void BtnPreview_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            const int previewDpi = 72;

            using (var doc = CreateDocument(
                       _chkDuplex.Checked,
                       _cmbDuplexEdge.SelectedIndex == 0))
            {
                if (doc.SheetCount == 0)
                {
                    MessageBox.Show(this,
                        "Нет листов для отображения в выбранном диапазоне.",
                        "Пусто", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var dlg = new PreviewForm(doc, _source, previewDpi, previewDpi))
                {
                    dlg.ShowDialog(this);
                }
            }

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            UpdatePreviewText();
        }

        // ====================================================================
        //  Печать
        // ====================================================================
        /// <summary>
        /// Открывает PrintDialog с готовым документом и отправляет его на печать.
        /// Перехватывает ExternalException (чаще всего — нехватка места под
        /// спулер печати) и показывает понятное сообщение вместо «generic error».
        /// </summary>
        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (_source == null) return;

            using (var doc = CreateDocument(
                       _chkDuplex.Checked,
                       _cmbDuplexEdge.SelectedIndex == 0))
            {
                if (doc.SheetCount == 0)
                {
                    MessageBox.Show(this,
                        "Нет листов для печати в выбранном диапазоне.",
                        "Пусто", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

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
                            " • принтер офлайн или недоступен.",
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
        //  Время жизни источника PDF
        // ====================================================================
        /// <summary>
        /// Освобождает PdfDocument и все закэшированные Bitmap-ы.
        /// Вызывается при закрытии формы и при переоткрытии PDF.
        /// </summary>
        private void DisposeSource()
        {
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            _pageCount = 0;
        }

        /// <summary>
        /// Гарантирует Dispose источника перед закрытием формы.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisposeSource();
            base.OnFormClosing(e);
        }
    }
}