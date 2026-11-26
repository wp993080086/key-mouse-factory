using System.Drawing.Drawing2D;

namespace KeyMouseFactory;

/// <summary>
/// 连按/连点状态浮条：引擎开启后主窗口收进托盘，屏幕右下角留一条只读提示
/// （「连按中 · F6」/「连点中 · F8」），与回放浮条同一视觉规格。
///
/// 与回放浮条同理：整块**鼠标穿透**（WS_EX_TRANSPARENT）。连按/连点期间鼠标可能被注入
/// 或用于按住停止，浮条若可点击会吃掉事件；这里只做展示——停止请按热键或用托盘菜单。
/// </summary>
internal sealed class EngineOverlayForm : OverlayBase
{
    private readonly string _name;    // "连按中" / "连点中"
    private string _hotkey;
    private bool _blinkOn = true;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public EngineOverlayForm(string name, string hotkey) : base(withBoldFont: true)
    {
        _name = name;
        _hotkey = string.IsNullOrWhiteSpace(hotkey) ? "" : hotkey.Trim();

        ResizeToContent();
        PlaceBottomRight();

        _timer.Tick += (_, _) => { _blinkOn = !_blinkOn; Invalidate(); };
        _timer.Start();
    }

    /// <summary>连按/连点期间鼠标会被注入，浮条不能吃掉点击；只做展示，停止走热键/托盘。</summary>
    protected override bool MouseTransparent => true;

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
        int w = PadLeft + DotSize + GapAfterDot + Measure(_name, BoldFont!).Width;
        if (tail.Length > 0) w += GapAfterDot + Measure(tail, SmallFont).Width;
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

        x += DrawSeg(g, _name, BoldFont!, AntStyle.Primary, x) + GapAfterDot;
        string tail = HotkeyText();
        if (tail.Length > 0) DrawSeg(g, tail, SmallFont, AntStyle.TextTertiary, x);
    }

}
