using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace NUpPdfPrinter
{
    /// <summary>
    /// Главная форма приложения: список PDF-документов, N-up настройки,
    /// предпросмотр и печать.
    ///
    /// Компоновка: 1000 × 750, две колонки групп.
    /// Сверху во всю ширину — список PDF-файлов с кнопками управления.
    /// Слева — страницы, дуплекс, совмещение, сводка.
    /// Справа — раскладка листа, отступы, кэш.
    /// </summary>
    public sealed class MainForm : Form
    {
        // ====================================================================
        //  Состояние
        // ====================================================================
        private IPageImageSource _source;
        private readonly List<string> _pdfPaths = new List<string>();
        private int _pageCount;

        private bool _suppressRangeUpdate;
        private bool _suppressMarginSync;

        // ====================================================================
        //  UI-элементы
        // ====================================================================
        // Группа «Документы PDF»
        private ListBox _lstFiles;
        private Button _btnAddPdf;
        private Button _btnRemovePdf;
        private Button _btnMoveUp;
        private Button _btnMoveDown;
        private Button _btnClearFiles;
        private Label _lblFilesHint;

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
        private Label _lblDpi;
        private NumericUpDown _numDpi;
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
            Text = "N-up печать PDF (2 / 3 / 4 / 6 / 8 / 9 на листе A4)";
            ClientSize = new Size(1000, 750);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);

            BuildGroupFiles();

            // Левая колонка x=12, ширина 480
            BuildGroupPages();
            BuildGroupDuplex();
            BuildGroupAlign();

            // Правая колонка x=508, ширина 480
            BuildGroupLayout();
            BuildGroupMargins();
            BuildGroupCache();

            // Сводка (левая колонка, под группами)
            _lblPreview = new Label
            {
                Location = new Point(12, 467),
                Size = new Size(480, 220),
                AutoSize = false,
                ForeColor = Color.DimGray
            };
            Controls.Add(_lblPreview);

            // Кнопки — по центру внизу
            _btnPreview = new Button
            {
                Text = "Предпросмотр…",
                Location = new Point(270, 700),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPreview.Click += BtnPreview_Click;

            _btnExit = new Button
            {
                Text = "Выход",
                Location = new Point(430, 700),
                Size = new Size(140, 34)
            };
            _btnExit.Click += (s, e) => Close();

            _btnPrint = new Button
            {
                Text = "Печать…",
                Location = new Point(590, 700),
                Size = new Size(140, 34),
                Enabled = false
            };
            _btnPrint.Click += BtnPrint_Click;

            Controls.AddRange(new Control[] { _btnPreview, _btnExit, _btnPrint });
        }

        // ====================================================================
        //  Группа «Документы PDF» (12, 12, 976 × 160)
        // ====================================================================
        private void BuildGroupFiles()
        {
            var grp = new GroupBox
            {
                Text = "Документы PDF (порядок в списке = порядок страниц при печати)",
                Location = new Point(12, 12),
                Size = new Size(976, 160)
            };

            _lstFiles = new ListBox
            {
                Location = new Point(10, 22),
                Size = new Size(700, 125),
                SelectionMode = SelectionMode.One,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };
            _lstFiles.SelectedIndexChanged += (s, e) => UpdateFileButtons();
            _lstFiles.DoubleClick += (s, e) => BtnRemovePdf_Click(s, e);

            _btnAddPdf = new Button
            {
                Text = "Добавить PDF…",
                Location = new Point(720, 22),
                Size = new Size(90, 28)
            };
            _btnAddPdf.Click += BtnAddPdf_Click;

            _btnRemovePdf = new Button
            {
                Text = "Удалить",
                Location = new Point(815, 22),
                Size = new Size(90, 28),
                Enabled = false
            };
            _btnRemovePdf.Click += BtnRemovePdf_Click;

            _btnMoveUp = new Button
            {
                Text = "Вверх",
                Location = new Point(720, 55),
                Size = new Size(90, 28),
                Enabled = false
            };
            _btnMoveUp.Click += (s, e) => MoveSelectedFile(-1);

            _btnMoveDown = new Button
            {
                Text = "Вниз",
                Location = new Point(815, 55),
                Size = new Size(90, 28),
                Enabled = false
            };
            _btnMoveDown.Click += (s, e) => MoveSelectedFile(+1);

            _btnClearFiles = new Button
            {
                Text = "Очистить список",
                Location = new Point(720, 88),
                Size = new Size(185, 28),
                Enabled = false
            };
            _btnClearFiles.Click += BtnClearFiles_Click;

            _lblFilesHint = new Label
            {
                Text = "Двойной клик по файлу — удалить.\r\n" +
                            "Страницы нумеруются сквозным образом\r\n" +
                            "по порядку списка.",
                Location = new Point(720, 120),
                Size = new Size(200, 40),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8F)
            };

            grp.Controls.AddRange(new Control[]
            {
                _lstFiles,
                _btnAddPdf, _btnRemovePdf,
                _btnMoveUp, _btnMoveDown,
                _btnClearFiles, _lblFilesHint
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Страницы для печати» (12, 182, 480 × 65)
        // ====================================================================
        private void BuildGroupPages()
        {
            var grp = new GroupBox
            {
                Text = "Страницы для печати",
                Location = new Point(12, 182),
                Size = new Size(480, 65)
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
                Location = new Point(230, 25),
                AutoSize = true
            };

            _numLastPage = new NumericUpDown
            {
                Location = new Point(258, 21),
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

            var tip = new ToolTip();
            tip.SetToolTip(_numFirstPage, "Начальная страница (сквозная нумерация).");
            tip.SetToolTip(_numLastPage, "Конечная страница (сквозная нумерация).");

            grp.Controls.AddRange(new Control[]
            {
                _chkAllPages, _lblRangeFrom, _numFirstPage, _lblRangeTo, _numLastPage
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Двусторонняя печать» (12, 257, 480 × 85)
        // ====================================================================
        private void BuildGroupDuplex()
        {
            var grp = new GroupBox
            {
                Text = "Двусторонняя печать",
                Location = new Point(12, 257),
                Size = new Size(480, 85)
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

            var tip = new ToolTip();
            tip.SetToolTip(_cmbDuplexEdge,
                "Как принтер переворачивает лист.\r\n" +
                "Длинный край — «книжка», короткий край — «блокнот».");

            grp.Controls.AddRange(new Control[]
            {
                _chkDuplex, _lblEdge, _cmbDuplexEdge
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Совмещение сторон» (12, 352, 480 × 105)
        // ====================================================================
        private void BuildGroupAlign()
        {
            var grp = new GroupBox
            {
                Text = "Совмещение сторон",
                Location = new Point(12, 352),
                Size = new Size(480, 105)
            };

            _chkRegistrationMarks = new CheckBox
            {
                Text = "Метки совмещения (рамка и крест на обеих сторонах)",
                Location = new Point(10, 22),
                Size = new Size(460, 24),
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
                Location = new Point(120, 55),
                AutoSize = true
            };

            _numBackOffsetX = new NumericUpDown
            {
                Location = new Point(138, 53),
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
                Location = new Point(203, 55),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _lblBackOffsetY = new Label
            {
                Text = "Y",
                Location = new Point(240, 55),
                AutoSize = true
            };

            _numBackOffsetY = new NumericUpDown
            {
                Location = new Point(258, 53),
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
                Location = new Point(323, 55),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipOff = new ToolTip();
            tipOff.SetToolTip(_numBackOffsetX,
                "Сдвиг содержимого обратной стороны по X (мм). + вправо, − влево.");
            tipOff.SetToolTip(_numBackOffsetY,
                "Сдвиг содержимого обратной стороны по Y (мм). + вниз, − вверх.");

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
        //  Группа «Раскладка листа» (508, 182, 480 × 240)
        // ====================================================================
        private void BuildGroupLayout()
        {
            var grp = new GroupBox
            {
                Text = "Раскладка листа",
                Location = new Point(508, 182),
                Size = new Size(480, 240)
            };

            _lblDpi = new Label
            {
                Text = "DPI рендеринга:",
                Location = new Point(10, 22),
                AutoSize = true
            };

            _numDpi = new NumericUpDown
            {
                Location = new Point(170, 19),
                Size = new Size(80, 24),
                Minimum = 72,
                Maximum = 400,
                Value = 150,
                Increment = 50
            };

            _lblPagesPerSheet = new Label
            {
                Text = "Страниц на листе:",
                Location = new Point(10, 55),
                AutoSize = true
            };

            _cmbPagesPerSheet = new ComboBox
            {
                Location = new Point(170, 52),
                Size = new Size(100, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbPagesPerSheet.Items.AddRange(new object[] { 2, 3, 4, 6, 8, 9 });
            _cmbPagesPerSheet.SelectedIndex = 2; // 4
            _cmbPagesPerSheet.SelectedIndexChanged += (s, e) => UpdatePreviewText();

            _lblOrientation = new Label
            {
                Text = "Формат листа:",
                Location = new Point(10, 88),
                AutoSize = true
            };

            _cmbOrientation = new ComboBox
            {
                Location = new Point(170, 85),
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
                Location = new Point(10, 121),
                AutoSize = true
            };

            _cmbCellOrientation = new ComboBox
            {
                Location = new Point(170, 118),
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
                Location = new Point(10, 154),
                AutoSize = true
            };

            _numPadding = new NumericUpDown
            {
                Location = new Point(170, 151),
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
                Location = new Point(248, 154),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            var tipPad = new ToolTip();
            tipPad.SetToolTip(_numPadding,
                "Отступ вокруг каждой страницы внутри её ячейки.\r\n" +
                "Реальное расстояние между соседними страницами = 2 × значение.");

            grp.Controls.AddRange(new Control[]
            {
                _lblDpi, _numDpi,
                _lblPagesPerSheet, _cmbPagesPerSheet,
                _lblOrientation, _cmbOrientation,
                _lblCellOrientation, _cmbCellOrientation,
                _lblPadding, _numPadding, _lblPaddingMm
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Группа «Отступы от края A4» (508, 432, 480 × 160)
        // ====================================================================
        private void BuildGroupMargins()
        {
            var grp = new GroupBox
            {
                Text = "Отступы от края листа A4",
                Location = new Point(508, 432),
                Size = new Size(480, 160)
            };

            _chkSameMargins = new CheckBox
            {
                Text = "Одинаковые со всех сторон",
                Location = new Point(10, 22),
                Size = new Size(460, 24),
                Checked = true
            };
            _chkSameMargins.CheckedChanged += ChkSameMargins_CheckedChanged;

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
                Location = new Point(185, 58),
                AutoSize = true
            };
            _numMarginTop = new NumericUpDown
            {
                Location = new Point(250, 55),
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
                Location = new Point(320, 58),
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
                Location = new Point(185, 91),
                AutoSize = true
            };
            _numMarginBottom = new NumericUpDown
            {
                Location = new Point(250, 88),
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
                Location = new Point(320, 91),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            EventHandler sync = MarginNumeric_ValueChanged;
            _numMarginLeft.ValueChanged += sync;
            _numMarginTop.ValueChanged += sync;
            _numMarginRight.ValueChanged += sync;
            _numMarginBottom.ValueChanged += sync;

            var tip = new ToolTip();
            tip.SetToolTip(_chkSameMargins,
                "Если снять — можно задать разные отступы по сторонам.\r\n" +
                "Полезно, если принтер не может печатать ближе 5 мм к правому/нижнему краю.");
            tip.SetToolTip(_numMarginRight,
                "У большинства принтеров аппаратный предел ~5 мм справа.");
            tip.SetToolTip(_numMarginBottom,
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
        //  Группа «Кэш страниц» (508, 602, 480 × 85)
        // ====================================================================
        private void BuildGroupCache()
        {
            var grp = new GroupBox
            {
                Text = "Кэш страниц",
                Location = new Point(508, 602),
                Size = new Size(480, 85)
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
                if (_pdfPaths.Count > 0) RebuildSourceWithUiFeedback();
            };

            _btnClearCache = new Button
            {
                Text = "Очистить кэш",
                Location = new Point(320, 21),
                Size = new Size(140, 28),
                Enabled = false
            };
            _btnClearCache.Click += BtnClearCache_Click;

            var tip = new ToolTip();
            tip.SetToolTip(_cmbCacheBudget,
                "Сколько страниц PDF держать в памяти для быстрого листания.\r\n" +
                "Меньше — экономнее, но медленнее.");

            grp.Controls.AddRange(new Control[]
            {
                _lblCache, _cmbCacheBudget, _btnClearCache
            });
            Controls.Add(grp);
        }

        // ====================================================================
        //  Управление списком файлов
        // ====================================================================
        /// <summary>
        /// Добавляет один или несколько PDF-файлов в список.
        /// Может выбрать сразу несколько через MultiSelect.
        /// </summary>
        private void BtnAddPdf_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "PDF-файлы (*.pdf)|*.pdf|Все файлы (*.*)|*.*",
                Title = "Выберите один или несколько PDF-документов",
                Multiselect = true
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                foreach (var path in ofd.FileNames)
                {
                    // Дубликат по полному пути пропускаем.
                    if (_pdfPaths.Exists(p =>
                        string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _pdfPaths.Add(path);
                }

                RebuildSourceWithUiFeedback();
                RefreshFilesList();
            }
        }

        /// <summary>Удаляет выбранный файл из списка.</summary>
        private void BtnRemovePdf_Click(object sender, EventArgs e)
        {
            int idx = _lstFiles.SelectedIndex;
            if (idx < 0 || idx >= _pdfPaths.Count) return;

            _pdfPaths.RemoveAt(idx);
            RebuildSourceWithUiFeedback();
            RefreshFilesList();

            if (_pdfPaths.Count > 0)
                _lstFiles.SelectedIndex = Math.Min(idx, _pdfPaths.Count - 1);
        }

        /// <summary>Перемещает выделенный файл вверх (−1) или вниз (+1) по списку.</summary>
        private void MoveSelectedFile(int direction)
        {
            int idx = _lstFiles.SelectedIndex;
            if (idx < 0) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _pdfPaths.Count) return;

            var tmp = _pdfPaths[idx];
            _pdfPaths[idx] = _pdfPaths[newIdx];
            _pdfPaths[newIdx] = tmp;

            RebuildSourceWithUiFeedback();
            RefreshFilesList();
            _lstFiles.SelectedIndex = newIdx;
        }

        /// <summary>Полностью очищает список файлов.</summary>
        private void BtnClearFiles_Click(object sender, EventArgs e)
        {
            if (_pdfPaths.Count == 0) return;

            var r = MessageBox.Show(this,
                "Убрать все PDF из списка?",
                "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            _pdfPaths.Clear();
            RebuildSourceWithUiFeedback();
            RefreshFilesList();
        }

        /// <summary>
        /// Обновляет ListBox: имя файла, количество страниц, глобальный диапазон.
        /// </summary>
        private void RefreshFilesList()
        {
            _lstFiles.BeginUpdate();
            try
            {
                _lstFiles.Items.Clear();
                int startIndex = 0;

                foreach (var path in _pdfPaths)
                {
                    int n;
                    try
                    {
                        using (var doc = PdfiumViewer.PdfDocument.Load(path))
                            n = doc.PageCount;
                    }
                    catch
                    {
                        n = 0;
                    }

                    string range = n > 0
                        ? "  (" + (startIndex + 1) + "—" + (startIndex + n) + ")"
                        : "  (ошибка открытия)";

                    _lstFiles.Items.Add(
                        Path.GetFileName(path) + "  —  " + n + " стр." + range);

                    startIndex += n;
                }
            }
            finally
            {
                _lstFiles.EndUpdate();
                UpdateFileButtons();
            }
        }

        /// <summary>Включает/выключает кнопки управления файлами по состоянию списка.</summary>
        private void UpdateFileButtons()
        {
            int idx = _lstFiles.SelectedIndex;
            int count = _pdfPaths.Count;

            _btnRemovePdf.Enabled = idx >= 0;
            _btnMoveUp.Enabled = idx > 0;
            _btnMoveDown.Enabled = idx >= 0 && idx < count - 1;
            _btnClearFiles.Enabled = count > 0;
        }

        // ====================================================================
        //  Пересборка источника (после изменения списка файлов или бюджета кэша)
        // ====================================================================
        /// <summary>
        /// Пересобирает CompositePageImageSource по текущему _pdfPaths.
        /// Старый источник освобождается ТОЛЬКО после успешной сборки нового,
        /// чтобы при ошибке не потерять уже открытые файлы.
        /// </summary>
        private bool TryRebuildSource(out string error)
        {
            error = null;
            var newSource = new CompositePageImageSource();
            try
            {
                foreach (var path in _pdfPaths)
                {
                    var pdf = new PdfPageImageSource(path, GetCacheBudgetBytes());
                    newSource.Add(pdf);
                }
            }
            catch (Exception ex)
            {
                newSource.Dispose();
                error = ex.Message;
                return false;
            }

            DisposeSource();
            _source = newSource;
            _pageCount = newSource.PageCount;
            return true;
        }

        private void RebuildSourceWithUiFeedback()
        {
            if (_pdfPaths.Count == 0)
            {
                DisposeSource();
                _pageCount = 0;

                _btnPrint.Enabled = false;
                _btnPreview.Enabled = false;
                _btnClearCache.Enabled = false;

                _suppressRangeUpdate = true;
                _numFirstPage.Maximum = 1;
                _numLastPage.Maximum = 1;
                _numFirstPage.Value = 1;
                _numLastPage.Value = 1;
                _suppressRangeUpdate = false;

                _lblPreview.Text = string.Empty;
                return;
            }

            if (!TryRebuildSource(out var error))
            {
                MessageBox.Show(this,
                    "Не удалось открыть один или несколько PDF:\r\n" + error,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _btnPrint.Enabled = true;
            _btnPreview.Enabled = true;
            _btnClearCache.Enabled = true;

            _suppressRangeUpdate = true;
            _numFirstPage.Maximum = Math.Max(1, _pageCount);
            _numLastPage.Maximum = Math.Max(1, _pageCount);
            _numFirstPage.Value = 1;
            _numLastPage.Value = Math.Max(1, _pageCount);
            _suppressRangeUpdate = false;

            UpdatePreviewText();
        }

        // ====================================================================
        //  Обработчики отступов
        // ====================================================================
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
        //  Кэш
        // ====================================================================
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
                "Файлов: " + _pdfPaths.Count + ";  " +
                "всего страниц: " + _pageCount +
                ";  диапазон: " + rangeText + "\r\n" +
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