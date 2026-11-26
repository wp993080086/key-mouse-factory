using System.Drawing.Drawing2D;

namespace KeyMouseFactory;

/// <summary>
/// 右下角状态浮条的公共基类：统一无边框置顶样式、圆角 Region 的创建与释放、
/// 字体创建与释放、计时文本格式化。三个子类（录制 / 回放 / 连按连点）只负责各自的绘制与交互。
/// <para>
/// 浮窗显示期间游戏正在接收注入的输入，所以只读的浮条整块鼠标穿透；录制条需要点■停止和拖动，
/// 通过 <see cref="MouseTransparent"/> 保持可交互。这个开关放在基类是为了统一 CreateParams 的写法，
/// 避免子类各写一遍又漏掉某个标志位。
/// </para>
/// </summary>
internal abstract class OverlayBase : Form
{
    protected const int DotSize = 8;
    protected const int Radius = 10;
    protected const int PadLeft = 14;
    protected const int PadRight = 14;
    protected const int GapAfterDot = 6;
    protected const int MinWidth = 150;

    protected const TextFormatFlags Flags =
        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
    protected const TextFormatFlags MeasFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    /// <summary>是否整块鼠标穿透；只读浮条为 true，录制条为 false。</summary>
    protected virtual bool MouseTransparent => false;

    protected readonly Font SmallFont = new(AntStyle.FontName, 8f);
    protected readonly Font? BoldFont;

    /// <param name="withBoldFont">回放 / 连按连点浮条需要粗体标题；录制条只用小字。</param>
    protected OverlayBase(bool withBoldFont)
    {
        if (withBoldFont) BoldFont = new Font(AntStyle.FontName, 9f, FontStyle.Bold);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Height = 44;
    }

    /// <summary>只读浮条不抢游戏焦点；需交互的录制条返回 false，允许激活以点击/拖动。</summary>
    protected override bool ShowWithoutActivation => MouseTransparent;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (MouseTransparent)
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>贴主显示器工作区右下角，离边缘 24px；构造时定位以免首帧在 (0,0) 闪一下。</summary>
    protected void PlaceBottomRight()
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    /// <summary>
    /// 圆角 Region 决定浮条实际可见/可点击的形状。旧 Region 必须先释放再替换，
    /// 否则每次 resize 都会泄漏一个 GDI 对象（浮窗尺寸随文本变化，长回放能积到上千个）。
    /// </summary>
    protected void UpdateRegion()
    {
        using var path = RoundedPath(new Rectangle(0, 0, Width, Height), Radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected static GraphicsPath RoundedPath(Rectangle r, int radius)
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

    /// <summary>统一计时格式 mm:ss.d（十分之一秒），录制条与回放条一致。</summary>
    protected static string FormatElapsed(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SmallFont.Dispose();
            BoldFont?.Dispose();
            Region?.Dispose();
            Region = null;
        }
        base.Dispose(disposing);
    }
}
