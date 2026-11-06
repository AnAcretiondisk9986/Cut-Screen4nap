using System.Drawing;
using System.Windows.Forms;

namespace CutScreen;

internal sealed class MainForm : Form
{
    private readonly Config _cfg;
    private ModeController? _ctl;

    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly Label _countdown = new();
    private readonly Panel _cards = new();
    private readonly Button[] _modeButtons = new Button[3];
    private readonly Button _cancel = new();
    private readonly Button _settings = new();
    private readonly ComboBox _delay = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };
    private bool _hiddenByMode;

    // 文案压到 3 行、每行不折行；技术细节留给 README / 设置页，不堆在主界面
    private static readonly (Mode Mode, string Title, string Sub, string[] Lines, Color Accent)[] Cards =
    {
        (Mode.Hibernate, "😴  休眠", "无任务",
            new[] { "整机断电", "屏幕全黑", "电源键唤醒" },
            Color.FromArgb(93, 100, 135)),
        (Mode.StayAwake, "⬇  挂机", "有任务",
            new[] { "任务照跑", "三屏全黑", "输入即回" },
            Color.FromArgb(52, 96, 168)),
        (Mode.Nap, "🌙  午睡", "临时补觉",
            new[] { "任务照跑", "纯黑窗口", "零闪烁" },
            Color.FromArgb(122, 84, 162)),
    };

    public MainForm(Config cfg)
    {
        _cfg = cfg;

        Text = "Cut-Screen";
        Size = new Size(700, 470);
        MinimumSize = new Size(640, 430);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(32, 34, 40);
        ForeColor = Color.FromArgb(232, 234, 240);

        BuildUi();
        _tick.Tick += (_, _) => TickCountdown();
        _tick.Start();
    }

    public void AttachController(ModeController ctl) => _ctl = ctl;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));    // 状态：单行，绝不裁切
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 卡片
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));    // 倒计时
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));    // 底栏

        root.Controls.Add(BuildStatusRow(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);
        root.Controls.Add(BuildCountdownRow(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildStatusRow()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _status.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _status.AutoSize = true;
        _status.Anchor = AnchorStyles.Left;          // 不锚上下 → TableLayoutPanel 垂直居中
        _status.TextAlign = ContentAlignment.MiddleLeft;

        _detail.ForeColor = Color.FromArgb(150, 155, 168);
        _detail.AutoSize = true;
        _detail.Anchor = AnchorStyles.Left;
        _detail.TextAlign = ContentAlignment.MiddleLeft;
        _detail.Padding = new Padding(14, 0, 0, 0);

        t.Controls.Add(_status, 0, 0);
        t.Controls.Add(_detail, 1, 0);
        return t;
    }

    private Control BuildCards()
    {
        _cards.Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        for (int i = 0; i < 3; i++) layout.Controls.Add(BuildCard(i), i, 0);
        _cards.Controls.Add(layout);
        return _cards;
    }

    private Control BuildCard(int i)
    {
        var c = Cards[i];
        var card = new Panel
        {
            Dock = DockStyle.Fill, Margin = new Padding(5, 2, 5, 4),
            BackColor = Color.FromArgb(44, 47, 56), Padding = new Padding(12)
        };

        // 固定行高的表格：不依赖 Dock 的反 z-order 规则，色条一定在最顶端
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));        // 顶部色条
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));       // 标题
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));       // 副标题
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // 说明
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));       // 按钮

        var accent = new Panel
        { Dock = DockStyle.Fill, BackColor = c.Accent, Margin = new Padding(0, 0, 0, 6) };

        var title = new Label
        {
            Text = c.Title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold)
        };
        var sub = new Label
        {
            Text = c.Sub, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = c.Accent, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };
        var lines = new Label
        {
            Text = string.Join("\n", c.Lines.Select(s => "·  " + s)),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(190, 194, 204),
            Font = new Font("Microsoft YaHei UI", 9F),
            Padding = new Padding(0, 0, 0, 0)
        };
        var btn = _modeButtons[i] = new Button
        {
            Text = "进入此档", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 2),
            FlatStyle = FlatStyle.Flat, BackColor = c.Accent, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btn.FlatAppearance.BorderSize = 0;
        var mode = c.Mode;
        btn.Click += (_, _) => _ctl?.Request(mode);

        t.Controls.Add(accent, 0, 0);
        t.Controls.Add(title, 0, 1);
        t.Controls.Add(sub, 0, 2);
        t.Controls.Add(lines, 0, 3);
        t.Controls.Add(btn, 0, 4);
        card.Controls.Add(t);
        return card;
    }

    private Control BuildCountdownRow()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        _countdown.Dock = DockStyle.Fill;
        _countdown.TextAlign = ContentAlignment.MiddleCenter;
        _countdown.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        _countdown.ForeColor = Color.FromArgb(255, 196, 92);
        _cancel.Text = "取消";
        _cancel.Dock = DockStyle.Right;
        _cancel.Width = 76;
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.Click += (_, _) => _ctl?.Cancel();
        p.Controls.Add(_countdown);
        p.Controls.Add(_cancel);
        return p;
    }

    private Control BuildFooter()
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(0, 4, 0, 0)
        };

        var lbl = new Label
        {
            Text = "进入前等待", AutoSize = true,
            Padding = new Padding(0, 7, 8, 0), ForeColor = Color.FromArgb(170, 175, 188)
        };
        _delay.DropDownStyle = ComboBoxStyle.DropDownList;
        _delay.Items.AddRange(new object[] { "0 秒", "10 秒", "20 秒", "30 秒", "45 秒", "60 秒" });
        _delay.Width = 84;
        int[] vals = { 0, 10, 20, 30, 45, 60 };
        int idx = Array.IndexOf(vals, _cfg.EnterDelaySeconds);
        _delay.SelectedIndex = idx < 0 ? 2 : idx;
        _delay.SelectedIndexChanged += (_, _) =>
        {
            _cfg.EnterDelaySeconds = vals[Math.Clamp(_delay.SelectedIndex, 0, vals.Length - 1)];
            _cfg.Save();
        };

        _settings.Text = "设置";
        _settings.AutoSize = true;
        _settings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _settings.MinimumSize = new Size(72, 28);
        _settings.Padding = new Padding(8, 0, 8, 0);
        _settings.Margin = new Padding(20, 2, 0, 0);
        _settings.FlatStyle = FlatStyle.Flat;
        _settings.Click += (_, _) => { if (_ctl is not null) new SettingsForm(_cfg, _ctl).ShowDialog(this); };

        p.Controls.Add(lbl);
        p.Controls.Add(_delay);
        p.Controls.Add(_settings);
        return p;
    }

    public void ApplyState(ModeState st)
    {
        if (InvokeRequired) { try { BeginInvoke(() => ApplyState(st)); } catch { } return; }

        _status.Text = st.Label;
        _detail.Text = st.Phase switch
        {
            Phase.Active => "任意输入即退出",
            Phase.Countdown => "点「取消」可撤销",
            Phase.Entering => "正在准备…",
            _ => "三屏亮着 · 无保活"
        };

        bool busy = st.Phase != Phase.Idle;
        foreach (var b in _modeButtons) b.Enabled = !busy;
        _cancel.Visible = busy;
        _settings.Enabled = !busy;

        // 进入档位后主窗口自己也要藏起来，否则三屏全黑时它还亮着一块
        if (st.Phase == Phase.Active && !_hiddenByMode)
        {
            _hiddenByMode = true;
            Hide();
        }
        else if (st.Phase == Phase.Idle && _hiddenByMode)
        {
            _hiddenByMode = false;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
        }

        TickCountdown();
    }

    private void TickCountdown()
    {
        if (_ctl is null) return;
        var st = _ctl.State;
        if (st.Phase == Phase.Countdown)
        {
            double left = Math.Max(0, (_ctl.CountdownEnd - DateTime.Now).TotalSeconds);
            _countdown.Text = $"{Math.Ceiling(left):0} 秒后进入【{ModeNames.Cn(st.Mode!.Value)}】";
            _countdown.Visible = true;
        }
        else if (st.Phase == Phase.Active)
        {
            var d = DateTime.Now - st.EnteredAt;
            _countdown.Text = $"已运行 {d.Hours:00}:{d.Minutes:00}:{d.Seconds:00}  ·  任意输入即退出";
            _countdown.Visible = true;
        }
        else
        {
            _countdown.Text = "";
            _countdown.Visible = false;
        }
    }

    // 单实例：第二个进程广播的自定义消息，在这里接住并置前
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TrayAppContext.WM_SHOW_FIRST_INSTANCE)
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront(); Activate();
        }
        base.WndProc(ref m);
    }
}
