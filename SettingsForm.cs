using System.Drawing;
using System.Windows.Forms;

namespace CutScreen;

internal sealed class SettingsForm : Form
{
    private readonly Config _cfg;
    private readonly ModeController _ctl;
    private bool _startupDesired;
    private CheckBox? _startupCheck;

    public SettingsForm(Config cfg, ModeController ctl)
    {
        _cfg = cfg;
        _ctl = ctl;
        // 以注册表实际状态为准（Program 启动时已把配置对齐过一次）。
        _startupDesired = StartupManager.IsEnabled();
        Text = "设置 · Cut-Screen";
        Size = new Size(560, 600);
        MinimumSize = new Size(440, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(32, 34, 40);
        ForeColor = Color.FromArgb(232, 234, 240);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false;

        // 纵向流 + 自动滚动。所有子控件 AutoSize=true，绝不写死 Height/Width：
        // 高 DPI 下硬编尺寸会把文字裁掉一半（旧版正是这么坏的）。
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(18, 12, 18, 12)
        };
        // 关闭横向滚动：文案已压短，靠换行消化，不撑出水平滚动条
        root.HorizontalScroll.Enabled = false;
        root.HorizontalScroll.Visible = false;

        root.Controls.Add(Section("进入与退出"));
        root.Controls.Add(Spin("进入延时（秒）", 0, 120, _cfg.EnterDelaySeconds, v => _cfg.EnterDelaySeconds = v));
        root.Controls.Add(Spin("退出宽限期（秒）", 0, 30, _cfg.ExitGraceSeconds, v => _cfg.ExitGraceSeconds = v));
        root.Controls.Add(Check("记住上次选择的档位", _cfg.RememberLastMode, v => _cfg.RememberLastMode = v));

        root.Controls.Add(Section("挂机档（B）"));
        root.Controls.Add(Check("进入时锁屏（唤醒后仍需输 PIN）",
            _cfg.StayAwakeLockScreen, v => _cfg.StayAwakeLockScreen = v));
        root.Controls.Add(Check("外接屏优先用 DDC/CI 关背光",
            _cfg.UseDdcCi, v => _cfg.UseDdcCi = v));

        root.Controls.Add(Section("输入检测"));
        root.Controls.Add(Check("忽略光学鼠标微小抖动",
            _cfg.IgnoreMouseJitter, v => { _cfg.IgnoreMouseJitter = v; _ctl.RebuildWatcher(); }));
        root.Controls.Add(Spin("鼠标位移阈值（px）", 1, 50, _cfg.MouseThresholdPx, v => _cfg.MouseThresholdPx = v));

        root.Controls.Add(Section("开机自启"));
        root.Controls.Add(BuildStartupOption());

        root.Controls.Add(BuildButtons());
        Controls.Add(root);
    }

    private Control BuildStartupOption()
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0, 4, 0, 4)
        };

        var c = new CheckBox
        {
            Text = "开机自动启动（静默，不弹主窗口）", Checked = _startupDesired,
            AutoSize = true, Margin = new Padding(0, 4, 0, 2)
        };
        c.CheckedChanged += (_, _) => _startupDesired = c.Checked;
        _startupCheck = c;

        var hint = new Label
        {
            Text = "登录后只在托盘静默就绪；写 HKCU 运行项，无需管理员。"
                 + "\n需修改请点「保存并关闭」后生效。关闭本项会移除自启。",
            AutoSize = true, ForeColor = Color.FromArgb(150, 155, 168),
            Margin = new Padding(20, 2, 0, 2)
        };

        p.Controls.Add(c);
        p.Controls.Add(hint);
        return p;
    }

    private Control BuildButtons()
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = new Padding(0, 20, 0, 4)
        };

        var cal = new Button
        {
            Text = "检测外接屏 DDC/CI 校准", AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30),
            Padding = new Padding(10, 0, 10, 0), FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0)
        };
        cal.Click += (_, _) => MessageBox.Show(this, DisplayController.Calibrate(), "DDC/CI 校准",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

        var ok = new Button
        {
            Text = "保存并关闭", AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30),
            Padding = new Padding(10, 0, 10, 0), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 96, 168), ForeColor = Color.White
        };
        ok.Click += (_, _) =>
        {
            // 自启状态写注册表（HKCU，无需管理员）。失败要让用户知道，别静默吞掉。
            bool applied = StartupManager.SetEnabled(_startupDesired);
            if (applied)
            {
                _cfg.RunAtStartup = _startupDesired;
                _cfg.Save();
                Close();
            }
            else
            {
                MessageBox.Show(this,
                    "无法写入开机自启设置（注册表访问被拒绝）。\n"
                  + "可手动用任务计划程序或以当前用户权限重试。",
                    "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (_startupCheck is not null) _startupCheck.Checked = StartupManager.IsEnabled();
            }
        };

        p.Controls.Add(cal);
        p.Controls.Add(ok);
        return p;
    }

    private static Label Section(string s) => new()
    {
        Text = s, AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        ForeColor = Color.FromArgb(120, 170, 255),
        Margin = new Padding(0, 16, 0, 6)
    };

    private static Control Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox
        {
            Text = text, Checked = value, AutoSize = true,
            Margin = new Padding(0, 4, 0, 4)
        };
        c.CheckedChanged += (_, _) => set(c.Checked);
        return c;
    }

    private static Control Spin(string label, int min, int max, int value, Action<int> set)
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = new Padding(0, 4, 0, 4)
        };
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 72, Margin = new Padding(10, 0, 0, 0)
        };
        n.ValueChanged += (_, _) => set((int)n.Value);
        p.Controls.Add(new Label
        {
            Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0)
        });
        p.Controls.Add(n);
        return p;
    }
}
