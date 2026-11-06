using System.Windows.Forms;
using System.Drawing;

namespace CutScreen;

/// <summary>
/// ApplicationContext：托盘常驻 + 主窗口。关窗口只 Hide，不结束进程 ——
/// 保活句柄挂在进程级 controller 上，跟窗口生命周期解耦。
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    // 必须存成 int：Message.Msg 是 int，而 RegisterWindowMessage 返回 0xC000~0xFFFF，
    // 若存 uint 则是正数，与 int(负数) 比较恒为 false，单实例唤醒会失效。
    public static readonly int WM_SHOW_FIRST_INSTANCE =
        unchecked((int)NativeMethods.RegisterWindowMessage("CutScreen_ShowFirstInstance"));

    private readonly Config _cfg;
    private readonly ModeController _controller;
    private readonly NotifyIcon _tray;
    private readonly MainForm _main;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly Dictionary<int, Icon> _iconCache = new();
    private Icon? _currentIcon;

    public TrayAppContext(Config cfg)
    {
        _cfg = cfg;
        _main = new MainForm(_cfg);   // 先建窗体（拿到句柄与同步上下文）
        _controller = new ModeController(cfg, RefreshUi, Notify, MarshalToUi);
        _main.AttachController(_controller);
        _main.FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _main.Hide();       // × = 缩到托盘，不是退出
                Notify("Cut-Screen 仍在托盘运行。右键托盘可完全退出。");
            }
        };

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(SystemColors.ControlText),
            Text = "Cut-Screen · 就绪",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowMain();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += (_, _) => RefreshUi();
        _uiTimer.Start();

        // 崩溃自恢复：上次异常退出留下的持久化改动
        if (Snapshot.Recover(out var report))
            Notify(report);

        _controller.RebuildWatcher();
        ShowMain();
        RefreshUi();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("😴  休眠（无任务）", null, (_, _) => _controller.Request(Mode.Hibernate));
        menu.Items.Add("⬇  挂机（有任务）", null, (_, _) => _controller.Request(Mode.StayAwake));
        menu.Items.Add("🌙  午睡（临时补觉）", null, (_, _) => _controller.Request(Mode.Nap));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("取消 / 退出当前档", null, (_, _) => _controller.Exit());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => ReallyExit());
        return menu;
    }

    private void ShowMain()
    {
        // 档位运行中不要把主窗口重新亮出来（否则三屏全黑被打破）
        if (_controller.State.Phase == Phase.Active) return;
        _main.Show();
        _main.WindowState = FormWindowState.Normal;
        _main.BringToFront();
        _main.Activate();
    }

    /// <summary>把回调封送到 UI 线程。已在 UI 线程则直接执行。</summary>
    private void MarshalToUi(Action action)
    {
        try
        {
            if (_main.IsDisposed) return;
            if (_main.InvokeRequired) _main.BeginInvoke(action);
            else action();
        }
        catch { /* 窗体正在销毁时忽略 */ }
    }

    private void Notify(string msg)
    {
        MarshalToUi(() =>
        {
            try { _tray.ShowBalloonTip(4000, "Cut-Screen", msg, ToolTipIcon.Info); } catch { }
        });
    }

    private void RefreshUi()
    {
        if (_main.IsDisposed) return;
        if (_main.InvokeRequired) { try { _main.BeginInvoke(RefreshUi); } catch { } return; }
        var st = _controller.State;
        Color c = st.Phase switch
        {
            Phase.Idle => SystemColors.ControlText,
            Phase.Countdown => Color.Orange,
            Phase.Entering => Color.Orange,
            Phase.Active => st.Mode switch
            {
                Mode.Hibernate => Color.SlateBlue,
                Mode.StayAwake => Color.RoyalBlue,
                Mode.Nap => Color.MediumPurple,
                _ => Color.SeaGreen
            },
            _ => SystemColors.ControlText
        };
        // 图标按颜色缓存：RefreshUi 每 500ms 调用一次，不能每次都新建 HICON（GDI 句柄泄漏）
        int key = c.ToArgb();
        if (!_iconCache.TryGetValue(key, out var icon))
        {
            icon = MakeIcon(c);
            _iconCache[key] = icon;
        }
        if (!ReferenceEquals(_currentIcon, icon))
        {
            _tray.Icon = icon;
            _currentIcon = icon;
        }
        _tray.Text = st.Phase == Phase.Idle ? "Cut-Screen · 就绪" : st.Label;
        _main.ApplyState(st);
    }

    private static Icon MakeIcon(Color c)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var b = new SolidBrush(c);
            // 一个简单的「月亮+屏幕」图形
            g.FillRectangle(b, 1, 3, 14, 10);
            using var cut = new SolidBrush(Color.Transparent);
            g.FillEllipse(cut, 8, 1, 10, 10);
            g.DrawRectangle(Pens.White, 1, 3, 14, 10);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ReallyExit()
    {
        _uiTimer.Stop();
        _controller.Shutdown();
        _tray.Visible = false;
        _tray.Dispose();
        foreach (var ic in _iconCache.Values) ic.Dispose();
        _iconCache.Clear();
        _main.Dispose();
        ExitThread();
    }
}
