using System.Runtime.InteropServices;
using static GameInputTester.WinApi;

namespace GameInputTester;

public enum RecordEvent { Down, Up }

public record RecordedKey(string LogicalKey, Keys PhysicalKey, RecordEvent Event, long TimestampMs);

public class Recorder : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const uint LLKHF_UP = 0x80;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private LowLevelKeyboardProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private long _startMs;

    public HashSet<Keys> WatchKeys { get; set; } = new();
    public Func<Keys, string>? LogicalNameOf { get; set; }
    public Action<RecordedKey>? OnEvent { get; set; }

    public void Start()
    {
        _startMs = Environment.TickCount64;
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("SetWindowsHookEx 失败，可能被反作弊/AV 拦截");
    }

    public void Stop() => UnhookWindowsHookEx(_hookId);
    public void Dispose() => Stop();

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 钩子回调必须尽快返回，只做最少量工作：解析 + 拷贝事件 + 派发。
        // 任何 DataTable / WinForms 控件访问都在派发后由 OnEvent 接收方在 UI 线程做。
        if (nCode == HC_ACTION)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var key = (Keys)info.vkCode;
            if (WatchKeys.Contains(key))
            {
                bool isDown = (info.flags & LLKHF_UP) == 0;
                // 拷贝关键字段到局部变量，避免闭包捕获 lParam
                var physicalKey = key;
                var logical = LogicalNameOf?.Invoke(key) ?? key.ToString();
                var recordEvt = isDown ? RecordEvent.Down : RecordEvent.Up;
                long ts = Environment.TickCount64 - _startMs;
                try
                {
                    OnEvent?.Invoke(new RecordedKey(logical, physicalKey, recordEvt, ts));
                }
                catch
                {
                    // OnEvent 抛异常会被 OS 当作钩子错误，可能导致整个钩子被卸载
                    // 这里必须吞掉
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
