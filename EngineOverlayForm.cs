using System.Drawing.Drawing2D;

namespace KeyMouseFactory;

/// <summary>
/// 连按/连点状态浮条：引擎开启后主窗口收进托盘，屏幕右下角留一条只读提示
/// （「连按中 · F6」/「连点中 · F8」），与回放浮条同一视觉规格。
///
/// 与回放浮条同理：整块**鼠标穿透**（WS_EX_TRANSPARENT）。连按/连点期间鼠标可能被注入
/// 或用于按住停止，浮条若可点击会吃掉事件；这里只做展示——停止请按热键或用托盘菜单。
/// </summary>
internal sealed class EngineOverlayForm : Form
{
    private const int DotSize = 8;
    private const int PadLeft = 14;
    private const int PadRight = 14;
    private const int GapAfterDot = 6;
    private const int Radius = 10;
    private const int MinWidth = 150;
    private const TextFormatFlags Flags =
        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
    private const TextFormatFlags MeasFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    private readonly Font _bold = new(AntStyle.FontName, 9f, FontStyle.Bold);
    private readonly Font _small = new(AntStyle.FontName, 8f);
    private readonly string _name;    // "连按中" / "连点中"
    private string _hotkey;
    private bool _blinkOn = true;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public EngineOverlayForm(string name, string hotkey)
    {
        _name = name;
        _hotkey = string.IsNullOrWhiteSpace(hotkey) ? "" : hotkey.Trim();

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Height = 44;

        ResizeToContent();

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);

        _timer.Tick += (_, _) => { _blinkOn = !_blinkOn; Invalidate(); };
        _timer.Start();
    }

    /// <summary>只做提示，绝不能把焦点从游戏手里抢走。</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>热键被改/被占用自动调整时同步浮窗上的提示。</summary>
    public void SetHotkey(string hotkey)
    {
        string next = string.IsNullOrWhiteSpace(hotkey) ? "" : hotkey.Trim();
        if (next == _hotkey) return;
        _hotkey = next;
        ResizeToContent(keepRight: true);
        Invalidate();
    }

    /// <summary>调用方先摘引用再调它，外部关闭（退出程序）也走这里。</summary>
    public void CloseSilently()
    {
        try { Close(); }
        finally
        {
            _timer.Stop();
            _timer.Dispose();
            Dispose();
        }
    }

    private string HotkeyText() => _hotkey.Length > 0 ? $"· {_hotkey} 停止" : "";

    // 文本宽度变化时重算，右边缘固定、只向左长（与回放浮条一致）
    private void ResizeToContent(bool keepRight = false)
    {
        string tail = HotkeyText();
        int w = PadLeft + DotSize + GapAfterDot + Measure(_name, _bold).Width;
        if (tail.Length > 0) w += GapAfterDot + Measure(tail, _small).Width;
        w += PadRight;

        w = Math.Max(w, MinWidth);
        if (w == Width) return;
        int right = Right;
        Width = w;
        if (keepRight) Left = right - w;
    }

    private static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, MeasFlags);

    private int DrawSeg(Graphics g, string text, Font font, Color color, int x)
    {
        int w = Measure(text, font).Width;
        TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, w, Height), color, Flags);
        return w;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedPath(new Rectangle(0, 0, Width, Height), Radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var all = new Rectangle(0, 0, Width - 1, Height - 1);
        AntStyle.FillRounded(g, Color.White, all, Radius);
        AntStyle.DrawRounded(g, AntStyle.Border, all, Radius);

        int x = PadLeft;
        Color dotColor = _blinkOn ? AntStyle.Primary : Color.FromArgb(163, 200, 255);
        using (var dot = new SolidBrush(dotColor))
            g.FillEllipse(dot, x, (Height - DotSize) / 2, DotSize, DotSize);
        x += DotSize + GapAfterDot;

        x += DrawSeg(g, _name, _bold, AntStyle.Primary, x) + GapAfterDot;
        string tail = HotkeyText();
        if (tail.Length > 0) DrawSeg(g, tail, _small, AntStyle.TextTertiary, x);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bold.Dispose();
            _small.Dispose();
            Region?.Dispose();
            Region = null;
        }
        base.Dispose(disposing);
    }
}
