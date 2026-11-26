using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace KeyMouseFactory;

/// <summary>
/// 轻量日志：写入 AppPaths.DataDir 下的 error.log（默认 exe 同目录，无写权限时回退 %APPDATA%\KeyMouseFactory）。
/// 追加写入，超过 200 条时整理一次（保留最新的 200 条），不做每次全量重写。
/// </summary>
internal static class Logger
{
    private static readonly object Sync = new();
    private static readonly ConcurrentQueue<string> Pending = new();
    private static System.Threading.Timer? _flushTimer;
    private static int _timerStarted;
    private const int MaxEntries = 200;
    private static int _lineCount = -1; // -1 = 还没读过盘；避免每次写都全量读一遍
    private static int _reported;       // 异常连环触发时只弹一次窗口

    public static string LogPath => Path.Combine(AppPaths.DataDir, "error.log");

    /// <summary>挂接全局异常捕获（UI 线程 + AppDomain），需在 Main 最先调用。</summary>
    public static void Hook()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report("AppDomain", e.ExceptionObject as Exception);
    }

    /// <summary>先落盘，再弹一次异常窗口；弹窗自身失败不能盖掉原来的异常。</summary>
    internal static void Report(string context, Exception? ex)
    {
        Error(context, ex);
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;
        try { CrashReportForm.Show(context, ex, LogPath); }
        catch { /* 日志已经写下了，弹窗失败就算了 */ }
    }

    /// <summary>
    /// 用系统默认程序打开日志，文件不存在时先建一个空文件。
    /// 返回 null 表示成功；调用方负责把错误显示给用户。
    /// </summary>
    public static string EnsureLogFile()
    {
        if (!File.Exists(LogPath)) File.WriteAllText(LogPath, "");
        return LogPath;
    }

    public static string? OpenLog()
    {
        try
        {
            EnsureLogFile();
            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Error("OpenLog", ex);
            return ex.Message;
        }
    }

    public static void Error(string context, Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] [").Append(context).Append("] ");
        if (ex == null)
        {
            sb.Append("(unknown non-exception object)");
        }
        else
        {
            sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace)) sb.Append('\n').Append(ex.StackTrace);
            if (ex.InnerException != null)
                sb.Append("\n--> ").Append(ex.InnerException.GetType().Name).Append(": ").Append(ex.InnerException.Message);
        }
        Write(sb.ToString());
    }

    public static void Info(string message) =>
        Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Info] {message}");

    /// <summary>
    /// 钩子回调等实时路径专用：只把日志排队，由后台线程每 200ms 批量落盘，
    /// 调用线程完全不碰磁盘（避免阻塞 LowLevelHooksTimeout 被系统摘钩）。
    /// 进程退出前冲刷队列；崩溃日志仍走同步的 Error/Write。
    /// </summary>
    public static void InfoAsync(string message)
    {
        Pending.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Info] {message}");
        if (Volatile.Read(ref _timerStarted) != 0) return;
        if (Interlocked.Exchange(ref _timerStarted, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushPending();
        _flushTimer = new System.Threading.Timer(_ => FlushPending(), null, 200, 200);
    }

    private static void FlushPending()
    {
        if (Pending.IsEmpty) return;
        var sb = new StringBuilder();
        int count = 0;
        while (Pending.TryDequeue(out var line))
        {
            sb.Append(line).Append(Environment.NewLine);
            count++;
        }
        if (count > 0) WriteCore(sb.ToString(), count);
    }

    public static void Write(string entry) => WriteCore(entry + Environment.NewLine, 1);

    private static void WriteCore(string text, int lines)
    {
        try
        {
            lock (Sync)
            {
                if (_lineCount < 0)
                    _lineCount = File.Exists(LogPath) ? File.ReadAllLines(LogPath).Length : 0;
                File.AppendAllText(LogPath, text);
                _lineCount += lines;
                if (_lineCount > MaxEntries) Trim();
            }
        }
        catch
        {
            // 日志本身失败时静默，不影响主流程
        }
    }

    /// <summary>超过上限时整体整理一次，只留最新 MaxEntries 条。</summary>
    private static void Trim()
    {
        var lines = File.ReadAllLines(LogPath).ToList();
        if (lines.Count <= MaxEntries)
        {
            _lineCount = lines.Count;
            return;
        }
        lines.RemoveRange(0, lines.Count - MaxEntries);
        File.WriteAllLines(LogPath, lines);
        _lineCount = lines.Count;
    }
}
