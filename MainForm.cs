using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LlamaVulkanLauncher
{
    internal sealed class MainForm : Form
    {
        // 實際色值集中在 Theme，可用 config\theme.xml 覆寫。
        private static readonly Color ColorHeader = Theme.Header;
        private static readonly Color ColorStart = Theme.Start;
        private static readonly Color ColorStop = Theme.Stop;
        private static readonly Color ColorOk = Theme.Ok;
        private static readonly Color ColorBad = Theme.Bad;
        private static readonly Color ColorMuted = Theme.Muted;
        private static readonly string[] CacheTypes = new string[]
        {
            "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1"
        };
        // 對應 llama-server --spec-type 的完整清單（b10488 官方說明）。
        private static readonly string[] SpecTypes = new string[]
        {
            "none", "draft-simple", "draft-eagle3", "draft-mtp", "draft-dflash", "draft-dspark",
            "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache"
        };

        /// <summary>共用的提示元件，讓被縮短的標題與說明仍可用滑鼠停留看到完整內容。</summary>
        private static readonly ToolTip SharedTips = CreateSharedTips();

        private static ToolTip CreateSharedTips()
        {
            ToolTip tips = new ToolTip();
            tips.AutoPopDelay = 20000;
            tips.InitialDelay = 400;
            tips.ReshowDelay = 120;
            tips.ShowAlways = true;
            return tips;
        }

        private readonly ServerProcess _server = new ServerProcess();
        private readonly Timer _healthTimer = new Timer();
        private AppState _state;
        private bool _loading;
        private bool _ready;
        private bool _healthPending;
        private bool _devicesPending;
        /// <summary>是否偵測到不是本啟動器啟動的 llama-server。</summary>
        private bool _externalRunning;

        private ComboBox _cboProfile;
        private PathField _exe;
        private PathField _model;
        private PathField _mmproj;
        private CheckBox _chkMmproj;
        private ComboBox _cboDevice;
        private ComboBox _cboCtx;
        private NumericUpDown _numGpuLayers;
        private ComboBox _cboKv;
        private ComboBox _cboFlash;
        private ComboBox _cboReasoning;
        private NumericUpDown _numUbatch;
        private CheckBox _chkNoMmap;
        private NumericUpDown _numImageTokens;
        private NumericUpDown _numCacheRam;
        private NumericUpDown _numParallel;
        private CheckBox _chkChatTemplate;
        private PathField _chatTemplate;
        private CheckBox _chkReasoningPreserve;
        private ServerCapabilities _caps;
        private CheckBox _chkSpec;
        private ComboBox _cboSpecType;
        private NumericUpDown _numDraftN;
        private NumericUpDown _numDraftP;
        private ComboBox _cboDraftKv;
        private TextBox _txtHost;
        private NumericUpDown _numPort;
        private NumericUpDown _numThreads;
        private NumericUpDown _numThreadsBatch;
        private ComboBox _cboPrio;
        private CheckBox _chkConsole;
        private TextBox _txtExtra;
        private Label _lblHwInfo;
        private Label _lblHwAdvice;
        private Label _lblThreadHint;
        private HardwareInfo _hardware;
        private TabControl _tabs;
        private TabPage _pageOptimize;
        private TextBox _txtCommand;
        private TextBox _txtLog;
        private Label _lblStatus;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnWeb;
        private Control[] _specControls;

        public MainForm()
        {
            Text = Strings.Get("App.Title");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 720);
            Size = new Size(1180, 840);
            Font = new Font("Segoe UI", 9.25f);
            BackColor = Theme.Window;
            KeyPreview = true;
            ApplyAppIcon();

            BuildUi();
            _healthTimer.Interval = 1000;
            _healthTimer.Tick += OnHealthTick;
            _server.OutputReceived += OnServerOutput;
            _server.Exited += OnServerExited;
            Load += OnFormLoad;
            Shown += OnFormShown;
            FormClosing += OnFormClosing;
            KeyDown += OnFormKeyDown;
        }

        private void BuildUi()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(12, 6);
            _tabs.TabPages.Add(BuildPathsTab());
            _tabs.TabPages.Add(BuildInferenceTab());
            _pageOptimize = BuildOptimizeTab();
            _tabs.TabPages.Add(_pageOptimize);
            _tabs.TabPages.Add(BuildSpecTab());
            _tabs.TabPages.Add(BuildServerTab());

            Panel command = BuildCommandPanel();
            command.Dock = DockStyle.Bottom;
            command.Height = 96;

            Panel log = BuildLogPanel();
            log.Dock = DockStyle.Fill;

            // 用分隔器讓使用者自行拉高設定區或紀錄區，
            // 參數較多的分頁不必一定得捲動才能看到全部欄位。
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterWidth = 6;
            split.Panel1MinSize = 260;
            split.Panel2MinSize = 90;
            split.Panel1.Controls.Add(_tabs);
            split.Panel2.Controls.Add(log);

            Panel profile = BuildProfileBar();
            profile.Dock = DockStyle.Top;
            profile.Height = 48;

            Panel header = BuildHeader();
            header.Dock = DockStyle.Top;
            header.Height = 60;

            Controls.Add(split);
            Controls.Add(command);
            Controls.Add(profile);
            Controls.Add(header);

            // 視窗尺寸底定後再設定分隔位置，避免建構期間超出範圍。
            Shown += delegate
            {
                try
                {
                    split.SplitterDistance = Math.Max(split.Panel1MinSize,
                        split.ClientSize.Height - 150);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("設定分隔位置失敗：" + ex.Message);
                }
            };
        }

        private Panel BuildHeader()
        {
            Panel panel = new Panel();
            panel.BackColor = ColorHeader;
            panel.Padding = new Padding(16, 10, 16, 10);

            Label title = new Label();
            title.AutoSize = true;
            title.Text = Strings.Get("App.Title");
            title.ForeColor = Theme.HeaderText;
            title.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
            title.Location = new Point(16, 8);

            Label sub = new Label();
            sub.AutoSize = true;
            sub.Text = Strings.Get("App.Subtitle");
            sub.ForeColor = Theme.HeaderSubText;
            sub.Location = new Point(18, 34);

            _lblStatus = new Label();
            _lblStatus.AutoSize = false;
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblStatus.ForeColor = Theme.HeaderSubText;
            _lblStatus.Text = "● " + Strings.Get("App.Status.Stopped");
            _lblStatus.TextAlign = ContentAlignment.MiddleRight;

            _btnWeb = MakeHeaderButton(Strings.Get("Button.WebUi"), Theme.Web, 120);
            _btnStop = MakeHeaderButton(Strings.Get("Button.Stop"), ColorStop, 80);
            _btnStart = MakeHeaderButton(Strings.Get("Button.Start"), ColorStart, 88);
            _btnWeb.Enabled = false;
            _btnStop.Enabled = false;
            _btnStart.Click += delegate { StartServer(); };
            _btnStop.Click += delegate { StopServer(); };
            _btnWeb.Click += delegate { OpenWebUi(); };

            // 右側元件改用由右往左的流動配置，寬度由內容決定，
            // 不必自己算座標，也不會因為按鈕文字變長而被視窗邊緣切掉。
            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.AutoSize = true;
            right.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            right.Padding = new Padding(0, 12, 4, 0);

            _btnWeb.Margin = new Padding(8, 0, 0, 0);
            _btnStop.Margin = new Padding(8, 0, 0, 0);
            _btnStart.Margin = new Padding(8, 0, 0, 0);
            _lblStatus.AutoSize = true;
            _lblStatus.Margin = new Padding(12, 7, 4, 0);

            right.Controls.Add(_btnWeb);
            right.Controls.Add(_btnStop);
            right.Controls.Add(_btnStart);
            right.Controls.Add(_lblStatus);

            panel.Controls.Add(right);
            panel.Controls.Add(title);
            panel.Controls.Add(sub);
            return panel;
        }

        /// <summary>
        /// 量測文字在預設字型下的寬度，用來決定按鈕需要多寬。
        /// </summary>
        private static int MeasureTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            using (Font font = new Font("Segoe UI", 9.25f))
            {
                return TextRenderer.MeasureText(text, font).Width;
            }
        }

        private static Button MakeHeaderButton(string text, Color back, int width)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Width = Math.Max(width, MeasureTextWidth(text) + 28);
            btn.Height = 32;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = back;
            btn.ForeColor = Theme.HeaderText;
            btn.Top = 14;
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private Panel BuildProfileBar()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Panel;
            panel.Padding = new Padding(12, 8, 12, 8);

            // 改用流動配置，按鈕與說明會依實際文字長度排列，換語言也不會互相重疊。
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = false;
            flow.AutoScroll = false;

            Label label = new Label();
            label.Text = Strings.Get("Label.Profile");
            label.AutoSize = true;
            label.Margin = new Padding(4, 9, 8, 0);

            _cboProfile = new ComboBox();
            _cboProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboProfile.Width = 340;
            _cboProfile.Margin = new Padding(0, 5, 12, 0);
            _cboProfile.SelectedIndexChanged += OnProfileSelected;

            Button save = MakeBarButton(Strings.Get("Button.Save"), 76);
            Button saveAs = MakeBarButton(Strings.Get("Button.SaveAs"), 76);
            Button delete = MakeBarButton(Strings.Get("Button.Delete"), 76);
            Button optimize = MakeBarButton(Strings.Get("Button.Optimize"), 116);
            save.Click += delegate { SaveCurrent(false); };
            saveAs.Click += delegate { SaveCurrent(true); };
            delete.Click += delegate { DeleteProfile(); };
            optimize.Click += delegate { OpenOptimizer(true, false); };

            Label hint = new Label();
            hint.AutoSize = true;
            hint.ForeColor = ColorMuted;
            hint.Text = Strings.Get("Label.Shortcuts");
            hint.Margin = new Padding(16, 9, 4, 0);

            flow.Controls.Add(label);
            flow.Controls.Add(_cboProfile);
            flow.Controls.Add(save);
            flow.Controls.Add(saveAs);
            flow.Controls.Add(delete);
            flow.Controls.Add(optimize);
            flow.Controls.Add(hint);
            panel.Controls.Add(flow);
            return panel;
        }

        private static Button MakeBarButton(string text, int width)
        {
            Button btn = new Button();
            btn.Text = text;
            // 依實際文字長度決定寬度，換語言或用較長的中文字也不會被截斷。
            btn.Width = Math.Max(width, MeasureTextWidth(text) + 24);
            btn.Height = 26;
            btn.UseVisualStyleBackColor = true;
            return btn;
        }

        private TabPage BuildPathsTab()
        {
            TabPage page = new TabPage(Strings.Get("Tab.Paths"));
            TableLayoutPanel table = CreateFormTable(7);
            _exe = AddPathRow(table, 0, Strings.Get("Field.LlamaServer"), "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
            _model = AddPathRow(table, 1, Strings.Get("Field.Model"), "GGUF 模型 (*.gguf)|*.gguf|所有檔案 (*.*)|*.*");
            _chkMmproj = new CheckBox();
            _chkMmproj.Text = Strings.Get("Check.UseMmproj");
            _chkMmproj.AutoSize = true;
            _chkMmproj.Checked = true;
            _chkMmproj.CheckedChanged += OnFieldChanged;
            table.Controls.Add(MakeCaption(Strings.Get("Field.Mmproj")), 0, 2);
            table.Controls.Add(_chkMmproj, 1, 2);
            _mmproj = AddPathRow(table, 3, Strings.Get("Field.MmprojFile"), "GGUF (*.gguf)|*.gguf|所有檔案 (*.*)|*.*");
            _model.TextBox.Leave += delegate { SuggestMmproj(); };

            _chkChatTemplate = new CheckBox();
            _chkChatTemplate.Text = Strings.Get("Check.UseChatTemplate");
            _chkChatTemplate.AutoSize = true;
            _chkChatTemplate.CheckedChanged += OnFieldChanged;
            AddLabeled(table, 4, Strings.Get("Field.ChatTemplate"),
                WithHint(_chkChatTemplate, Strings.Get("Hint.ChatTemplate")));

            _chatTemplate = AddPathRow(table, 5, Strings.Get("Field.ChatTemplateFile"),
                "Jinja 模板 (*.jinja)|*.jinja|所有檔案 (*.*)|*.*");

            _chkReasoningPreserve = new CheckBox();
            _chkReasoningPreserve.Text = Strings.Get("Check.ReasoningPreserve");
            _chkReasoningPreserve.AutoSize = true;
            _chkReasoningPreserve.CheckedChanged += OnFieldChanged;
            AddLabeled(table, 6, Strings.Get("Field.ReasoningPreserve"),
                WithHint(_chkReasoningPreserve, Strings.Get("Hint.ReasoningPreserve")));

            page.Controls.Add(MakeScrollHost(table));
            return page;
        }

        /// <summary>
        /// 套用羊駝圖示。優先讀取內嵌資源，失敗時退回執行檔本身的圖示，
        /// 兩者都取不到就維持 WinForms 預設，不影響程式啟動。
        /// </summary>
        private void ApplyAppIcon()
        {
            try
            {
                System.Reflection.Assembly asm = typeof(MainForm).Assembly;
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (!names[i].EndsWith("llama.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using (System.IO.Stream stream = asm.GetManifestResourceStream(names[i]))
                    {
                        if (stream != null)
                        {
                            Icon = new Icon(stream);
                            return;
                        }
                    }
                }

                Icon extracted = Icon.ExtractAssociatedIcon(asm.Location);
                if (extracted != null)
                {
                    Icon = extracted;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("套用應用程式圖示失敗：" + ex.Message);
            }
        }

        /// <summary>
        /// 把控制項和一行灰色建議說明併在同一列，讓新手知道每個參數該填什麼。
        /// </summary>
        private static Panel WithHint(Control control, string hint)
        {
            Panel row = new Panel();
            row.Dock = DockStyle.Fill;

            // 說明文字填滿剩餘寬度並置中對齊，視窗變窄時會自動縮短而不是被裁掉。
            Label label = new Label();
            label.Dock = DockStyle.Fill;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.ForeColor = ColorMuted;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(10, 0, 0, 0);
            label.Text = hint;
            SharedTips.SetToolTip(label, hint);

            int width = control.Width > 0 ? control.Width : 150;
            control.Dock = DockStyle.Left;
            control.Width = width;
            SharedTips.SetToolTip(control, hint);

            // 先加入 Fill 再加入 Left，Dock 才會依正確順序配置。
            row.Controls.Add(label);
            row.Controls.Add(control);
            return row;
        }

        /// <summary>取出 WithHint 產生的說明標籤，供之後動態更新文字。</summary>
        private static Label FindHintLabel(Control row)
        {
            if (row == null)
            {
                return null;
            }
            for (int i = 0; i < row.Controls.Count; i++)
            {
                Label label = row.Controls[i] as Label;
                if (label != null)
                {
                    return label;
                }
            }
            return null;
        }

        private TabPage BuildInferenceTab()
        {
            TabPage page = new TabPage(Strings.Get("Tab.Inference"));
            TableLayoutPanel table = CreateFormTable(11);

            Panel deviceRow = new Panel();
            deviceRow.Dock = DockStyle.Fill;
            _cboDevice = MakeCombo(true, new string[] { "Vulkan0" });
            _cboDevice.Dock = DockStyle.Fill;
            Button refresh = MakeBarButton(Strings.Get("Button.Detect"), 96);
            refresh.Dock = DockStyle.Right;
            refresh.Click += delegate { RefreshDevices(true); };
            deviceRow.Controls.Add(_cboDevice);
            deviceRow.Controls.Add(refresh);
            AddLabeled(table, 0, Strings.Get("Field.Device"), deviceRow);

            _cboCtx = MakeCombo(true, new string[] { "4096", "8192", "16384", "32768", "65536", "131072" });
            AddLabeled(table, 1, Strings.Get("Field.ContextSize"), WithHint(_cboCtx, Strings.Get("Hint.ContextSize")));
            _numGpuLayers = MakeNum(0, 999, 99, 0);
            AddLabeled(table, 2, Strings.Get("Field.GpuLayers"), WithHint(_numGpuLayers, Strings.Get("Hint.GpuLayers")));
            _cboKv = MakeCombo(true, CacheTypes);
            AddLabeled(table, 3, Strings.Get("Field.KvCache"), WithHint(_cboKv, Strings.Get("Hint.KvCache")));
            _cboFlash = MakeCombo(false, new string[] { "on", "off", "auto" });
            AddLabeled(table, 4, Strings.Get("Field.FlashAttn"), WithHint(_cboFlash, Strings.Get("Hint.FlashAttn")));
            _cboReasoning = MakeCombo(false, new string[] { "off", "on", "auto" });
            AddLabeled(table, 5, Strings.Get("Field.Reasoning"), WithHint(_cboReasoning, Strings.Get("Hint.Reasoning")));
            _numUbatch = MakeNum(1, 4096, 256, 0);
            AddLabeled(table, 6, Strings.Get("Field.Ubatch"), WithHint(_numUbatch, Strings.Get("Hint.Ubatch")));
            _chkNoMmap = new CheckBox();
            _chkNoMmap.Text = Strings.Get("Check.NoMmap");
            _chkNoMmap.AutoSize = true;
            _chkNoMmap.CheckedChanged += OnFieldChanged;
            AddLabeled(table, 7, Strings.Get("Field.LoadMode"), _chkNoMmap);
            _numImageTokens = MakeNum(0, 100000, 1024, 0);
            AddLabeled(table, 8, Strings.Get("Field.ImageMinTokens"),
                WithHint(_numImageTokens, Strings.Get("Hint.ImageMinTokens")));

            _numCacheRam = MakeNum(-1, 262144, LaunchProfile.DefaultCacheRam, 0);
            AddLabeled(table, 9, Strings.Get("Field.CacheRam"), WithHint(_numCacheRam, Strings.Get("Hint.CacheRam")));

            _numParallel = MakeNum(1, 64, LaunchProfile.DefaultParallel, 0);
            AddLabeled(table, 10, Strings.Get("Field.Parallel"), WithHint(_numParallel, Strings.Get("Hint.Parallel")));

            page.Controls.Add(MakeScrollHost(table));
            return page;
        }

        private TabPage BuildSpecTab()
        {
            TabPage page = new TabPage(Strings.Get("Tab.Spec"));
            TableLayoutPanel table = CreateFormTable(5);
            _chkSpec = new CheckBox();
            _chkSpec.Text = Strings.Get("Check.EnableSpec");
            _chkSpec.AutoSize = true;
            _chkSpec.CheckedChanged += OnFieldChanged;
            AddLabeled(table, 0, Strings.Get("Field.SpecEnable"), _chkSpec);
            _cboSpecType = MakeCombo(true, SpecTypes);
            AddLabeled(table, 1, Strings.Get("Field.SpecType"), WithHint(_cboSpecType, Strings.Get("Hint.SpecType")));
            _numDraftN = MakeNum(0, 32, 2, 0);
            AddLabeled(table, 2, Strings.Get("Field.SpecDraftNMax"),
                WithHint(_numDraftN, Strings.Get("Hint.SpecDraftNMax")));
            _numDraftP = MakeNum(0, 1, 0.10m, 2);
            _numDraftP.Increment = 0.05m;
            AddLabeled(table, 3, Strings.Get("Field.SpecDraftPMin"),
                WithHint(_numDraftP, Strings.Get("Hint.SpecDraftPMin")));
            _cboDraftKv = MakeCombo(true, CacheTypes);
            AddLabeled(table, 4, Strings.Get("Field.DraftKv"), WithHint(_cboDraftKv, Strings.Get("Hint.DraftKv")));
            _specControls = new Control[] { _cboSpecType, _numDraftN, _numDraftP, _cboDraftKv };
            page.Controls.Add(MakeScrollHost(table));
            return page;
        }

        private TabPage BuildOptimizeTab()
        {
            TabPage page = new TabPage(Strings.Get("Tab.Optimize"));
            TableLayoutPanel table = CreateFormTable(6);
            table.RowStyles[0] = new RowStyle(SizeType.Absolute, 104f);
            table.RowStyles[1] = new RowStyle(SizeType.Absolute, 120f);
            table.RowStyles[2] = new RowStyle(SizeType.Absolute, 44f);

            // 這兩段文字較長，改為靠上對齊並允許多行，避免內容被切掉。
            _lblHwInfo = new Label();
            _lblHwInfo.Dock = DockStyle.Fill;
            _lblHwInfo.TextAlign = ContentAlignment.TopLeft;
            _lblHwInfo.Padding = new Padding(0, 6, 8, 0);
            AddLabeled(table, 0, Strings.Get("Field.HardwareInfo"), _lblHwInfo);

            _lblHwAdvice = new Label();
            _lblHwAdvice.Dock = DockStyle.Fill;
            _lblHwAdvice.TextAlign = ContentAlignment.TopLeft;
            _lblHwAdvice.Padding = new Padding(0, 6, 8, 0);
            AddLabeled(table, 1, Strings.Get("Field.FirstAdvice"), _lblHwAdvice);

            // 按鈕改用流動配置，換語言後不會因文字變長而互相重疊。
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            Button office = MakeBarButton(Strings.Get("Button.Office"), 136);
            Button perf = MakeBarButton(Strings.Get("Button.Performance"), 104);
            Button refresh = MakeBarButton(Strings.Get("Button.Refresh"), 96);
            office.Margin = new Padding(0, 4, 8, 0);
            perf.Margin = new Padding(0, 4, 8, 0);
            refresh.Margin = new Padding(0, 4, 0, 0);
            office.Click += delegate { OpenOptimizer(true, false); };
            perf.Click += delegate { OpenOptimizer(false, false); };
            refresh.Click += delegate { RefreshHardware(true); };
            actions.Controls.Add(office);
            actions.Controls.Add(perf);
            actions.Controls.Add(refresh);
            AddLabeled(table, 2, Strings.Get("Field.QuickApply"), actions);

            // 執行緒提示會隨硬體偵測更新，因此保留欄位參考並沿用共用的說明版面。
            _numThreads = MakeNum(-1, 256, -1, 0);
            Panel threadRow = WithHint(_numThreads, Strings.Get("Hint.Threads"));
            _lblThreadHint = FindHintLabel(threadRow);
            AddLabeled(table, 3, Strings.Get("Field.Threads"), threadRow);

            _numThreadsBatch = MakeNum(-1, 256, -1, 0);
            AddLabeled(table, 4, Strings.Get("Field.ThreadsBatch"),
                WithHint(_numThreadsBatch, Strings.Get("Hint.ThreadsBatch")));

            _cboPrio = MakeCombo(false, new string[]
            {
                "省略（llama 預設）",
                "low（辦公建議）",
                "normal",
                "medium",
                "high"
            });
            _cboPrio.SelectedIndex = 0;
            AddLabeled(table, 5, Strings.Get("Field.Priority"), _cboPrio);

            page.Controls.Add(MakeScrollHost(table));
            return page;
        }

        private TabPage BuildServerTab()
        {
            TabPage page = new TabPage(Strings.Get("Tab.Server"));
            TableLayoutPanel table = CreateFormTable(4);
            _txtHost = new TextBox();
            _txtHost.Dock = DockStyle.Fill;
            _txtHost.TextChanged += OnFieldChanged;
            AddLabeled(table, 0, Strings.Get("Field.Host"), _txtHost);
            _numPort = MakeNum(1, 65535, 8080, 0);
            AddLabeled(table, 1, Strings.Get("Field.Port"), _numPort);
            _chkConsole = new CheckBox();
            _chkConsole.Text = Strings.Get("Check.ShowConsole");
            _chkConsole.AutoSize = true;
            _chkConsole.CheckedChanged += OnFieldChanged;
            AddLabeled(table, 2, Strings.Get("Field.Console"), _chkConsole);
            _txtExtra = new TextBox();
            _txtExtra.Dock = DockStyle.Fill;
            _txtExtra.Multiline = true;
            _txtExtra.ScrollBars = ScrollBars.Vertical;
            _txtExtra.TextChanged += OnFieldChanged;
            // 表格改為自動高度後不能再用百分比，改給額外參數一個固定的多行高度。
            table.RowStyles[3] = new RowStyle(SizeType.Absolute, 140f);
            AddLabeled(table, 3, Strings.Get("Field.ExtraArgs"), _txtExtra);
            page.Controls.Add(MakeScrollHost(table));
            return page;
        }

        private Panel BuildCommandPanel()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Panel;
            panel.Padding = new Padding(12, 8, 12, 8);

            Label label = new Label();
            label.Text = Strings.Get("Label.CommandPreview");
            label.AutoSize = true;
            label.Location = new Point(12, 8);

            Button copy = MakeBarButton(Strings.Get("Button.CopyCommand"), 92);
            Button exportBat = MakeBarButton(Strings.Get("Button.ExportBat"), 92);
            copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            exportBat.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copy.Click += delegate { CopyCommand(); };
            exportBat.Click += delegate { ExportBat(); };

            _txtCommand = new TextBox();
            _txtCommand.Multiline = true;
            _txtCommand.ReadOnly = true;
            _txtCommand.ScrollBars = ScrollBars.Vertical;
            _txtCommand.Font = new Font("Consolas", 8.75f);
            _txtCommand.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _txtCommand.Location = new Point(12, 30);
            _txtCommand.Size = new Size(800, 56);

            panel.Controls.Add(label);
            panel.Controls.Add(copy);
            panel.Controls.Add(exportBat);
            panel.Controls.Add(_txtCommand);
            panel.Resize += delegate
            {
                exportBat.Left = panel.ClientSize.Width - exportBat.Width - 12;
                exportBat.Top = 6;
                copy.Left = exportBat.Left - copy.Width - 6;
                copy.Top = 6;
                _txtCommand.Width = panel.ClientSize.Width - 24;
            };
            return panel;
        }

        private Panel BuildLogPanel()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.LogBack;
            panel.Padding = new Padding(8);

            Label label = new Label();
            label.Text = Strings.Get("Label.Log");
            label.ForeColor = Theme.HeaderSubText;
            label.AutoSize = true;
            label.Location = new Point(10, 6);

            Button clear = MakeBarButton(Strings.Get("Button.Clear"), 64);
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { _txtLog.Clear(); };

            _txtLog = new TextBox();
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Both;
            _txtLog.WordWrap = false;
            _txtLog.BorderStyle = BorderStyle.None;
            _txtLog.BackColor = Theme.LogBack;
            _txtLog.ForeColor = Theme.LogText;
            _txtLog.Font = new Font("Consolas", 8.75f);
            _txtLog.Dock = DockStyle.Fill;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 28;
            top.BackColor = Theme.LogBack;
            top.Controls.Add(label);
            top.Controls.Add(clear);
            top.Resize += delegate { clear.Left = top.ClientSize.Width - clear.Width - 6; clear.Top = 2; };

            panel.Controls.Add(_txtLog);
            panel.Controls.Add(top);
            return panel;
        }

        private PathField AddPathRow(TableLayoutPanel table, int row, string caption, string filter)
        {
            PathField field = new PathField();
            field.TextBox = new TextBox();
            field.TextBox.Dock = DockStyle.Fill;
            field.TextBox.TextChanged += OnFieldChanged;
            field.Browse = MakeBarButton(Strings.Get("Button.Browse"), 72);
            field.Browse.Dock = DockStyle.Right;
            field.Status = new Label();
            field.Status.AutoSize = false;
            field.Status.Dock = DockStyle.Right;
            field.Status.Width = 150;
            field.Status.TextAlign = ContentAlignment.MiddleLeft;
            field.Status.ForeColor = ColorMuted;
            field.Filter = filter;
            field.Browse.Click += delegate { BrowseFile(field); };

            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Controls.Add(field.TextBox);
            host.Controls.Add(field.Status);
            host.Controls.Add(field.Browse);
            AddLabeled(table, row, caption, host);
            return field;
        }

        /// <summary>左側標題欄寬度。中文標題加上參數名稱較長，需要足夠空間。</summary>
        private const int CaptionColumnWidth = 240;

        /// <summary>一般欄位的列高。說明文字與欄位同列，因此不需要額外高度。</summary>
        private const int FormRowHeight = 36;

        private static TableLayoutPanel CreateFormTable(int rows)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            // 由內容決定高度並靠上停駐，外層捲動容器才知道實際需要多少空間。
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.Padding = new Padding(16, 14, 16, 14);
            table.ColumnCount = 2;
            table.RowCount = rows;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CaptionColumnWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < rows; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, FormRowHeight));
            }
            return table;
        }

        /// <summary>
        /// 把表格放進可捲動的容器，欄位比視窗高時會出現捲軸而不是被裁掉。
        /// </summary>
        private static Panel MakeScrollHost(Control content)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.Controls.Add(content);
            return host;
        }

        private static Label MakeCaption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(0, 0, 8, 0);
            // 標題若仍放不下就顯示省略號，並用提示顯示完整文字，不會被硬生生截斷。
            label.AutoEllipsis = true;
            SharedTips.SetToolTip(label, text);
            return label;
        }

        private void AddLabeled(TableLayoutPanel table, int row, string caption, Control field)
        {
            table.Controls.Add(MakeCaption(caption), 0, row);
            field.Dock = DockStyle.Fill;
            table.Controls.Add(field, 1, row);
        }

        private ComboBox MakeCombo(bool editable, string[] items)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            combo.Items.AddRange(items);
            combo.Dock = DockStyle.Fill;
            combo.TextChanged += OnFieldChanged;
            combo.SelectedIndexChanged += OnFieldChanged;
            return combo;
        }

        private NumericUpDown MakeNum(decimal min, decimal max, decimal value, int decimals)
        {
            NumericUpDown num = new NumericUpDown();
            num.Minimum = min;
            num.Maximum = max;
            num.DecimalPlaces = decimals;
            num.Value = value;
            num.Dock = DockStyle.Left;
            num.Width = 140;
            num.ThousandsSeparator = false;
            num.ValueChanged += OnFieldChanged;
            return num;
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            _state = ProfileStore.LoadOrCreate();
            string loadError = ProfileStore.LastLoadError;
            string migrationNote = ProfileStore.LastMigrationNote;
            ReloadProfileCombo(_state.ActiveProfileName);
            ApplySelectedProfile();
            SetStatus("已停止", ColorMuted, false);
            RefreshHardware(false);
            RefreshCapabilities(_exe.TextBox.Text.Trim());
            AppendLog("設定已載入。裝置清單請按「偵測裝置」；初次建議在「本機最佳化」。");
            _ready = true;
            UpdatePreview();
            UpdateFileStatus();
            UpdateHardwareAdvice();

            AppendLog(Strings.Format("Msg.SettingsPath", ProfileStore.GetFilePath()));
            if (!string.IsNullOrEmpty(VulkanFix.AppliedIcd))
            {
                AppendLog("[i] " + Strings.Get("Msg.VulkanFixed"));
            }
            EnsureCustomizationTemplates();
            // 上一次啟動器若被強制關閉，llama-server 可能還活著，這裡接管它。
            UpdateExternalServerState();
            if (!string.IsNullOrEmpty(migrationNote))
            {
                AppendLog("[i] " + migrationNote);
            }

            if (!string.IsNullOrEmpty(loadError))
            {
                AppendLog("[X] " + loadError);
                MessageBox.Show(this, loadError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            Shown -= OnFormShown;
            if (_state == null || _state.OptimizerOffered)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                OpenOptimizer(true, true);
            }));
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                SaveCurrentIntoState();
                ProfileStore.Save(_state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("關閉時儲存設定失敗：" + ex.Message);
                MessageBox.Show(this, "設定沒有存檔成功：" + Environment.NewLine + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (!_server.IsRunning && ServerProcess.FindOrphanProcesses().Length > 0)
            {
                // 關閉前提醒使用者仍有服務在背景執行，避免下次啟動被連接埠卡住。
                DialogResult keep = MessageBox.Show(
                    this,
                    Strings.Get("Msg.ConfirmStopExternal"),
                    Text,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (keep == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (keep == DialogResult.Yes)
                {
                    ServerProcess.StopAllServers();
                }
            }

            if (_server.IsRunning)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "llama-server 仍在執行，要一併停止並關閉嗎？",
                    Text,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == DialogResult.Yes)
                {
                    _server.Stop();
                }
            }

            _healthTimer.Stop();
            _server.Dispose();
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && e.Shift)
            {
                StopServer();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5)
            {
                StartServer();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                SaveCurrent(false);
                e.Handled = true;
            }
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading || !_ready)
            {
                return;
            }
            SaveCurrentIntoState();
            ApplySelectedProfile();
        }

        private void OnFieldChanged(object sender, EventArgs e)
        {
            if (_loading || !_ready)
            {
                return;
            }
            if (_chkMmproj != null && _mmproj != null)
            {
                _mmproj.TextBox.Enabled = _chkMmproj.Checked;
                _mmproj.Browse.Enabled = _chkMmproj.Checked;
            }
            if (_chkChatTemplate != null && _chatTemplate != null)
            {
                _chatTemplate.TextBox.Enabled = _chkChatTemplate.Checked;
                _chatTemplate.Browse.Enabled = _chkChatTemplate.Checked;
            }
            SetSpecEnabled(_chkSpec != null && _chkSpec.Checked);
            UpdatePreview();
            UpdateFileStatus();
            UpdateHardwareAdvice();
        }

        private void ReloadProfileCombo(string selectName)
        {
            _loading = true;
            _cboProfile.Items.Clear();
            for (int i = 0; i < _state.Profiles.Length; i++)
            {
                _cboProfile.Items.Add(_state.Profiles[i].Name);
            }
            int index = 0;
            if (!string.IsNullOrEmpty(selectName))
            {
                int found = _cboProfile.FindStringExact(selectName);
                if (found >= 0)
                {
                    index = found;
                }
            }
            if (_cboProfile.Items.Count > 0)
            {
                _cboProfile.SelectedIndex = index;
            }
            _loading = false;
        }

        private LaunchProfile CurrentProfileOrNull()
        {
            if (_cboProfile.SelectedIndex < 0 || _state == null || _state.Profiles == null)
            {
                return null;
            }
            string name = _cboProfile.SelectedItem as string;
            return FindProfile(name);
        }

        private LaunchProfile FindProfile(string name)
        {
            if (_state == null || _state.Profiles == null)
            {
                return null;
            }
            for (int i = 0; i < _state.Profiles.Length; i++)
            {
                if (string.Equals(_state.Profiles[i].Name, name, StringComparison.Ordinal))
                {
                    return _state.Profiles[i];
                }
            }
            return null;
        }

        private void ApplySelectedProfile()
        {
            LaunchProfile profile = CurrentProfileOrNull();
            if (profile == null)
            {
                return;
            }
            _loading = true;
            _state.ActiveProfileName = profile.Name;
            _exe.TextBox.Text = profile.LlamaServerPath;
            _model.TextBox.Text = profile.ModelPath;
            _mmproj.TextBox.Text = profile.MmprojPath;
            _chkMmproj.Checked = profile.UseMmproj;
            // 設定檔存的是純代號，需對映回帶顯示卡名稱的選項。
            SelectDeviceById(ExtractDeviceId(profile.Device));
            SetCombo(_cboCtx, profile.ContextSize.ToString());
            _numGpuLayers.Value = Clamp(_numGpuLayers, profile.GpuLayers);
            SetCombo(_cboKv, profile.KvCacheType);
            SetCombo(_cboFlash, profile.FlashAttn);
            SetCombo(_cboReasoning, profile.Reasoning);
            _numUbatch.Value = Clamp(_numUbatch, profile.UbatchSize);
            _chkNoMmap.Checked = profile.NoMmap;
            _numImageTokens.Value = Clamp(_numImageTokens, profile.ImageMinTokens);
            _chkSpec.Checked = profile.EnableSpeculative;
            SetCombo(_cboSpecType, profile.SpecType);
            _numDraftN.Value = Clamp(_numDraftN, profile.SpecDraftNMax);
            _numDraftP.Value = Clamp(_numDraftP, profile.SpecDraftPMin);
            SetCombo(_cboDraftKv, profile.DraftKvType);
            _txtHost.Text = profile.Host;
            _numPort.Value = Clamp(_numPort, profile.Port);
            _numThreads.Value = Clamp(_numThreads, profile.Threads);
            _numThreadsBatch.Value = Clamp(_numThreadsBatch, profile.ThreadsBatch);
            SetPrioCombo(profile.ProcessPrio);
            _chkConsole.Checked = profile.ShowConsole;
            _txtExtra.Text = profile.ExtraArgs ?? "";
            _numCacheRam.Value = Clamp(_numCacheRam, profile.CacheRam);
            _numParallel.Value = Clamp(_numParallel, profile.Parallel);
            _chkChatTemplate.Checked = profile.UseChatTemplate;
            _chatTemplate.TextBox.Text = profile.ChatTemplateFile ?? "";
            _chkReasoningPreserve.Checked = profile.ReasoningPreserve;
            _mmproj.TextBox.Enabled = profile.UseMmproj;
            _mmproj.Browse.Enabled = profile.UseMmproj;
            _chatTemplate.TextBox.Enabled = profile.UseChatTemplate;
            _chatTemplate.Browse.Enabled = profile.UseChatTemplate;
            SetSpecEnabled(profile.EnableSpeculative);
            _loading = false;
            UpdatePreview();
            UpdateFileStatus();
        }

        private LaunchProfile ReadUi()
        {
            LaunchProfile profile = new LaunchProfile();
            LaunchProfile current = CurrentProfileOrNull();
            profile.Name = current != null ? current.Name : "未命名";
            profile.LlamaServerPath = _exe.TextBox.Text.Trim();
            profile.ModelPath = _model.TextBox.Text.Trim();
            profile.MmprojPath = _mmproj.TextBox.Text.Trim();
            profile.UseMmproj = _chkMmproj.Checked;
            // 選單會顯示顯示卡名稱，這裡只取代號，避免名稱被塞進 --device。
            profile.Device = ExtractDeviceId(_cboDevice.Text);
            profile.ContextSize = ParseInt(_cboCtx.Text, LaunchProfile.DefaultContextSize);
            profile.GpuLayers = (int)_numGpuLayers.Value;
            profile.KvCacheType = _cboKv.Text.Trim();
            profile.FlashAttn = _cboFlash.Text.Trim();
            profile.Reasoning = _cboReasoning.Text.Trim();
            profile.UbatchSize = (int)_numUbatch.Value;
            profile.NoMmap = _chkNoMmap.Checked;
            profile.ImageMinTokens = (int)_numImageTokens.Value;
            profile.EnableSpeculative = _chkSpec.Checked;
            profile.SpecType = _cboSpecType.Text.Trim();
            profile.SpecDraftNMax = (int)_numDraftN.Value;
            profile.SpecDraftPMin = _numDraftP.Value;
            profile.DraftKvType = _cboDraftKv.Text.Trim();
            profile.Host = _txtHost.Text.Trim();
            profile.Port = (int)_numPort.Value;
            profile.Threads = (int)_numThreads.Value;
            profile.ThreadsBatch = (int)_numThreadsBatch.Value;
            profile.ProcessPrio = ReadPrioCombo();
            profile.ShowConsole = _chkConsole.Checked;
            profile.ExtraArgs = _txtExtra.Text.Trim();
            profile.CacheRam = (int)_numCacheRam.Value;
            profile.Parallel = (int)_numParallel.Value;
            profile.UseChatTemplate = _chkChatTemplate.Checked;
            profile.ChatTemplateFile = _chatTemplate.TextBox.Text.Trim();
            profile.ReasoningPreserve = _chkReasoningPreserve.Checked;
            return profile;
        }

        private void SaveCurrentIntoState()
        {
            LaunchProfile edited = ReadUi();
            LaunchProfile existing = FindProfile(edited.Name);
            if (existing == null)
            {
                List<LaunchProfile> list = new List<LaunchProfile>(_state.Profiles);
                list.Add(edited);
                _state.Profiles = list.ToArray();
            }
            else
            {
                edited.CopySettingsTo(existing);
            }
            _state.ActiveProfileName = edited.Name;
        }

        private void SaveCurrent(bool saveAs)
        {
            LaunchProfile edited = ReadUi();
            if (saveAs)
            {
                string name = PromptName("另存設定檔", edited.Name + " 複本");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                if (FindProfile(name) != null)
                {
                    MessageBox.Show(this, "已有同名設定檔。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                edited.Name = name.Trim();
                List<LaunchProfile> list = new List<LaunchProfile>(_state.Profiles);
                list.Add(edited);
                _state.Profiles = list.ToArray();
                ProfileStore.Save(_state);
                ReloadProfileCombo(edited.Name);
                ApplySelectedProfile();
                AppendLog("已另存設定檔：" + edited.Name);
                return;
            }

            SaveCurrentIntoState();
            ProfileStore.Save(_state);
            AppendLog("已儲存設定檔：" + edited.Name);
        }

        private void DeleteProfile()
        {
            LaunchProfile current = CurrentProfileOrNull();
            if (current == null)
            {
                return;
            }
            if (MessageBox.Show(this, "確定刪除設定檔「" + current.Name + "」？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            List<LaunchProfile> list = new List<LaunchProfile>();
            for (int i = 0; i < _state.Profiles.Length; i++)
            {
                if (!string.Equals(_state.Profiles[i].Name, current.Name, StringComparison.Ordinal))
                {
                    list.Add(_state.Profiles[i]);
                }
            }
            if (list.Count == 0)
            {
                list.Add(LaunchProfile.CreateQ4());
            }
            _state.Profiles = list.ToArray();
            _state.ActiveProfileName = _state.Profiles[0].Name;
            ProfileStore.Save(_state);
            ReloadProfileCombo(_state.ActiveProfileName);
            ApplySelectedProfile();
            AppendLog("已刪除設定檔。");
        }

        private void StartServer()
        {
            if (_server.IsRunning)
            {
                return;
            }

            SaveCurrentIntoState();
            LaunchProfile profile = ReadUi();
            string[] errors = CommandBuilder.Validate(profile);
            if (errors.Length > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "無法啟動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string[] warnings = CommandBuilder.Warn(profile);
            if (warnings.Length > 0)
            {
                for (int i = 0; i < warnings.Length; i++)
                {
                    AppendLog("[!] " + warnings[i]);
                }
                string text = string.Join(Environment.NewLine + Environment.NewLine, warnings)
                    + Environment.NewLine + Environment.NewLine + "仍要繼續啟動嗎？";
                if (MessageBox.Show(this, text, "設定可能有問題", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }

            if (IsPortInUse(profile.Port))
            {
                // 多半是先前殘留的服務占用，直接問使用者要不要順手結束它。
                if (ServerProcess.FindOrphanProcesses().Length > 0)
                {
                    if (MessageBox.Show(this, Strings.Get("Msg.ConfirmStopExternal"),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        int stopped = ServerProcess.StopAllServers();
                        AppendLog(Strings.Format("Msg.StoppedExternal", stopped));
                        _externalRunning = false;
                        System.Threading.Thread.Sleep(600);
                    }
                }

                if (IsPortInUse(profile.Port))
                {
                    string text = Strings.Format("Msg.PortInUse", profile.Port)
                        + Environment.NewLine + Strings.Get("Msg.PortInUseDetail");
                    MessageBox.Show(this, text, Strings.Get("Msg.CannotStart"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    AppendLog("[X] " + text.Replace(Environment.NewLine, " "));
                    return;
                }
            }

            try
            {
                ProfileStore.Save(_state);
                _txtLog.Clear();
                AppendLog("[啟動] " + CommandBuilder.BuildFullCommand(profile, _caps));
                _server.Start(profile, _caps);
                SetRunningUi(true, false, profile);
                _healthTimer.Start();
                if (profile.ShowConsole)
                {
                    AppendLog("已在獨立主控台啟動。關閉該視窗或按停止即可結束。");
                }
            }
            catch (Exception ex)
            {
                AppendLog("[X] " + ex.Message);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopServer()
        {
            _healthTimer.Stop();
            _healthPending = false;

            if (_server.IsRunning)
            {
                _server.Stop();
                AppendLog(Strings.Get("Msg.Stopping"));
            }
            else
            {
                // 這個 llama-server 不是本次啟動的（例如上次啟動器被強制關閉留下的），
                // 仍然要能結束它，否則連接埠會一直被占用。
                int stopped = ServerProcess.StopAllServers();
                AppendLog(stopped > 0
                    ? Strings.Format("Msg.StoppedExternal", stopped)
                    : Strings.Get("Msg.NothingToStop"));
            }

            _externalRunning = false;
            SetRunningUi(false, false, ReadUi());
            UpdateExternalServerState();
        }

        /// <summary>
        /// 檢查是否有不受本啟動器管理的 llama-server 還在跑，
        /// 有的話讓「停止」按鈕仍可使用，避免服務關不掉。
        /// </summary>
        private void UpdateExternalServerState()
        {
            if (_server.IsRunning)
            {
                return;
            }

            Process[] found = ServerProcess.FindOrphanProcesses();
            bool exists = found.Length > 0;
            int pid = exists ? found[0].Id : 0;
            for (int i = 0; i < found.Length; i++)
            {
                try
                {
                    found[i].Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("釋放行程物件失敗：" + ex.Message);
                }
            }

            if (exists == _externalRunning)
            {
                return;
            }

            _externalRunning = exists;
            if (exists)
            {
                _btnStop.Enabled = true;
                _btnWeb.Enabled = true;
                SetStatus("● " + Strings.Format("App.Status.External", pid), Theme.Loading, false);
                AppendLog("[!] " + Strings.Format("Msg.ExternalDetected", pid));
            }
        }

        private void OpenWebUi()
        {
            LaunchProfile profile = ReadUi();
            try
            {
                Process.Start(profile.GetApiUrl());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnHealthTick(object sender, EventArgs e)
        {
            if (!_server.IsRunning)
            {
                _healthTimer.Stop();
                return;
            }
            if (_healthPending)
            {
                return;
            }

            LaunchProfile profile = ReadUi();
            string url = profile.GetApiUrl();
            _healthPending = true;
            // 健康檢查改在背景執行緒進行，避免每秒讓 UI 卡在網路逾時上。
            ServerProcess.PingHealthAsync(url, 400, delegate (bool ok)
            {
                RunOnUi(delegate
                {
                    _healthPending = false;
                    if (!ok || !_server.IsRunning)
                    {
                        return;
                    }
                    _healthTimer.Stop();
                    SetRunningUi(true, true, profile);
                    AppendLog("[OK] API 已就緒 " + url);
                });
            });
        }

        /// <summary>
        /// 首次啟動時輸出語言與配色範本，讓想翻譯或改色的人有檔案可以直接照著改。
        /// </summary>
        private void EnsureCustomizationTemplates()
        {
            try
            {
                string langTemplate = Path.Combine(Strings.GetLanguageDirectory(),
                    Strings.DefaultLanguage + ".sample.xml");
                if (!File.Exists(langTemplate))
                {
                    Strings.WriteTemplate(langTemplate);
                }

                string themeTemplate = Path.Combine(ProfileStore.GetDirectory(), "theme.sample.xml");
                if (!File.Exists(themeTemplate))
                {
                    Theme.WriteTemplate(themeTemplate);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("輸出自訂範本失敗：" + ex.Message);
            }
        }

        /// <summary>
        /// 啟動前先確認連接埠沒被占用，這比讓 llama-server 自己失敗更容易看懂。
        /// </summary>
        private static bool IsPortInUse(int port)
        {
            if (port < 1 || port > 65535)
            {
                return false;
            }

            System.Net.Sockets.TcpListener listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                return false;
            }
            catch (System.Net.Sockets.SocketException)
            {
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("檢查連接埠失敗：" + ex.Message);
                return false;
            }
            finally
            {
                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("關閉測試監聽失敗：" + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// 讀取指定執行檔支援的旗標，之後組命令列才知道要用 --no-mmap 還是 --load-mode。
        /// </summary>
        private void RefreshCapabilities(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return;
            }

            ServerCapabilities.DetectAsync(exePath, 8000, delegate (ServerCapabilities caps)
            {
                RunOnUi(delegate
                {
                    _caps = caps;
                    if (caps != null && caps.Known)
                    {
                        AppendLog("已讀取 llama-server 支援的參數（" + caps.FlagCount.ToString() + " 個）。"
                            + (caps.Has("--load-mode")
                                ? "此版本使用 --load-mode 取代 --no-mmap。"
                                : "此版本仍使用 --no-mmap。"));
                    }
                    UpdatePreview();
                });
            });
        }

        /// <summary>
        /// 把動作排回 UI 執行緒執行；視窗已釋放時安全略過。
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("排入 UI 執行緒失敗：" + ex.Message);
            }
        }

        private void OnServerOutput(string line)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                AppendLog(line);
            }));
        }

        private void OnServerExited(int code)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                _healthTimer.Stop();
                SetRunningUi(false, false, ReadUi());
                AppendLog("[結束] 結束代碼 " + code.ToString());
            }));
        }

        private void SetRunningUi(bool running, bool ready, LaunchProfile profile)
        {
            _btnStart.Enabled = !running;
            // 即使不是自己啟動的服務，也要留著停止與開啟網頁的能力。
            _btnStop.Enabled = running || _externalRunning;
            _btnWeb.Enabled = running || _externalRunning;
            if (!running)
            {
                SetStatus("● " + Strings.Get("App.Status.Stopped"), ColorMuted, false);
            }
            else if (ready)
            {
                SetStatus("● 運行中  " + profile.GetApiUrl(), Theme.Running, true);
            }
            else
            {
                SetStatus("● 載入中… PID " + _server.ProcessId.ToString(), Theme.Loading, false);
            }
        }

        private void SetStatus(string text, Color color, bool bold)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
            _lblStatus.Font = new Font(Font, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        private void UpdatePreview()
        {
            if (_txtCommand == null)
            {
                return;
            }
            _txtCommand.Text = CommandBuilder.BuildFullCommand(ReadUi(), _caps);
        }

        private void UpdateFileStatus()
        {
            UpdateOnePath(_exe, false);
            UpdateOnePath(_model, false);
            UpdateOnePath(_mmproj, _chkMmproj == null || !_chkMmproj.Checked);
            UpdateOnePath(_chatTemplate, _chkChatTemplate == null || !_chkChatTemplate.Checked);
        }

        private static void UpdateOnePath(PathField field, bool skipped)
        {
            if (field == null)
            {
                return;
            }
            string path = field.TextBox.Text.Trim();
            if (skipped)
            {
                field.Status.Text = "未使用";
                field.Status.ForeColor = ColorMuted;
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                field.Status.Text = "未指定";
                field.Status.ForeColor = ColorMuted;
                return;
            }
            if (File.Exists(path))
            {
                field.Status.Text = "OK  " + FormatSize(new FileInfo(path).Length);
                field.Status.ForeColor = ColorOk;
            }
            else
            {
                field.Status.Text = "找不到檔案";
                field.Status.ForeColor = ColorBad;
            }
        }

        private void SuggestMmproj()
        {
            if (_loading || !_chkMmproj.Checked)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(_mmproj.TextBox.Text) && File.Exists(_mmproj.TextBox.Text.Trim()))
            {
                return;
            }
            string model = _model.TextBox.Text.Trim();
            if (!File.Exists(model))
            {
                return;
            }
            string dir = Path.GetDirectoryName(model);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.gguf");
            string found = null;
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]).ToLowerInvariant();
                if (name.IndexOf("vision", StringComparison.Ordinal) >= 0
                    || name.IndexOf("mmproj", StringComparison.Ordinal) >= 0)
                {
                    found = files[i];
                    break;
                }
            }
            if (found != null)
            {
                _mmproj.TextBox.Text = found;
            }
        }

        private void RefreshHardware(bool interactive)
        {
            try
            {
                _hardware = SystemResources.Query();
            }
            catch (Exception ex)
            {
                _hardware = new HardwareInfo();
                _hardware.CpuName = "偵測失敗";
                _hardware.LogicalProcessors = Environment.ProcessorCount;
                _hardware.PhysicalCores = Environment.ProcessorCount;
                _hardware.VulkanDevices = new GpuDevice[0];
                AppendLog("[X] 硬體偵測失敗：" + ex.Message);
            }

            if (interactive)
            {
                TryMergeLlamaDevices();
            }

            UpdateHardwareAdvice();
            if (_hardware != null && _hardware.TotalMemoryBytes > 0)
            {
                string gpu = string.IsNullOrEmpty(_hardware.GpuName) ? "GPU 未偵測" : _hardware.GpuName;
                string vram = _hardware.DedicatedVramBytes > 0
                    ? SystemResources.FormatGb(_hardware.DedicatedVramBytes)
                    : "未知";
                AppendLog("本機：" + _hardware.CpuName
                    + "，" + _hardware.PhysicalCores.ToString() + " 實體 / "
                    + _hardware.LogicalProcessors.ToString() + " 邏輯，記憶體 "
                    + SystemResources.FormatGb(_hardware.TotalMemoryBytes)
                    + "（可用 " + SystemResources.FormatGb(_hardware.AvailableMemoryBytes)
                    + "），" + gpu + " 顯存 " + vram + "。");
            }
            else if (interactive)
            {
                MessageBox.Show(this, "無法讀取系統記憶體資訊。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void TryMergeLlamaDevices()
        {
            if (_exe == null || _hardware == null)
            {
                return;
            }
            string exe = _exe.TextBox.Text.Trim();
            if (!File.Exists(exe) || _devicesPending)
            {
                return;
            }

            // 呼叫 llama-server --list-devices 可能耗時數秒，放到背景避免 UI 凍結。
            _devicesPending = true;
            UseWaitCursor = true;
            ServerProcess.ListGpuDevicesAsync(exe, 4000, delegate (GpuDevice[] devices)
            {
                RunOnUi(delegate
                {
                    _devicesPending = false;
                    UseWaitCursor = false;
                    if (_hardware == null)
                    {
                        return;
                    }

                    SystemResources.MergeVulkanDevices(_hardware, devices);
                    if (devices.Length == 0)
                    {
                        return;
                    }

                    FillDeviceCombo(devices);
                    UpdateHardwareAdvice();
                    AppendLog("偵測到裝置：" + FormatGpuDevices(devices));
                });
            });
        }

        /// <summary>
        /// 以偵測到的裝置重建下拉選單，盡量保留使用者目前的選擇。
        /// 選項會一併顯示顯示卡名稱與容量，方便分辨內顯與外接獨顯；
        /// 送進 --device 的值則另外由 ExtractDeviceId 還原成純裝置代號。
        /// </summary>
        private void FillDeviceCombo(GpuDevice[] devices)
        {
            if (_cboDevice == null || devices == null || devices.Length == 0)
            {
                return;
            }

            // 先記下目前選擇的裝置代號，重建清單後再選回同一顆卡。
            string currentId = ExtractDeviceId(_cboDevice.Text);
            _cboDevice.Items.Clear();
            for (int i = 0; i < devices.Length; i++)
            {
                string label = FormatGpuDeviceLabel(devices[i]);
                if (!_cboDevice.Items.Contains(label))
                {
                    _cboDevice.Items.Add(label);
                }
            }

            SelectDeviceById(currentId);
            if (string.IsNullOrEmpty(_cboDevice.Text))
            {
                _cboDevice.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 依裝置代號選回下拉選單中對應的項目；找不到就保留原字串，
        /// 讓使用者手動輸入的內容（例如尚未偵測到的 Vulkan0）不會被清掉。
        /// </summary>
        private void SelectDeviceById(string deviceId)
        {
            if (_cboDevice == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(deviceId))
            {
                _cboDevice.Text = "";
                return;
            }

            for (int i = 0; i < _cboDevice.Items.Count; i++)
            {
                string item = Convert.ToString(_cboDevice.Items[i]);
                if (string.Equals(ExtractDeviceId(item), deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _cboDevice.SelectedIndex = i;
                    return;
                }
            }
            _cboDevice.Text = deviceId;
        }

        /// <summary>
        /// 由選單文字取回純裝置代號，例如
        /// 「Vulkan1 - AMD Radeon RX 7900 XTX 24.0 GB」會取出「Vulkan1」。
        /// --device 支援以逗號指定多顆裝置，因此逐段處理後再組回去。
        /// </summary>
        internal static string ExtractDeviceId(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string[] parts = text.Split(',');
            List<string> ids = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }
                // 代號本身不含空白，取第一段即可去掉附加的顯示卡名稱與容量。
                int space = part.IndexOf(' ');
                if (space > 0)
                {
                    part = part.Substring(0, space).Trim();
                }
                if (part.Length > 0)
                {
                    ids.Add(part);
                }
            }
            return string.Join(",", ids.ToArray());
        }

        /// <summary>把單一裝置整理成「代號 - 名稱 容量」的顯示字串。</summary>
        private static string FormatGpuDeviceLabel(GpuDevice device)
        {
            if (device == null)
            {
                return "";
            }
            string item = device.Id;
            if (!string.IsNullOrEmpty(device.Name))
            {
                item += " - " + device.Name;
            }
            if (device.MemoryBytes > 0)
            {
                item += " " + SystemResources.FormatGb(device.MemoryBytes);
            }
            return item;
        }

        private static string FormatGpuDevices(GpuDevice[] devices)
        {
            if (devices == null || devices.Length == 0)
            {
                return "";
            }
            string[] parts = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
            {
                parts[i] = FormatGpuDeviceLabel(devices[i]);
            }
            return string.Join(", ", parts);
        }

        private void OpenOptimizer(bool office, bool firstRun)
        {
            if (_tabs != null && _pageOptimize != null)
            {
                _tabs.SelectedTab = _pageOptimize;
            }
            if (_hardware == null)
            {
                RefreshHardware(false);
            }
            ShowTunePreview(office, firstRun);
        }

        private void ShowTunePreview(bool office, bool firstRun)
        {
            LaunchProfile current = ReadUi();
            TunePlan plan = HardwareTuner.Build(_hardware, current, office);
            string text = HardwareTuner.FormatPreview(plan);
            if (firstRun)
            {
                text = "第一次使用：已依這台電腦算出一組比較不容易把桌面搶光的設定。"
                    + Environment.NewLine + Environment.NewLine + text;
            }

            DialogResult result;
            using (Form dlg = new Form())
            {
                dlg.Text = firstRun ? "初次設定：依本機最佳化" : "依本機最佳化";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(640, 430);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Vertical;
                box.Text = text;
                box.Location = new Point(16, 16);
                box.Size = new Size(608, 360);

                Button ok = new Button();
                ok.Text = firstRun ? "套用辦公建議" : "套用";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(firstRun ? 392 : 452, 388);
                ok.Width = firstRun ? 116 : 80;

                Button cancel = new Button();
                cancel.Text = firstRun ? "稍後自己調" : "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(520, 388);
                cancel.Width = firstRun ? 104 : 80;

                dlg.Controls.Add(box);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                result = dlg.ShowDialog(this);
            }

            if (result == DialogResult.OK)
            {
                ApplyTunePlan(plan);
                AppendLog(office ? "已套用辦公並行建議（尚未存檔，可按 Ctrl+S）。" : "已套用效能優先建議（尚未存檔，可按 Ctrl+S）。");
            }

            if (firstRun)
            {
                MarkOptimizerOffered();
            }
        }

        private void ApplyTunePlan(TunePlan plan)
        {
            LaunchProfile current = CurrentProfileOrNull();
            if (current == null || plan == null || plan.Suggested == null)
            {
                return;
            }
            HardwareTuner.CopyTunable(plan.Suggested, current);
            ApplySelectedProfile();
            SaveCurrentIntoState();
        }

        private void MarkOptimizerOffered()
        {
            if (_state == null)
            {
                return;
            }
            _state.OptimizerOffered = true;
            try
            {
                SaveCurrentIntoState();
                ProfileStore.Save(_state);
            }
            catch (Exception)
            {
            }
        }

        private void UpdateHardwareAdvice()
        {
            if (_lblHwInfo == null)
            {
                return;
            }
            if (_hardware == null)
            {
                _lblHwInfo.Text = "尚未偵測。";
                return;
            }

            HardwareInfo hw = _hardware;
            string gpu = string.IsNullOrEmpty(hw.GpuName) ? "未偵測到獨立顯卡" : hw.GpuName;
            string vram = hw.DedicatedVramBytes > 0
                ? SystemResources.FormatGb(hw.DedicatedVramBytes)
                : "未知";
            string vulkan = "";
            if (hw.VulkanDevices != null && hw.VulkanDevices.Length > 0)
            {
                vulkan = "Vulkan：" + FormatGpuDevices(hw.VulkanDevices);
            }

            _lblHwInfo.Text = hw.CpuName + Environment.NewLine
                + hw.PhysicalCores.ToString() + " 實體核心 / "
                + hw.LogicalProcessors.ToString() + " 邏輯處理器" + Environment.NewLine
                + "記憶體 " + SystemResources.FormatGb(hw.TotalMemoryBytes)
                + "（可用 " + SystemResources.FormatGb(hw.AvailableMemoryBytes)
                + "）　" + gpu + " 顯存 " + vram
                + (string.IsNullOrEmpty(vulkan) ? "" : Environment.NewLine + vulkan);

            if (_lblHwAdvice != null)
            {
                TunePlan office = HardwareTuner.Build(hw, _ready ? ReadUi() : new LaunchProfile(), true);
                _lblHwAdvice.Text = office.Summary
                    + (office.Warnings != null && office.Warnings.Length > 0
                        ? Environment.NewLine + office.Warnings[0]
                        : "")
                    + Environment.NewLine
                    + "按「辦公並行」或「效能優先」可預覽後再套用，不會自動存檔。";
            }

            if (_lblThreadHint != null)
            {
                _lblThreadHint.Text = "−1 省略。辦公建議 "
                    + SystemResources.SuggestOfficeThreads(hw).ToString()
                    + "，效能建議 "
                    + SystemResources.SuggestPerfThreads(hw).ToString()
                    + "（用實體核，不必吃滿邏輯處理器）";
            }
        }

        private void SetPrioCombo(string value)
        {
            if (_cboPrio == null)
            {
                return;
            }
            string prio = SystemResources.NormalizePrio(value);
            if (prio == "low")
            {
                _cboPrio.SelectedIndex = 1;
            }
            else if (prio == "normal")
            {
                _cboPrio.SelectedIndex = 2;
            }
            else if (prio == "medium")
            {
                _cboPrio.SelectedIndex = 3;
            }
            else if (prio == "high")
            {
                _cboPrio.SelectedIndex = 4;
            }
            else
            {
                _cboPrio.SelectedIndex = 0;
            }
        }

        private string ReadPrioCombo()
        {
            if (_cboPrio == null || _cboPrio.SelectedIndex <= 0)
            {
                return "";
            }
            return SystemResources.NormalizePrio(_cboPrio.Text);
        }

        private void RefreshDevices(bool interactive)
        {
            string exe = _exe.TextBox.Text.Trim();
            if (!File.Exists(exe))
            {
                if (interactive)
                {
                    MessageBox.Show(this, "請先指定有效的 llama-server.exe。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (_devicesPending)
            {
                AppendLog("裝置偵測進行中，請稍候…");
                return;
            }

            AppendLog("正在偵測裝置（最多 5 秒）…");
            _devicesPending = true;
            UseWaitCursor = true;
            // 偵測工作放在背景執行緒，避免等待期間整個視窗沒有回應。
            ServerProcess.ListGpuDevicesAsync(exe, 5000, delegate (GpuDevice[] devices)
            {
                RunOnUi(delegate
                {
                    _devicesPending = false;
                    UseWaitCursor = false;
                    OnDevicesDetected(devices, interactive);
                });
            });
        }

        private void OnDevicesDetected(GpuDevice[] devices, bool interactive)
        {
            if (devices == null)
            {
                devices = new GpuDevice[0];
            }

            if (_hardware == null)
            {
                try
                {
                    _hardware = SystemResources.Query();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("硬體偵測失敗：" + ex.Message);
                    _hardware = new HardwareInfo();
                    _hardware.VulkanDevices = new GpuDevice[0];
                }
            }
            SystemResources.MergeVulkanDevices(_hardware, devices);
            UpdateHardwareAdvice();

            if (devices.Length == 0)
            {
                // 沒有 Vulkan 裝置會退回 CPU，速度差好幾倍，這裡要講清楚。
                AppendLog("[X] " + Strings.Get("Msg.VulkanNoDevice"));
                AppendLog(Strings.Get("Msg.NoDevices"));
                if (interactive)
                {
                    MessageBox.Show(this, Strings.Get("Msg.VulkanNoDevice")
                        + Environment.NewLine + Environment.NewLine
                        + Strings.Get("Msg.NoDevicesDialog"),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            FillDeviceCombo(devices);
            AppendLog(Strings.Format("Msg.DevicesFound", FormatGpuDevices(devices)));
        }

        private void BrowseFile(PathField field)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = field.Filter;
                dlg.CheckFileExists = true;
                string current = field.TextBox.Text.Trim();
                if (File.Exists(current))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(current);
                    dlg.FileName = Path.GetFileName(current);
                }
                else if (Directory.Exists(current))
                {
                    dlg.InitialDirectory = current;
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    field.TextBox.Text = dlg.FileName;
                    if (field == _model)
                    {
                        SuggestMmproj();
                    }
                }
            }
        }

        private void CopyCommand()
        {
            string text = _txtCommand.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Clipboard.SetText(text);
            AppendLog("已複製指令。");
        }

        private void ExportBat()
        {
            LaunchProfile profile = ReadUi();
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "批次檔 (*.bat)|*.bat";
                dlg.FileName = "start-" + SanitizeFileName(profile.Name) + ".bat";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    File.WriteAllText(dlg.FileName, CommandBuilder.BuildBat(profile), new System.Text.UTF8Encoding(false));
                    AppendLog("已匯出：" + dlg.FileName);
                }
            }
        }

        private void AppendLog(string line)
        {
            if (_txtLog == null)
            {
                return;
            }
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText(stamp + "  " + line + Environment.NewLine);
        }

        private void SetSpecEnabled(bool enabled)
        {
            if (_specControls == null)
            {
                return;
            }
            for (int i = 0; i < _specControls.Length; i++)
            {
                _specControls[i].Enabled = enabled;
            }
        }

        private string PromptName(string title, string defaultName)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = title;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(420, 110);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                Label label = new Label();
                label.Text = "設定檔名稱";
                label.Location = new Point(16, 16);
                label.AutoSize = true;
                TextBox box = new TextBox();
                box.Text = defaultName;
                box.Location = new Point(16, 38);
                box.Width = 388;
                Button ok = new Button();
                ok.Text = "確定";
                ok.DialogResult = DialogResult.OK;
                ok.Location = new Point(248, 72);
                Button cancel = new Button();
                cancel.Text = "取消";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(329, 72);
                dlg.Controls.Add(label);
                dlg.Controls.Add(box);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }

        private static void SetCombo(ComboBox combo, string value)
        {
            if (value == null)
            {
                value = "";
            }
            int index = combo.FindStringExact(value);
            if (index >= 0)
            {
                combo.SelectedIndex = index;
            }
            else
            {
                combo.Text = value;
            }
        }

        private static decimal Clamp(NumericUpDown num, decimal value)
        {
            if (value < num.Minimum)
            {
                return num.Minimum;
            }
            if (value > num.Maximum)
            {
                return num.Maximum;
            }
            return value;
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            if (int.TryParse(text.Trim(), out value) && value > 0)
            {
                return value;
            }
            return fallback;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824L)
            {
                return (bytes / 1073741824.0).ToString("0.0") + " GB";
            }
            if (bytes >= 1048576L)
            {
                return (bytes / 1048576.0).ToString("0.0") + " MB";
            }
            return bytes.ToString() + " B";
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "profile";
            }
            char[] bad = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < bad.Length; j++)
                {
                    if (chars[i] == bad[j] || chars[i] == ' ')
                    {
                        chars[i] = '-';
                        break;
                    }
                }
            }
            return new string(chars);
        }

        private sealed class PathField
        {
            public TextBox TextBox;
            public Button Browse;
            public Label Status;
            public string Filter;
        }
    }
}
