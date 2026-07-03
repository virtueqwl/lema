using System.Runtime.InteropServices;

namespace GameInputTester;

public static class WinApi
{
    public const int WM_HOTKEY = 0x0312;
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint KEYEVENTF_KEYUP    = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>用扫描码发一个完整的"按下-抬起"事件，自动处理扩展键。</summary>
    public static void SendScanCode(ushort scan)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (scan >= 0xE000) flags |= KEYEVENTF_EXTENDEDKEY;

        var kiDown = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags };
        var kiUp   = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags | KEYEVENTF_KEYUP };
        var down   = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = kiDown } };
        var up     = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = kiUp   } };

        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        SendInput(1, new[] { up   }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>US 104 键 → Set 1 扫描码表。涵盖字母/数字/符号/F1-F24/方向/编辑/修饰/小键盘。</summary>
    public static ushort ScanCodeOf(Keys k) => k switch
    {
        // 字母
        Keys.A => 0x1E, Keys.B => 0x30, Keys.C => 0x2E, Keys.D => 0x20,
        Keys.E => 0x12, Keys.F => 0x21, Keys.G => 0x22, Keys.H => 0x23,
        Keys.I => 0x17, Keys.J => 0x24, Keys.K => 0x25, Keys.L => 0x26,
        Keys.M => 0x32, Keys.N => 0x31, Keys.O => 0x18, Keys.P => 0x19,
        Keys.Q => 0x10, Keys.R => 0x13, Keys.S => 0x1F, Keys.T => 0x14,
        Keys.U => 0x16, Keys.V => 0x2F, Keys.W => 0x11, Keys.X => 0x2D,
        Keys.Y => 0x15, Keys.Z => 0x2C,

        // 数字顶行
        Keys.D0 => 0x0B, Keys.D1 => 0x02, Keys.D2 => 0x03, Keys.D3 => 0x04,
        Keys.D4 => 0x05, Keys.D5 => 0x06, Keys.D6 => 0x07, Keys.D7 => 0x08,
        Keys.D8 => 0x09, Keys.D9 => 0x0A,

        // 符号
        Keys.Oemtilde         => 0x29, // ` ~
        Keys.OemMinus         => 0x0C, // - _
        Keys.Oemplus          => 0x0D, // = +
        Keys.OemOpenBrackets  => 0x1A, // [ {
        Keys.OemCloseBrackets => 0x1B, // ] }
        Keys.OemBackslash     => 0x2B, // \ |
        Keys.OemSemicolon     => 0x27, // ; :
        Keys.OemQuotes        => 0x28, // ' "
        Keys.Oemcomma         => 0x33, // , <
        Keys.OemPeriod        => 0x34, // . >
        Keys.OemQuestion      => 0x35, // / ?

        // 功能键
        Keys.F1  => 0x3B, Keys.F2  => 0x3C, Keys.F3  => 0x3D, Keys.F4  => 0x3E,
        Keys.F5  => 0x3F, Keys.F6  => 0x40, Keys.F7  => 0x41, Keys.F8  => 0x42,
        Keys.F9  => 0x43, Keys.F10 => 0x44, Keys.F11 => 0x57, Keys.F12 => 0x58,
        Keys.F13 => 0x64, Keys.F14 => 0x65, Keys.F15 => 0x66, Keys.F16 => 0x67,
        Keys.F17 => 0x68, Keys.F18 => 0x69, Keys.F19 => 0x6A, Keys.F20 => 0x6B,
        Keys.F21 => 0x6C, Keys.F22 => 0x6D, Keys.F23 => 0x6E, Keys.F24 => 0x6F,

        // 修饰键
        Keys.LShiftKey   => 0x2A, Keys.RShiftKey   => 0x36,
        Keys.LControlKey => 0x1D, Keys.RControlKey => 0xE01D, // 扩展
        Keys.LMenu       => 0x38, Keys.RMenu       => 0xE038, // 扩展
        Keys.CapsLock    => 0x3A, Keys.NumLock     => 0x45, Keys.Scroll      => 0x46,

        // 编辑 / 方向
        Keys.Escape  => 0x01, Keys.Tab  => 0x0F, Keys.Space => 0x39,
        Keys.Return  => 0x1C, Keys.Back => 0x0E,
        Keys.Up      => 0xE048, Keys.Down  => 0xE050,
        Keys.Left    => 0xE04B, Keys.Right => 0xE04D,
        Keys.Home    => 0xE047, Keys.End   => 0xE04F,
        Keys.Insert  => 0xE052, Keys.Delete => 0xE053,
        Keys.PageUp  => 0xE049, Keys.PageDown => 0xE051,
        Keys.Print   => 0xE02A, Keys.Pause => 0xE11D45,

        // 小键盘
        Keys.NumPad0 => 0x52, Keys.NumPad1 => 0x4F, Keys.NumPad2 => 0x50,
        Keys.NumPad3 => 0x51, Keys.NumPad4 => 0x4B, Keys.NumPad5 => 0x4C,
        Keys.NumPad6 => 0x4D, Keys.NumPad7 => 0x47, Keys.NumPad8 => 0x48,
        Keys.NumPad9 => 0x49,
        Keys.Multiply => 0x37, Keys.Add => 0x4E, Keys.Separator => 0xE0AC,
        Keys.Subtract => 0x4A, Keys.Decimal => 0x53, Keys.Divide => 0xE035,

        _ => 0x02
    };
}
