using System.Diagnostics;

namespace KeyMouseFactory;

/// <summary>
/// 防打字干扰守卫。两个暂停来源，任一成立就暂停连按、放行物理键：
/// 1) 焦点离开游戏进程（游戏窗口还在、只是失焦也算）——覆盖在别的软件里打字；
/// 2) 聊天键切换出的"打字态"——覆盖游戏内自绘聊天框打字（系统认不出输入框，只能认按键）。
///
/// 不自己装钩子：由连发引擎把已过滤 injected 的物理按键转发进来，少一个全局钩子失败点。
/// 焦点按"进程 PID"判断而非窗口句柄：游戏弹子窗口、切输入法时 HWND 会变，进程不变。
/// </summary>
internal sealed class TypingGuard
{
    private readonly int _selfPid = Process.GetCurrentProcess().Id;
    private volatile bool _enabled;
    private int _chatVk = 0x0D;          // 默认 Enter
    private volatile bool _chatTyping;   // 聊天打字态
    private volatile bool _chatKeyDown;  // 聊天键按下沿去重（长按的自动重复不重复 toggle）
    private volatile int _gamePid;       // 锁定的游戏进程 PID，0 = 尚未锁定
    private bool _lastLost;              // 上一次焦点是否离开，用于识别"切回游戏"边沿

    /// <summary>由界面在开启连按前设置：总开关 + 聊天键 VK。</summary>
    public void Configure(bool enabled, int chatVk)
    {
        _enabled = enabled;
        _chatVk = chatVk > 0 ? chatVk : 0x0D;
    }

    /// <summary>开启连按时调用：重置打字态，并锁定当前前台进程作为游戏进程。</summary>
    public void Arm()
    {
        _chatTyping = false;
        _chatKeyDown = false;
        _lastLost = false;
        uint pid = NativeMethods.ForegroundPid();
        // 点界面按钮开启的瞬间前台是本程序，先不锁，等焦点回到游戏后在 ApplyForegroundPid 里补锁
        // （由 Refresh / ObserveDown 调用）
        _gamePid = (pid != 0 && pid != (uint)_selfPid) ? (int)pid : 0;
    }

    public void Disarm()
    {
        _gamePid = 0;
        _chatTyping = false;
        _chatKeyDown = false;
        _lastLost = false;
    }

    /// <summary>焦点是否已离开游戏。未锁定到游戏时不判离开，避免锁定前的瞬间误暂停。</summary>
    private bool FocusLost()
    {
        if (_gamePid == 0) return false;
        uint pid = NativeMethods.ForegroundPid();
        return pid != 0 && pid != (uint)_gamePid;
    }

    /// <summary>
    /// 采样一次前台进程：补足延迟锁定；识别"从别的程序切回游戏"边沿，顺手清掉残留的打字态。
    /// 物理按键与工作线程每轮都会调用——后者保证"切回游戏后不按任何键也能自动恢复连按"。
    /// </summary>
    public void Refresh()
    {
        if (!_enabled) return;
        ApplyForegroundPid(NativeMethods.ForegroundPid());
    }

    /// <summary>用已采样的一次前台 PID 完成延迟锁定与切回复位，避免同一次按键重复查询。</summary>
    private void ApplyForegroundPid(uint pid)
    {
        if (pid == 0 || pid == (uint)_selfPid) return; // 桌面/本程序：维持上次判定
        if (_gamePid == 0)
        {
            _gamePid = (int)pid;
            return;
        }
        bool lost = _gamePid != (int)pid;
        if (_lastLost && !lost) _chatTyping = false; // 切回游戏：聊天框必然已关闭，复位打字态
        _lastLost = lost;
    }

    /// <summary>
    /// 纯函数：三个输入决定当前是否该暂停连按。抽出来供单测覆盖四种组合，
    /// 不依赖真实前台进程。任一起效来源成立即暂停。
    /// </summary>
    internal static bool ShouldPause(bool enabled, bool chatTyping, bool focusLost) =>
        enabled && (chatTyping || focusLost);

    /// <summary>
    /// 当前是否应暂停连按。实时查前台（打字第一下就能判出来），不依赖工作线程轮询。
    /// </summary>
    public bool Paused => ShouldPause(_enabled, _chatTyping, FocusLost());

    /// <summary>
    /// 转发物理按下。返回 true 表示这个键是聊天键、已被消费：引擎须放行它（不能吞，
    /// 否则游戏收不到 Enter，聊天框打不开），且不能当作连按目标触发。
    /// </summary>
    public bool ObserveDown(int vk)
    {
        if (!_enabled) return false;
        uint pid = NativeMethods.ForegroundPid(); // 一次采样：补足锁定 + 判失焦共用
        ApplyForegroundPid(pid);
        // 与原 FocusLost() 同语义：未锁定到游戏、或焦点在桌面时不判失焦
        if (pid != 0 && _gamePid != 0 && pid != (uint)_gamePid) return false;
        if (vk != _chatVk) return false;
        if (_chatKeyDown) return true;              // 长按自动重复：只记一次
        _chatKeyDown = true;
        _chatTyping = !_chatTyping;
        Logger.InfoAsync($"打字态切换：{(_chatTyping ? "进入（连按暂停）" : "退出（连按恢复）")}");
        return true;
    }

    /// <summary>转发物理抬起。只用于清聊天键的按下沿标志。</summary>
    public void ObserveUp(int vk)
    {
        if (vk == _chatVk) _chatKeyDown = false;
    }
}
