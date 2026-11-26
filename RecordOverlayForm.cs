using System.Drawing.Drawing2D;

namespace KeyMouseFactory;

/// <summary>
/// 录制状态悬浮条：主窗口收进托盘后，屏幕上留一条小的置顶提示。
/// 显示红点（闪烁）/「录制中」/ 计时 / 停止热键 / ■ 停止；可拖动，右上角位置默认贴工作区右下角。
/// 悬浮条自身的矩形会作为「排除区域」交给录制器，所以拖它、点■都不会进录制。
/// </summary>
internal sealed class RecordOverlayForm : OverlayBase
{
    private static readonly Rectangle StopRect = new(206, 11, 22, 22);
    private static readonly Color RecordRed = Color.FromArgb(255, 77, 79);
    private static readonly Color RecordRedBg = Color.FromArgb(255, 241, 240);

    private readonly MacroRecorder _recorder;
    private string _hotkey;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private bool _blinkOn = true;
    private bool _stopHover;
    private bool _dragging;
    private int _lastDragTick;     // 拖动节流：整窗移动限到约 60fps
    private Point _dragCursor;
    private Point _dragOrigin;
    private bool _closingSilently;

    /// <summary>点■、或时长触顶时触发；由主窗口统一走停止流程。</summary>
    public event Action? StopRequested;

    /// <summary>拖动结束后触发，调用方据此刷新排除区域。</summary>
    public event Action? BoundsMoved;

    public RecordOverlayForm(MacroRecorder recorder, string stopHotkey) : base(withBoldFont: false)
    {
        _recorder = recorder;
        _hotkey = string.IsNullOrWhiteSpace(stopHotkey) ? "" : stopHotkey.Trim();
        ClientSize = new Size(240, 44);
        PlaceBottomRight();

        UpdateRegion();
        _timer.Tick += (_, _) => OnTickInternal();
        _timer.Start();
    }

    /// <summary>录制条要能点■停止、能拖动，所以不继承只读浮条的鼠标穿透。</summary>
    protected override bool MouseTransparent => false;

    /// <summary>热键被改/被占用自动调整时同步浮窗上的提示。</summary>
    public void SetHotkey(string hotkey)
    {
        _hotkey = string.IsNullOrWhiteSpace(hotkey) ? "" : hotkey.Trim();
        Invalidate();
    }

    /// <summary>整块矩形（外扩在录制器侧做）用于排除录制。</summary>
    public Rectangle ExcludeBounds => new(Left, Top, Width, Height);

    private void OnTickInternal()
    {
        _blinkOn = !_blinkOn;
        Invalidate();
        if (_recorder.LimitReached) StopRequested?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var all = new Rectangle(0, 0, Width - 1, Height - 1);
        AntStyle.FillRounded(g, Color.White, all, 10);
        AntStyle.DrawRounded(g, Color.FromArgb(229, 230, 235), all, 10);

        // 红点（每 200ms 翻转一次明暗，完整周期 400ms）
        using (var dot = new SolidBrush(_blinkOn ? RecordRed : Color.FromArgb(255, 163, 158)))
            g.FillEllipse(dot, 14, (Height - DotSize) / 2, DotSize, DotSize);

        // GetFont 返回进程级缓存的共享字体，禁止 Dispose（与基类独占创建并释放的 Font 不同）
        var boldFont = AntStyle.GetFont(9f, FontStyle.Bold);
        TextRenderer.DrawText(g, "录制中", boldFont, new Rectangle(30, 0, 60, Height), RecordRed,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        TextRenderer.DrawText(g, FormatElapsed(_recorder.ElapsedMs), boldFont,
            new Rectangle(86, 0, 56, Height), AntStyle.TextPri,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 停止热键提示（按 F9 停止）；热键可能因冲突被自动调整，显示的是当前生效值
        if (_hotkey.Length > 0)
            TextRenderer.DrawText(g, $"按 {_hotkey} 停止", SmallFont,
                new Rectangle(144, 0, 58, Height), AntStyle.TextTertiary, Flags);

        // ■ 停止
        AntStyle.FillRounded(g, _stopHover ? RecordRed : RecordRedBg, StopRect, 5);
        using var stopBrush = new SolidBrush(_stopHover ? Color.White : RecordRed);
        g.FillRectangle(stopBrush, StopRect.X + 7, StopRect.Y + 7, 8, 8);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = StopRect.Contains(e.Location);
        if (hover != _stopHover)
        {
            _stopHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.SizeAll;
            Invalidate();
        }
        if (!_dragging) return;
        // 移动整窗会触发布局与整窗重绘，MouseMove 每秒上百次太重；限到约 60fps，松手补最终位置
        int tick = Environment.TickCount;
        if (_lastDragTick != 0 && tick - _lastDragTick < 16) return;
        _lastDragTick = tick;
        MoveToCursor();
    }

    private void MoveToCursor()
    {
        var now = Cursor.Position;
        Location = new Point(_dragOrigin.X + (now.X - _dragCursor.X), _dragOrigin.Y + (now.Y - _dragCursor.Y));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (StopRect.Contains(e.Location))
        {
            StopRequested?.Invoke();
            return;
        }
        _dragging = true;
        _lastDragTick = 0; // 首帧立即响应
        _dragCursor = Cursor.Position;
        _dragOrigin = Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        MoveToCursor();  // 补上节流丢掉的最后一小段
        BoundsMoved?.Invoke();
    }

    /// <summary>Alt+F4 等外部关闭也要变成"停止录制"，否则数据会丢。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingSilently && e.CloseReason != CloseReason.ApplicationExitCall)
        {
            e.Cancel = true;
            StopRequested?.Invoke();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>由主窗口在完成保存后调用，跳过"关闭即停止"的保护逻辑。</summary>
    public void CloseSilently()
    {
        _closingSilently = true;
        Close();
        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            // 字体与 Region 由基类 OverlayBase 统一释放
        }
        base.Dispose(disposing);
    }
}
