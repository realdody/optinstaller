using System;
using System.Runtime.InteropServices;

namespace Optinstaller.Platform;

internal static class Win32Native
{
    internal static readonly nint DarkBackgroundBrush = CreateSolidBrush(ToColorRef(43, 46, 49));

    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_OWNDC = 0x0020;

    public const int SW_SHOWDEFAULT = 10;
    public const int SW_SHOW = 5;
    public const uint PM_REMOVE = 0x0001;

    public const int GWLP_USERDATA = -21;

    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_CAPTION_COLOR = 35;
    public const uint DWMWA_TEXT_COLOR = 36;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_SIZING = 0x0214;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_EXITSIZEMOVE = 0x0232;

    public const uint SIZE_RESTORED = 0;
    public const uint SIZE_MINIMIZED = 1;

    public const uint SC_KEYMENU = 0xF100;

    public const int IDC_ARROW = 32512;
    public const int IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const uint LR_DEFAULTSIZE = 0x0040;
    public const int SM_CXICON = 11;
    public const int SM_CYICON = 12;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;
    public static readonly nuint ICON_SMALL = 0;
    public static readonly nuint ICON_BIG = 1;

    public const int VK_TAB = 0x09;
    public const int VK_LEFT = 0x25;
    public const int VK_RIGHT = 0x27;
    public const int VK_UP = 0x26;
    public const int VK_DOWN = 0x28;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_HOME = 0x24;
    public const int VK_END = 0x23;
    public const int VK_INSERT = 0x2D;
    public const int VK_DELETE = 0x2E;
    public const int VK_BACK = 0x08;
    public const int VK_SPACE = 0x20;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_OEM_7 = 0xDE;
    public const int VK_OEM_COMMA = 0xBC;
    public const int VK_OEM_MINUS = 0xBD;
    public const int VK_OEM_PERIOD = 0xBE;
    public const int VK_OEM_2 = 0xBF;
    public const int VK_OEM_1 = 0xBA;
    public const int VK_OEM_PLUS = 0xBB;
    public const int VK_OEM_4 = 0xDB;
    public const int VK_OEM_5 = 0xDC;
    public const int VK_OEM_6 = 0xDD;
    public const int VK_OEM_3 = 0xC0;
    public const int VK_CAPITAL = 0x14;
    public const int VK_SCROLL = 0x91;
    public const int VK_NUMLOCK = 0x90;
    public const int VK_SNAPSHOT = 0x2C;
    public const int VK_PAUSE = 0x13;
    public const int VK_NUMPAD0 = 0x60;
    public const int VK_NUMPAD1 = 0x61;
    public const int VK_NUMPAD2 = 0x62;
    public const int VK_NUMPAD3 = 0x63;
    public const int VK_NUMPAD4 = 0x64;
    public const int VK_NUMPAD5 = 0x65;
    public const int VK_NUMPAD6 = 0x66;
    public const int VK_NUMPAD7 = 0x67;
    public const int VK_NUMPAD8 = 0x68;
    public const int VK_NUMPAD9 = 0x69;
    public const int VK_DECIMAL = 0x6E;
    public const int VK_DIVIDE = 0x6F;
    public const int VK_MULTIPLY = 0x6A;
    public const int VK_SUBTRACT = 0x6D;
    public const int VK_ADD = 0x6B;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_LMENU = 0xA4;
    public const int VK_LWIN = 0x5B;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_RMENU = 0xA5;
    public const int VK_RWIN = 0x5C;
    public const int VK_APPS = 0x5D;
    public const int VK_F1 = 0x70;
    public const int VK_F2 = 0x71;
    public const int VK_F3 = 0x72;
    public const int VK_F4 = 0x73;
    public const int VK_F5 = 0x74;
    public const int VK_F6 = 0x75;
    public const int VK_F7 = 0x76;
    public const int VK_F8 = 0x77;
    public const int VK_F9 = 0x78;
    public const int VK_F10 = 0x79;
    public const int VK_F11 = 0x7A;
    public const int VK_F12 = 0x7B;
    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int VK_MENU = 0x12;

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    internal delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREATESTRUCTW
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public nint hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public int style;
        public nint lpszName;
        public nint lpszClass;
        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        public nint hdc;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fErase;
        public RECT rcPaint;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint exStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowText(nint hWnd, string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SendMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateSolidBrush(uint color);

    internal static void ApplyDarkWindowTheme(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var enabled = 1;
        var intSize = (uint)Marshal.SizeOf<int>();
        var result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, intSize);
        if (result != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref enabled, intSize);
        }

        var captionColor = ToColorRef(43, 46, 49);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, (uint)Marshal.SizeOf<uint>());

        var textColor = ToColorRef(239, 242, 234);
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, (uint)Marshal.SizeOf<uint>());

        var borderColor = ToColorRef(87, 99, 70);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, (uint)Marshal.SizeOf<uint>());
    }

    private static uint ToColorRef(byte red, byte green, byte blue)
    {
        return (uint)(red | (green << 8) | (blue << 16));
    }

    public static int GetXFromLParam(nint lParam) => (short)((long)lParam & 0xFFFF);

    public static int GetYFromLParam(nint lParam) => (short)(((long)lParam >> 16) & 0xFFFF);

    public static short GetWheelDeltaWParam(nuint wParam) => unchecked((short)(((ulong)wParam >> 16) & 0xFFFF));
}
