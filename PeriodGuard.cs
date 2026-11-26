using System.Runtime.InteropServices;

namespace KeyMouseFactory;

/// <summary>
/// 系统计时精度（1ms）的申请/归还管家，供连按、连点、回放三个引擎共用。
/// <para>
/// Windows 默认计时精度约 15.6ms；连按时「按下保持 20~30ms、抬起 = 间隔 − 按下」的短窗口
/// 需要 1ms 精度才稳（长间隔时抬起可能接近整段间隔）。
/// timeBeginPeriod 是进程级全局状态，且必须和 timeEndPeriod 严格成对：
/// 多还一次会把别的模块申请的精度也还掉，少还一次则永久拉高系统功耗。
/// </para>
/// 内部状态 + 锁保证「重复 Start / 超时 Stop 并发」时也不会破坏配对。
/// </summary>
internal sealed class PeriodGuard
{
    private readonly object _sync = new();
    private bool _raised;

    /// <summary>申请或归还 1ms 精度；内部去重，重复调用无副作用。</summary>
    public void Set(bool on)
    {
        lock (_sync)
        {
            if (_raised == on) return;
            if (on) timeBeginPeriod(1); else timeEndPeriod(1);
            _raised = on;
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);
}
