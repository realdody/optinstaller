using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Optinstaller.Models;
using Optinstaller.Platform;
using Optinstaller.Services;
using Optinstaller.ViewModels;

namespace Optinstaller.UI;

public sealed class OptinstallerImGuiApp : IDisposable
{
    private const int MinMainClientWidth = 1100;
    private const int MinMainClientHeight = 720;
    private const int MinGameDetailsClientWidth = 840;
    private const int MinGameDetailsClientHeight = 760;
    private const string ConfirmationPopupIdPrefix = "ConfirmationPrompt##";
    private const ImGuiWindowFlags PanelWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    private const ImGuiWindowFlags ScrollablePanelWindowFlags = ImGuiWindowFlags.None;
    private const ImGuiChildFlags PaddedPanelChildFlags = ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding;

    private static readonly Vector4 InfoColor = new(0.45f, 0.69f, 0.34f, 1f);
    private static readonly Vector4 SuccessColor = new(0.57f, 0.78f, 0.39f, 1f);
    private static readonly Vector4 WarningColor = new(0.86f, 0.68f, 0.27f, 1f);
    private static readonly Vector4 ErrorColor = new(0.82f, 0.42f, 0.36f, 1f);
    private static readonly Vector4 MutedTextColor = new(0.70f, 0.72f, 0.68f, 1f);
    private static readonly Vector4 WindowBackgroundColor = new(0.17f, 0.18f, 0.19f, 1f);
    private static readonly Vector4 PanelBackgroundColor = new(0.21f, 0.22f, 0.23f, 1f);
    private static readonly Vector4 PanelRaisedBackgroundColor = new(0.24f, 0.25f, 0.26f, 1f);
    private static readonly Vector4 PanelBorderColor = new(0.34f, 0.39f, 0.31f, 1f);
    private static readonly Vector4 PrimaryTextColor = new(0.94f, 0.95f, 0.92f, 1f);
    private static readonly ConfigChoice[] BooleanChoices =
    {
        new("false", "Disabled"),
        new("true", "Enabled"),
    };
    private static readonly ConfigChoice[] FrameGenerationInputChoices =
    {
        new("nofg", "Disabled"),
        new("dlssg", "DLSSG via Streamline"),
        new("nukems", "Nukem's DLSSG"),
        new("fsrfg", "FSR FG"),
        new("upscaler", "OptiFG (Upscaler)"),
        new("fsrfg30", "FSR FG 3.0"),
    };
    private static readonly ConfigChoice[] FrameGenerationOutputChoices =
    {
        new("nofg", "Disabled"),
        new("fsrfg", "FSR FG 3/4"),
        new("xefg", "XeFG"),
        new("nukems", "Nukem's FSR FG"),
    };
    private static readonly ConfigChoice[] LogLevelChoices =
    {
        new("0", "Trace"),
        new("1", "Debug"),
        new("2", "Info"),
        new("3", "Warning"),
        new("4", "Error"),
    };
    private static readonly ShortcutCaptureKey[] ShortcutCaptureKeys =
    {
        new(ImGuiKey.Tab, Win32Native.VK_TAB),
        new(ImGuiKey.LeftArrow, Win32Native.VK_LEFT),
        new(ImGuiKey.RightArrow, Win32Native.VK_RIGHT),
        new(ImGuiKey.UpArrow, Win32Native.VK_UP),
        new(ImGuiKey.DownArrow, Win32Native.VK_DOWN),
        new(ImGuiKey.PageUp, Win32Native.VK_PRIOR),
        new(ImGuiKey.PageDown, Win32Native.VK_NEXT),
        new(ImGuiKey.Home, Win32Native.VK_HOME),
        new(ImGuiKey.End, Win32Native.VK_END),
        new(ImGuiKey.Insert, Win32Native.VK_INSERT),
        new(ImGuiKey.Delete, Win32Native.VK_DELETE),
        new(ImGuiKey.Backspace, Win32Native.VK_BACK),
        new(ImGuiKey.Space, Win32Native.VK_SPACE),
        new(ImGuiKey.Enter, Win32Native.VK_RETURN),
        new(ImGuiKey.Apostrophe, Win32Native.VK_OEM_7),
        new(ImGuiKey.Comma, Win32Native.VK_OEM_COMMA),
        new(ImGuiKey.Minus, Win32Native.VK_OEM_MINUS),
        new(ImGuiKey.Period, Win32Native.VK_OEM_PERIOD),
        new(ImGuiKey.Slash, Win32Native.VK_OEM_2),
        new(ImGuiKey.Semicolon, Win32Native.VK_OEM_1),
        new(ImGuiKey.Equal, Win32Native.VK_OEM_PLUS),
        new(ImGuiKey.LeftBracket, Win32Native.VK_OEM_4),
        new(ImGuiKey.Backslash, Win32Native.VK_OEM_5),
        new(ImGuiKey.RightBracket, Win32Native.VK_OEM_6),
        new(ImGuiKey.GraveAccent, Win32Native.VK_OEM_3),
        new(ImGuiKey.CapsLock, Win32Native.VK_CAPITAL),
        new(ImGuiKey.ScrollLock, Win32Native.VK_SCROLL),
        new(ImGuiKey.NumLock, Win32Native.VK_NUMLOCK),
        new(ImGuiKey.PrintScreen, Win32Native.VK_SNAPSHOT),
        new(ImGuiKey.Pause, Win32Native.VK_PAUSE),
        new(ImGuiKey.Keypad0, Win32Native.VK_NUMPAD0),
        new(ImGuiKey.Keypad1, Win32Native.VK_NUMPAD1),
        new(ImGuiKey.Keypad2, Win32Native.VK_NUMPAD2),
        new(ImGuiKey.Keypad3, Win32Native.VK_NUMPAD3),
        new(ImGuiKey.Keypad4, Win32Native.VK_NUMPAD4),
        new(ImGuiKey.Keypad5, Win32Native.VK_NUMPAD5),
        new(ImGuiKey.Keypad6, Win32Native.VK_NUMPAD6),
        new(ImGuiKey.Keypad7, Win32Native.VK_NUMPAD7),
        new(ImGuiKey.Keypad8, Win32Native.VK_NUMPAD8),
        new(ImGuiKey.Keypad9, Win32Native.VK_NUMPAD9),
        new(ImGuiKey.KeypadDecimal, Win32Native.VK_DECIMAL),
        new(ImGuiKey.KeypadDivide, Win32Native.VK_DIVIDE),
        new(ImGuiKey.KeypadMultiply, Win32Native.VK_MULTIPLY),
        new(ImGuiKey.KeypadSubtract, Win32Native.VK_SUBTRACT),
        new(ImGuiKey.KeypadAdd, Win32Native.VK_ADD),
        new(ImGuiKey.Menu, Win32Native.VK_APPS),
        new(ImGuiKey.F1, Win32Native.VK_F1),
        new(ImGuiKey.F2, Win32Native.VK_F2),
        new(ImGuiKey.F3, Win32Native.VK_F3),
        new(ImGuiKey.F4, Win32Native.VK_F4),
        new(ImGuiKey.F5, Win32Native.VK_F5),
        new(ImGuiKey.F6, Win32Native.VK_F6),
        new(ImGuiKey.F7, Win32Native.VK_F7),
        new(ImGuiKey.F8, Win32Native.VK_F8),
        new(ImGuiKey.F9, Win32Native.VK_F9),
        new(ImGuiKey.F10, Win32Native.VK_F10),
        new(ImGuiKey.F11, Win32Native.VK_F11),
        new(ImGuiKey.F12, Win32Native.VK_F12),
        new(ImGuiKey._0, '0'),
        new(ImGuiKey._1, '1'),
        new(ImGuiKey._2, '2'),
        new(ImGuiKey._3, '3'),
        new(ImGuiKey._4, '4'),
        new(ImGuiKey._5, '5'),
        new(ImGuiKey._6, '6'),
        new(ImGuiKey._7, '7'),
        new(ImGuiKey._8, '8'),
        new(ImGuiKey._9, '9'),
        new(ImGuiKey.A, 'A'),
        new(ImGuiKey.B, 'B'),
        new(ImGuiKey.C, 'C'),
        new(ImGuiKey.D, 'D'),
        new(ImGuiKey.E, 'E'),
        new(ImGuiKey.F, 'F'),
        new(ImGuiKey.G, 'G'),
        new(ImGuiKey.H, 'H'),
        new(ImGuiKey.I, 'I'),
        new(ImGuiKey.J, 'J'),
        new(ImGuiKey.K, 'K'),
        new(ImGuiKey.L, 'L'),
        new(ImGuiKey.M, 'M'),
        new(ImGuiKey.N, 'N'),
        new(ImGuiKey.O, 'O'),
        new(ImGuiKey.P, 'P'),
        new(ImGuiKey.Q, 'Q'),
        new(ImGuiKey.R, 'R'),
        new(ImGuiKey.S, 'S'),
        new(ImGuiKey.T, 'T'),
        new(ImGuiKey.U, 'U'),
        new(ImGuiKey.V, 'V'),
        new(ImGuiKey.W, 'W'),
        new(ImGuiKey.X, 'X'),
        new(ImGuiKey.Y, 'Y'),
        new(ImGuiKey.Z, 'Z'),
    };
    private static string? _defaultDxgiConfigValue;
    private static readonly Win32Native.WndProcDelegate WindowProcedureDelegate = WindowProcedure;
    private static readonly string AppIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.ico");
    private static readonly object AppIconSync = new();
    private static nint _largeAppIcon;
    private static nint _smallAppIcon;
    private static bool _appIconsLoaded;

    private readonly UiSynchronizationContext _syncContext;
    private readonly MainWindowViewModel _mainViewModel = new();
    private readonly string _windowClassName = $"OptinstallerWindowClass_{Guid.NewGuid():N}";
    private const string WindowTitle = "OptiManager";
    private GCHandle _selfHandle;
    private nint _hInstance;
    private nint _hwnd;
    private bool _classRegistered;
    private bool _isRunning;
    private bool _isMinimized;
    private bool _isInSizeMove;
    private bool _isRenderingFrame;
    private int _windowWidth = 1440;
    private int _windowHeight = 900;

    private Dx11ImGuiRenderer? _renderer;
    private bool _disposed;

    private AppPage _currentPage = AppPage.Dashboard;
    private NotificationKind _notificationKind = NotificationKind.Info;
    private string? _notificationMessage;
    private DateTime _notificationExpiresAt;

    private string _dashboardSearchQuery = string.Empty;
    private string? _selectedGamePath;
    private string? _selectedVersionTag;
    private readonly Dictionary<string, float> _animationValues = new();
    private float _uiTime;

    private ConfirmationDialogState? _confirmation;
    private ConfirmationHost? _confirmationHost;
    private Vector2? _confirmationPopupPosition;
    private Vector2 _confirmationPopupSize = new(460f, 0f);
    private Vector2 _confirmationPopupDragOffset;
    private UpdateDialogState? _updateDialog;
    private ComponentDllDialogState? _componentDllDialog;
    private ConfigDialogState? _configDialog;
    private InstallationDialogState? _installationDialog;
    private GameDetailsDialogState? _gameDetailsDialog;

    private bool _isDraggingConfirmationPopup;
    private bool _openConfirmationPopup;
    private NativeWindowHost? _updateWindow;
    private NativeWindowHost? _componentDllWindow;
    private NativeWindowHost? _configWindow;
    private NativeWindowHost? _installationWindow;
    private NativeWindowHost? _gameDetailsWindow;

    public OptinstallerImGuiApp(UiSynchronizationContext syncContext)
    {
        _syncContext = syncContext;
        _selfHandle = GCHandle.Alloc(this);
        _hInstance = Win32Native.GetModuleHandle(null);

        try
        {
            RegisterWindowClass();
            CreateWindow();
            _renderer = new Dx11ImGuiRenderer(_hwnd, _windowWidth, _windowHeight, ConfigureImGuiIo, LoadFonts, ApplyTheme);
        }
        catch
        {
            PreserveImGuiContext(Dispose);
            throw;
        }
    }

    public void Run()
    {
        _isRunning = true;
        Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOWDEFAULT);
        Win32Native.UpdateWindow(_hwnd);

        _ = InitializeAsync();

        var stopwatch = Stopwatch.StartNew();
        var lastFrameTime = stopwatch.Elapsed;

        while (_isRunning)
        {
            while (Win32Native.PeekMessage(out var message, IntPtr.Zero, 0, 0, Win32Native.PM_REMOVE))
            {
                if (message.message == Win32Native.WM_QUIT)
                {
                    _isRunning = false;
                    break;
                }

                Win32Native.TranslateMessage(ref message);
                Win32Native.DispatchMessage(ref message);
            }

            _syncContext.Pump();
            CleanupClosedNativeWindows();

            if (!_isRunning)
            {
                break;
            }

            var currentFrameTime = stopwatch.Elapsed;
            var delta = (float)(currentFrameTime - lastFrameTime).TotalSeconds;
            lastFrameTime = currentFrameTime;

            var renderedAnyWindow = false;
            if (!_isMinimized && !_isInSizeMove)
            {
                renderedAnyWindow = RenderFrame(delta);
            }

            renderedAnyWindow |= RenderNativeWindow(_gameDetailsWindow, delta);
            renderedAnyWindow |= RenderNativeWindow(_updateWindow, delta);
            renderedAnyWindow |= RenderNativeWindow(_componentDllWindow, delta);
            renderedAnyWindow |= RenderNativeWindow(_configWindow, delta);
            renderedAnyWindow |= RenderNativeWindow(_installationWindow, delta);

            if (!renderedAnyWindow)
            {
                Thread.Sleep(16);
            }
        }
    }

    private bool RenderFrame(float delta, bool enableVsync = true)
    {
        if (_renderer == null || _isRenderingFrame)
        {
            return false;
        }

        _isRenderingFrame = true;
        try
        {
            _uiTime += delta;
            if (!_renderer.BeginFrame(delta, _windowWidth, _windowHeight))
            {
                return false;
            }

            RenderUi();
            _renderer.Render(WindowBackgroundColor, enableVsync);
            return true;
        }
        finally
        {
            _isRenderingFrame = false;
        }
    }

    private void ResizeMainRenderer(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _windowWidth = width;
        _windowHeight = height;
        _renderer?.Resize(width, height);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeNativeWindows();
        _renderer?.Dispose();

        if (_hwnd != IntPtr.Zero)
        {
            Win32Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_classRegistered)
        {
            Win32Native.UnregisterClass(_windowClassName, _hInstance);
            _classRegistered = false;
        }

        Win32Native.ReleaseDarkBackgroundBrush();
        ReleaseAppIcons();

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            SetNotification($"Initialization failed: {ex.Message}", NotificationKind.Error);
        }
    }

    private void RegisterWindowClass()
    {
        var (largeIcon, smallIcon) = GetAppIcons();
        var windowClass = new Win32Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEXW>(),
            style = Win32Native.CS_HREDRAW | Win32Native.CS_VREDRAW | Win32Native.CS_OWNDC,
            lpfnWndProc = WindowProcedureDelegate,
            hInstance = _hInstance,
            hIcon = largeIcon,
            hCursor = Win32Native.LoadCursor(IntPtr.Zero, (nint)Win32Native.IDC_ARROW),
            hbrBackground = Win32Native.DarkBackgroundBrush,
            lpszClassName = _windowClassName,
            hIconSm = smallIcon,
        };

        if (Win32Native.RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        _classRegistered = true;
    }

    private void CreateWindow()
    {
        var windowRect = new Win32Native.RECT
        {
            Left = 0,
            Top = 0,
            Right = _windowWidth,
            Bottom = _windowHeight,
        };

        Win32Native.AdjustWindowRectEx(ref windowRect, Win32Native.WS_OVERLAPPEDWINDOW, false, 0);

        _hwnd = Win32Native.CreateWindowEx(
            0,
            _windowClassName,
            WindowTitle,
            Win32Native.WS_OVERLAPPEDWINDOW | Win32Native.WS_VISIBLE,
            Win32Native.CW_USEDEFAULT,
            Win32Native.CW_USEDEFAULT,
            windowRect.Right - windowRect.Left,
            windowRect.Bottom - windowRect.Top,
            IntPtr.Zero,
            IntPtr.Zero,
            _hInstance,
            GCHandle.ToIntPtr(_selfHandle));

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        Win32Native.ApplyDarkWindowTheme(_hwnd);
        ApplyWindowIcons(_hwnd);
        Win32Native.SetWindowText(_hwnd, WindowTitle);

        if (Win32Native.GetClientRect(_hwnd, out var clientRect))
        {
            _windowWidth = Math.Max(1, clientRect.Right - clientRect.Left);
            _windowHeight = Math.Max(1, clientRect.Bottom - clientRect.Top);
        }
    }

    private static nint WindowProcedure(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == Win32Native.WM_NCCREATE)
        {
            var createStruct = Marshal.PtrToStructure<Win32Native.CREATESTRUCTW>(lParam);
            Win32Native.SetWindowLongPtr(hwnd, Win32Native.GWLP_USERDATA, createStruct.lpCreateParams);
        }

        var userData = Win32Native.GetWindowLongPtr(hwnd, Win32Native.GWLP_USERDATA);
        if (userData != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is OptinstallerImGuiApp app)
            {
                return app.HandleWindowMessage(hwnd, msg, wParam, lParam);
            }
        }

        return msg == Win32Native.WM_NCCREATE ? 1 : Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private nint HandleWindowMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (_renderer?.HandleMessage(msg, wParam, lParam) == true)
        {
            return 0;
        }

        switch (msg)
        {
            case Win32Native.WM_ERASEBKGND:
                return _isInSizeMove ? Win32Native.DefWindowProc(hwnd, msg, wParam, lParam) : 1;

            case Win32Native.WM_SYSCOMMAND when ((uint)wParam & 0xFFF0) == Win32Native.SC_KEYMENU:
                return 0;

            case Win32Native.WM_GETMINMAXINFO:
                var minMaxInfo = Marshal.PtrToStructure<Win32Native.MINMAXINFO>(lParam);
                var minWindowRect = new Win32Native.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = MinMainClientWidth,
                    Bottom = MinMainClientHeight,
                };

                Win32Native.AdjustWindowRectEx(ref minWindowRect, Win32Native.WS_OVERLAPPEDWINDOW, false, 0);
                minMaxInfo.ptMinTrackSize.X = minWindowRect.Right - minWindowRect.Left;
                minMaxInfo.ptMinTrackSize.Y = minWindowRect.Bottom - minWindowRect.Top;
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
                return 0;

            case Win32Native.WM_ENTERSIZEMOVE:
                _isInSizeMove = true;
                return 0;

            case Win32Native.WM_SIZING:
                Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                if (!_isRenderingFrame)
                {
                    Win32Native.UpdateWindow(hwnd);
                }
                break;

            case Win32Native.WM_EXITSIZEMOVE:
                _isInSizeMove = false;
                Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                Win32Native.UpdateWindow(hwnd);
                return 0;

            case Win32Native.WM_SIZE:
                if ((uint)wParam == Win32Native.SIZE_MINIMIZED)
                {
                    _isMinimized = true;
                    return 0;
                }

                _isMinimized = false;
                var width = Win32Native.GetXFromLParam(lParam);
                var height = Win32Native.GetYFromLParam(lParam);
                if (width > 0 && height > 0)
                {
                    ResizeMainRenderer(width, height);
                    Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                    if (_isInSizeMove)
                    {
                        Win32Native.UpdateWindow(hwnd);
                    }
                }
                return 0;

            case Win32Native.WM_PAINT:
                var paint = Win32Native.BeginPaint(hwnd, out var paintStruct);
                if (paint != IntPtr.Zero)
                {
                    if (!_isMinimized)
                    {
                        RenderFrame(1f / 60f);
                    }

                    Win32Native.EndPaint(hwnd, ref paintStruct);
                }
                return 0;

            case Win32Native.WM_CLOSE:
                _isRunning = false;
                Win32Native.DestroyWindow(hwnd);
                return 0;

            case Win32Native.WM_DESTROY:
                Win32Native.PostQuitMessage(0);
                return 0;
        }

        return Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static (nint LargeIcon, nint SmallIcon) GetAppIcons()
    {
        EnsureAppIconsLoaded();
        return (_largeAppIcon, _smallAppIcon);
    }

    private static void ApplyWindowIcons(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var (largeIcon, smallIcon) = GetAppIcons();
        if (largeIcon != 0)
        {
            Win32Native.SendMessage(hwnd, Win32Native.WM_SETICON, Win32Native.ICON_BIG, largeIcon);
        }

        if (smallIcon != 0)
        {
            Win32Native.SendMessage(hwnd, Win32Native.WM_SETICON, Win32Native.ICON_SMALL, smallIcon);
        }
    }

    private static void EnsureAppIconsLoaded()
    {
        if (_appIconsLoaded)
        {
            return;
        }

        lock (AppIconSync)
        {
            if (_appIconsLoaded)
            {
                return;
            }

            _appIconsLoaded = true;
            if (!File.Exists(AppIconPath))
            {
                return;
            }

            _largeAppIcon = LoadAppIcon(Win32Native.SM_CXICON, Win32Native.SM_CYICON);
            _smallAppIcon = LoadAppIcon(Win32Native.SM_CXSMICON, Win32Native.SM_CYSMICON);

            if (_smallAppIcon == 0)
            {
                _smallAppIcon = _largeAppIcon;
            }
        }
    }

    private static nint LoadAppIcon(int widthMetric, int heightMetric)
    {
        var width = Math.Max(0, Win32Native.GetSystemMetrics(widthMetric));
        var height = Math.Max(0, Win32Native.GetSystemMetrics(heightMetric));
        return Win32Native.LoadImage(
            IntPtr.Zero,
            AppIconPath,
            Win32Native.IMAGE_ICON,
            width,
            height,
            Win32Native.LR_LOADFROMFILE | Win32Native.LR_DEFAULTSIZE);
    }

    private static void ReleaseAppIcons()
    {
        nint largeIcon;
        nint smallIcon;

        lock (AppIconSync)
        {
            largeIcon = _largeAppIcon;
            smallIcon = _smallAppIcon;
            _largeAppIcon = 0;
            _smallAppIcon = 0;
            _appIconsLoaded = false;
        }

        if (largeIcon != 0)
        {
            Win32Native.DestroyIcon(largeIcon);
        }

        if (smallIcon != 0 && smallIcon != largeIcon)
        {
            Win32Native.DestroyIcon(smallIcon);
        }
    }

    private void RequestClose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32Native.PostMessage(_hwnd, Win32Native.WM_CLOSE, 0, IntPtr.Zero);
        }
    }

    private NativeWindowHost CreateNativeWindow(string title, int width, int height, string rootId, Action renderContent, Func<bool>? canClose = null)
    {
        return new NativeWindowHost(
            _hInstance,
            _hwnd,
            title,
            width,
            height,
            width,
            height,
            () => RenderNativeWindowRoot(rootId, renderContent),
            canClose);
    }

    private void CleanupClosedNativeWindows()
    {
        CleanupClosedNativeWindow(ref _gameDetailsWindow, () =>
        {
            _gameDetailsDialog = null;
            if (_confirmationHost == ConfirmationHost.GameDetailsWindow)
            {
                CloseConfirmationDialog();
            }
        });
        CleanupClosedNativeWindow(ref _updateWindow, () => _updateDialog = null);
        CleanupClosedNativeWindow(ref _componentDllWindow, () => _componentDllDialog = null);
        CleanupClosedNativeWindow(ref _configWindow, () => _configDialog = null);
        CleanupClosedNativeWindow(ref _installationWindow, () => FinalizeInstallationDialog(showSuccessMessage: false));
    }

    private void DisposeNativeWindows()
    {
        DisposeNativeWindow(ref _gameDetailsWindow);
        DisposeNativeWindow(ref _updateWindow);
        DisposeNativeWindow(ref _componentDllWindow);
        DisposeNativeWindow(ref _configWindow);
        DisposeNativeWindow(ref _installationWindow);
    }

    private static bool RenderNativeWindow(NativeWindowHost? window, float delta)
    {
        if (window == null || window.IsClosed || window.IsInSizeMove)
        {
            return false;
        }

        return window.RenderFrame(delta);
    }

    private static void CleanupClosedNativeWindow(ref NativeWindowHost? window, Action onClosed)
    {
        if (window == null || !window.IsClosed)
        {
            return;
        }

        onClosed();
        PreserveImGuiContext(window.Dispose);
        window = null;
    }

    private static void DisposeNativeWindow(ref NativeWindowHost? window)
    {
        if (window == null)
        {
            return;
        }

        PreserveImGuiContext(window.Dispose);
        window = null;
    }

    private static void PreserveImGuiContext(Action action)
    {
        var previousContext = ImGui.GetCurrentContext();
        try
        {
            action();
        }
        finally
        {
            ImGui.SetCurrentContext(previousContext);
        }
    }

    private static T PreserveImGuiContext<T>(Func<T> action)
    {
        var previousContext = ImGui.GetCurrentContext();
        try
        {
            return action();
        }
        finally
        {
            ImGui.SetCurrentContext(previousContext);
        }
    }

    private static void RenderNativeWindowRoot(string rootId, Action renderContent)
    {
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f, 20f));

        ImGui.Begin(rootId, windowFlags);
        renderContent();
        ImGui.End();

        ImGui.PopStyleVar(3);
    }

    private bool CanCloseInstallationWindow()
    {
        if (_installationDialog?.ViewModel.IsInstalling == true)
        {
            SetNotification("Wait for the installation to finish before closing the installer window.", NotificationKind.Info);
            return false;
        }

        return true;
    }

    private void RenderUi()
    {
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.MenuBar;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f, 20f));

        ImGui.Begin("OptinstallerRoot", windowFlags);

        RenderMenuBar();
        RenderSidebar();
        ImGui.SameLine();
        RenderContent();
        RenderConfirmationDialog(ConfirmationHost.MainWindow);

        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("Actions"))
        {
            if (ImGui.MenuItem("Refresh Versions"))
            {
                StartUiTask(() => _mainViewModel.Versions.LoadVersions(), "Could not refresh versions");
            }

            if (ImGui.MenuItem("Rescan Library"))
            {
                StartUiTask(() => _mainViewModel.Dashboard.InitializeAsync(), "Could not refresh library");
            }

            if (ImGui.MenuItem("Quit"))
            {
                RequestClose();
            }

            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void RenderSidebar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("Sidebar", new Vector2(248f, 0f), PaddedPanelChildFlags, PanelWindowFlags);

        ImGui.TextColored(InfoColor, "OPTIMANAGER");
        TextMuted("OptiScaler manager");
        ImGui.Spacing();
        ImGui.SeparatorText("Pages");

        DrawPageButton(AppPage.Dashboard, "Dashboard", "Games and installs");
        DrawPageButton(AppPage.Versions, "Versions", "Downloads and releases");
        DrawPageButton(AppPage.Settings, "Settings", "App defaults and info");

        ImGui.Spacing();
        ImGui.SeparatorText("Status");

        RenderSidebarSignal("Tracked", _mainViewModel.Dashboard.Games.Count.ToString(), InfoColor);
        RenderSidebarSignal("Installed", CountInstalledGames().ToString(), SuccessColor);
        RenderSidebarSignal("Downloads", _mainViewModel.Dashboard.DownloadedVersions.Count.ToString(), WarningColor);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawPageButton(AppPage page, string title, string subtitle)
    {
        var selected = _currentPage == page;
        if (DrawSelectableRow($"Nav::{page}", title, subtitle, selected, InfoColor, string.Empty, centerText: false, persistentIndicator: true))
        {
            _currentPage = page;
        }

        ImGui.Dummy(new Vector2(0f, 4f));
    }

    private static void RenderSidebarSignal(string label, string value, Vector4 accent)
    {
        ImGui.TextColored(accent, value);
        ImGui.SameLine();
        ImGui.TextDisabled(label);
    }

    private void RenderContent()
    {
        ImGui.BeginChild("Content", new Vector2(0f, 0f), ImGuiChildFlags.None);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 18f));
        ImGui.BeginChild("ContentBody", new Vector2(0f, 0f), ImGuiChildFlags.AlwaysUseWindowPadding);

        RenderNotification();

        switch (_currentPage)
        {
            case AppPage.Dashboard:
                RenderDashboard();
                break;
            case AppPage.Versions:
                RenderVersionManager();
                break;
            case AppPage.Settings:
                RenderSettings();
                break;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.EndChild();
    }

    private void RenderNotification()
    {
        if (string.IsNullOrWhiteSpace(_notificationMessage))
        {
            return;
        }

        if (_notificationExpiresAt != default && DateTime.UtcNow >= _notificationExpiresAt)
        {
            _notificationMessage = null;
            return;
        }

        var (accent, background, label) = _notificationKind switch
        {
            NotificationKind.Success => (SuccessColor, new Vector4(0.17f, 0.22f, 0.15f, 1f), "Success"),
            NotificationKind.Warning => (WarningColor, new Vector4(0.24f, 0.19f, 0.11f, 1f), "Warning"),
            NotificationKind.Error => (ErrorColor, new Vector4(0.24f, 0.14f, 0.13f, 1f), "Error"),
            _ => (InfoColor, new Vector4(0.18f, 0.21f, 0.16f, 1f), "Info"),
        };

        var style = ImGui.GetStyle();
        var dismissWidth = 90f;
        var notificationText = $"{label}: {_notificationMessage}";
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var innerWidth = MathF.Max(0f, availableWidth - (style.WindowPadding.X * 2f));
        var stackedLayout = innerWidth < 560f;
        var messageWidth = stackedLayout
            ? innerWidth
            : MathF.Max(120f, innerWidth - dismissWidth - style.ItemSpacing.X);
        var messageSize = ImGui.CalcTextSize(notificationText, false, messageWidth);
        var contentHeight = stackedLayout
            ? messageSize.Y + style.ItemSpacing.Y + ImGui.GetFrameHeight()
            : MathF.Max(messageSize.Y, ImGui.GetFrameHeight());
        var bannerHeight = MathF.Max(58f, contentHeight + (style.WindowPadding.Y * 2f));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, background);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.55f));

        ImGui.BeginChild("NotificationBanner", new Vector2(0f, bannerHeight), PaddedPanelChildFlags, PanelWindowFlags);
        var contentOrigin = ImGui.GetCursorPos();
        var contentSize = ImGui.GetContentRegionAvail();
        var verticalOffset = MathF.Max(0f, (contentSize.Y - contentHeight) * 0.5f);
        var messageY = contentOrigin.Y + verticalOffset;
        if (!stackedLayout)
        {
            messageY += MathF.Max(0f, (contentHeight - messageSize.Y) * 0.5f);
        }

        ImGui.SetCursorPos(new Vector2(contentOrigin.X, messageY));
        ImGui.PushTextWrapPos(contentOrigin.X + messageWidth);
        ImGui.TextWrapped(notificationText);
        ImGui.PopTextWrapPos();

        if (stackedLayout)
        {
            var buttonX = contentOrigin.X + MathF.Max(0f, (contentSize.X - dismissWidth) * 0.5f);
            var buttonY = contentOrigin.Y + verticalOffset + messageSize.Y + style.ItemSpacing.Y;
            ImGui.SetCursorPos(new Vector2(buttonX, buttonY));
        }
        else
        {
            var buttonX = contentOrigin.X + MathF.Max(0f, contentSize.X - dismissWidth);
            var buttonY = contentOrigin.Y + verticalOffset + MathF.Max(0f, (contentHeight - ImGui.GetFrameHeight()) * 0.5f);
            ImGui.SetCursorPos(new Vector2(buttonX, buttonY));
        }

        if (ImGui.Button("Dismiss", new Vector2(dismissWidth, 0f)))
        {
            _notificationMessage = null;
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

    private (int Width, int Height) GetGameDetailsClientSize()
    {
        var (workWidth, workHeight) = GetCurrentMonitorWorkAreaSize();
        var maxWidth = Math.Max(600, workWidth - 120);
        var maxHeight = Math.Max(520, workHeight - 120);
        var minWidth = Math.Min(MinGameDetailsClientWidth, maxWidth);
        var minHeight = Math.Min(MinGameDetailsClientHeight, maxHeight);

        var width = Math.Clamp((int)MathF.Round(workWidth * 0.44f), minWidth, maxWidth);
        var height = Math.Clamp((int)MathF.Round(workHeight * 0.72f), minHeight, maxHeight);
        return (width, height);
    }

    private (int Width, int Height) GetCurrentMonitorWorkAreaSize()
    {
        var fallbackWidth = Math.Max(_windowWidth, MinGameDetailsClientWidth);
        var fallbackHeight = Math.Max(_windowHeight, MinGameDetailsClientHeight);
        var monitor = Win32Native.MonitorFromWindow(_hwnd, Win32Native.MONITOR_DEFAULTTONEAREST);
        if (monitor == 0)
        {
            return (fallbackWidth, fallbackHeight);
        }

        var monitorInfo = new Win32Native.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<Win32Native.MONITORINFO>(),
        };

        if (!Win32Native.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return (fallbackWidth, fallbackHeight);
        }

        var workWidth = Math.Max(1, monitorInfo.rcWork.Right - monitorInfo.rcWork.Left);
        var workHeight = Math.Max(1, monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top);
        return (workWidth, workHeight);
    }

    private void RenderDashboard()
    {
        var dashboard = _mainViewModel.Dashboard;
        var allGames = new List<GameInstance>(dashboard.Games);
        var filteredGames = GetFilteredGames(allGames, _dashboardSearchQuery);
        var selectedGame = ResolveSelectedGame(dashboard, filteredGames);
        var installedCount = CountInstalledGames();
        var pendingCount = allGames.Count - installedCount;

        RenderPageHeader("Dashboard", "Manage installs directly or open per-game details.");
        RenderDashboardToolbar(dashboard, allGames.Count, installedCount, pendingCount);

        ImGui.Spacing();
        var search = _dashboardSearchQuery;
        ImGui.SetNextItemWidth(320f);
        if (ImGui.InputText("Search##Dashboard", ref search, 256))
        {
            _dashboardSearchQuery = search;
            filteredGames = GetFilteredGames(allGames, _dashboardSearchQuery);
            selectedGame = ResolveSelectedGame(dashboard, filteredGames);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear##DashboardSearch"))
        {
            _dashboardSearchQuery = string.Empty;
            filteredGames = GetFilteredGames(allGames, _dashboardSearchQuery);
            selectedGame = ResolveSelectedGame(dashboard, filteredGames);
        }

        ImGui.SameLine();
        TextMuted($"Showing {filteredGames.Count} of {allGames.Count} games");

        ImGui.Spacing();
        if (allGames.Count == 0)
        {
            RenderCallout(
                "No games added yet",
                "Add a game directory to start managing OptiScaler installs. The app remembers tracked paths between launches.",
                InfoColor);
            return;
        }

        if (filteredGames.Count == 0)
        {
            RenderCallout(
                "No matches",
                "No tracked games matched the current search. Try a different term or clear the filter.",
                WarningColor);
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("DashboardList", new Vector2(0f, 0f), PaddedPanelChildFlags, ScrollablePanelWindowFlags);
        RenderSectionHeader($"Games ({filteredGames.Count})");
        TextMuted("Use Install or Uninstall for quick actions, or open Details for the full per-game view.");
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        var canQuickInstall = dashboard.DownloadedVersions.Count > 0;
        foreach (var game in filteredGames)
        {
            var isSelected = selectedGame != null && selectedGame.GamePath.Equals(game.GamePath, StringComparison.OrdinalIgnoreCase);
            var action = game.IsInstalled
                ? DrawInstalledGameRow(game, isSelected)
                : DrawPendingGameRow(game, isSelected, canQuickInstall);

            if (action == DashboardGameRowAction.None)
            {
                continue;
            }

            SelectDashboardGame(dashboard, game);
            selectedGame = game;

            switch (action)
            {
                case DashboardGameRowAction.Select:
                    break;
                case DashboardGameRowAction.Details:
                    OpenGameDetailsDialog(game);
                    break;
                case DashboardGameRowAction.Install:
                    StartQuickInstall(game, dashboard);
                    break;
                case DashboardGameRowAction.Uninstall:
                    QueueConfirmation(
                        ConfirmationHost.MainWindow,
                        $"Uninstall from {game.Name}",
                        "This removes OptiScaler files from the selected game but keeps the game in your library." +
                        (ElevatedOperationService.RequiresElevation(game.GamePath)
                            ? BuildProtectedFolderNotice("This folder is protected. Continuing will require administrator approval before OptiManager can remove the installed files.")
                            : string.Empty),
                        "Uninstall",
                        () => dashboard.UninstallOptiScaler(game),
                        $"Uninstalled OptiScaler from {game.Name}.",
                        $"Could not uninstall OptiScaler from {game.Name}");
                    break;
                case DashboardGameRowAction.UpdateVersion:
                    OpenUpdateDialog(game);
                    break;
                case DashboardGameRowAction.OpenComponentDlls:
                    OpenComponentDllDialog(game);
                    break;
            }

        }
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void SelectDashboardGame(DashboardViewModel dashboard, GameInstance game)
    {
        _selectedGamePath = game.GamePath;
        dashboard.SelectedGame = game;
    }

    private void RenderGameDetails(DashboardViewModel dashboard, GameDetailsDialogState? details)
    {
        var game = details?.Game;
        if (game == null)
        {
            RenderCallout(
                "No game selected",
                "Select a game from the library list to review its current install state and available actions.",
                InfoColor);
            return;
        }

        ImGui.TextUnformatted(game.Name);
        ImGui.SameLine();
        ImGui.TextColored(game.IsInstalled ? SuccessColor : InfoColor, game.IsInstalled ? "Installed" : "Not installed");

        ImGui.Spacing();
        RenderInlinePill(game.IsInstalled ? "Installed" : "Not installed", game.IsInstalled ? SuccessColor : InfoColor);
        if (game.IsInstalled)
        {
            ImGui.SameLine();
            if (RenderClickableInlinePill($"GameDetails::Version::{game.GamePath}", GetOptiScalerQuickPillText(game), SuccessColor))
            {
                OpenUpdateDialog(game);
            }

            ImGui.SameLine();
            if (RenderClickableInlinePill($"GameDetails::Components::{game.GamePath}", GetUpscalersFgQuickPillText(), InfoColor))
            {
                OpenComponentDllDialog(game);
            }
        }

        if (!string.IsNullOrWhiteSpace(game.AntiCheatProvider))
        {
            ImGui.SameLine();
            RenderInlinePill(game.AntiCheatProvider, WarningColor);
        }

        ImGui.Spacing();
        RenderSectionHeader("Details");
        var changePathWidth = GetButtonWidth("Change Game Path", 170f);
        var openFolderWidth = GetButtonWidth("Open Folder", 140f);

        if (ImGui.BeginTable("GamePathActions", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, changePathWidth + openFolderWidth + ImGui.GetStyle().ItemSpacing.X);

            ImGui.TableNextColumn();
            ImGui.TextDisabled("Game Path");
            ImGui.TextWrapped(game.GamePath);

            ImGui.TableNextColumn();
            var actionsWidth = changePathWidth + openFolderWidth + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - actionsWidth));

            if (ImGui.Button("Change Game Path", new Vector2(changePathWidth, 0f)))
            {
                PromptChangeGamePath(dashboard, game);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Folder", new Vector2(openFolderWidth, 0f)))
            {
                dashboard.OpenGameFolder(game);
            }

            ImGui.EndTable();
        }

        TextMuted("Pick the exact executable to track for this game. This bypasses the add-game auto-detection logic.");
        ImGui.Spacing();

        if (!string.IsNullOrWhiteSpace(game.ExecutableName))
        {
            RenderKeyValue("Executable", game.ExecutableName);
        }

        if (ImGui.BeginTable("GameDetailSummary", 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn();
            RenderCompactKeyValue("Current Version", game.CurrentVersion);

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Install State", game.IsInstalled ? "Installed" : "Not installed");

            ImGui.TableNextColumn();
            RenderCompactKeyValue("DLL", string.IsNullOrWhiteSpace(game.InstalledFilename) ? "-" : game.InstalledFilename);

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Anti-Cheat", string.IsNullOrWhiteSpace(game.AntiCheatProvider) ? "None detected" : game.AntiCheatProvider);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        RenderSectionHeader("Configure");
        if (details == null)
        {
            RenderCallout(
                "Configuration unavailable",
                "Could not load this game's configuration state.",
                WarningColor);
        }
        else if (!game.IsInstalled)
        {
            TextMuted("Install OptiScaler to edit this game's configuration.");
        }
        else
        {
            RenderGameConfigureSection(details);
        }

        ImGui.Spacing();
        RenderSectionHeader("Actions");
        var installWidth = GetButtonWidth("Install OptiScaler", 160f);
        var uninstallWidth = GetButtonWidth("Uninstall", 120f);
        var removeWidth = GetButtonWidth("Remove From Library", 180f);

        if (!game.IsInstalled)
        {
            ImGui.BeginDisabled(dashboard.DownloadedVersions.Count == 0);
            var openInstallDialog = ImGui.Button("Install OptiScaler", new Vector2(installWidth, 0f));
            ImGui.EndDisabled();

            if (openInstallDialog)
            {
                OpenInstallationDialog(game);
            }
        }
        else
        {
            if (ImGui.Button("Uninstall", new Vector2(uninstallWidth, 0f)))
            {
                QueueConfirmation(
                    ConfirmationHost.GameDetailsWindow,
                    $"Uninstall from {game.Name}",
                    "This removes OptiScaler files from the selected game but keeps the game in your library." +
                    (ElevatedOperationService.RequiresElevation(game.GamePath)
                        ? BuildProtectedFolderNotice("This folder is protected. Continuing will require administrator approval before OptiManager can remove the installed files.")
                        : string.Empty),
                    "Uninstall",
                    () => dashboard.UninstallOptiScaler(game),
                    $"Uninstalled OptiScaler from {game.Name}.",
                    $"Could not uninstall OptiScaler from {game.Name}");
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Remove From Library", new Vector2(removeWidth, 0f)))
        {
            QueueConfirmation(
                ConfirmationHost.GameDetailsWindow,
                $"Remove {game.Name}",
                game.IsInstalled
                    ? "This removes the game from the library and uninstalls OptiScaler from it." +
                      (ElevatedOperationService.RequiresElevation(game.GamePath)
                          ? BuildProtectedFolderNotice("This folder is protected. Continuing will require administrator approval before OptiManager can remove the installed files.")
                          : string.Empty)
                    : "This removes the game from the library.",
                "Remove",
                async () =>
                {
                    await dashboard.RemoveGame(game);
                    CloseGameDetailsDialog();
                },
                $"Removed {game.Name} from the library.",
                $"Could not remove {game.Name}");
        }
    }

    private void RenderGameConfigureSection(GameDetailsDialogState details)
    {
        var config = EnsureGameDetailsConfig(details);
        if (config == null)
        {
            RenderCallout(
                "Configuration unavailable",
                "OptiScaler.ini could not be loaded for this game.",
                WarningColor);
            return;
        }

        EnsureGameConfigureDefaults(config);

        TextMuted("Expand a group, adjust values, then save to write OptiScaler.ini for this game.");
        ImGui.Spacing();

        if (!ImGui.BeginTable("GameConfigureLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableSetupColumn("Config", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Spacer", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableNextColumn();

        if (ImGui.CollapsingHeader("Frame Generation"))
        {
            var previousInput = config.GetSetting("FrameGen", "FGInput");
            var previousOutput = config.GetSetting("FrameGen", "FGOutput");
            var inputChanged = RenderConfigChoiceCombo("FG Input", config, "FrameGen", "FGInput", FrameGenerationInputChoices);
            var outputChanged = RenderConfigChoiceCombo("FG Output", config, "FrameGen", "FGOutput", FrameGenerationOutputChoices);
            ApplyNukemsFrameGenerationCoupling(config, previousInput, previousOutput, inputChanged, outputChanged);
            TextMuted("Nukem's forces both the input and output settings together.");
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Menu"))
        {
            RenderShortcutKeySetting(details, config);
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Sharpness"))
        {
            RenderConfigChoiceCombo("Override Sharpness", config, "Sharpness", "OverrideSharpness", BooleanChoices);
            RenderConfigFloatSlider("Sharpness Amount", config, "Sharpness", "Sharpness", 0f, 1f, "%.2f", "0.###");
            RenderConfigChoiceCombo("Contrast Enabled", config, "CAS", "ContrastEnabled", BooleanChoices);
            RenderConfigFloatSlider("Contrast", config, "CAS", "Contrast", 0f, 2f, "%.2f", "0.###");
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Logging"))
        {
            RenderConfigChoiceCombo("Log Level", config, "Log", "LogLevel", LogLevelChoices);
            RenderConfigChoiceCombo("Log To File", config, "Log", "LogToFile", BooleanChoices);
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Spoofing & Plugins"))
        {
            RenderConfigChoiceCombo("DXGI Spoofing", config, "Spoofing", "Dxgi", BooleanChoices);
            RenderConfigChoiceCombo("Load ASI Plugins", config, "Plugins", "LoadAsiPlugins", BooleanChoices);
            ImGui.Spacing();
        }

        var saveWidth = GetButtonWidth("Save Config", 120f);
        var reloadWidth = GetButtonWidth("Reload Config", 130f);
        var openFileWidth = GetButtonWidth("Open Config File", 145f);

        if (ImGui.Button("Save Config", new Vector2(saveWidth, 0f)))
        {
            try
            {
                config.SaveChanges();
                SetNotification($"Saved configuration for {details.Game.Name}.", NotificationKind.Success);
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message, NotificationKind.Error);
            }
        }

        ContinueOnSameLineIfFits(reloadWidth);
        if (ImGui.Button("Reload Config", new Vector2(reloadWidth, 0f)))
        {
            config.Reload();
            SyncShortcutKeyInput(details, config, force: true);
            SetNotification($"Reloaded configuration for {details.Game.Name}.", NotificationKind.Info);
        }

        ContinueOnSameLineIfFits(openFileWidth);
        if (ImGui.Button("Open Config File", new Vector2(openFileWidth, 0f)))
        {
            try
            {
                config.OpenFile();
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message, NotificationKind.Error);
            }
        }

        ImGui.EndTable();
    }

    private void EnsureGameConfigureDefaults(GameConfigViewModel config)
    {
        EnsureConfigChoiceValue(config, "FrameGen", "FGInput", FrameGenerationInputChoices, "nofg");
        EnsureConfigChoiceValue(config, "FrameGen", "FGOutput", FrameGenerationOutputChoices, "nofg");
        ApplyNukemsFrameGenerationCoupling(config, config.GetSetting("FrameGen", "FGInput"), config.GetSetting("FrameGen", "FGOutput"), inputChanged: false, outputChanged: false);
        EnsureConfigShortcutValue(config, "Menu", "ShortcutKey", "0x2D");
        EnsureConfigChoiceValue(config, "Sharpness", "OverrideSharpness", BooleanChoices, "false");
        EnsureConfigFloatValue(config, "Sharpness", "Sharpness", 0.3f, "0.###");
        EnsureConfigChoiceValue(config, "CAS", "ContrastEnabled", BooleanChoices, "false");
        EnsureConfigFloatValue(config, "CAS", "Contrast", 0f, "0.###");
        EnsureConfigChoiceValue(config, "Log", "LogLevel", LogLevelChoices, "1");
        EnsureConfigChoiceValue(config, "Log", "LogToFile", BooleanChoices, "false");
        EnsureConfigChoiceValue(config, "Spoofing", "Dxgi", BooleanChoices, GetDefaultDxgiConfigValue());
        EnsureConfigChoiceValue(config, "Plugins", "LoadAsiPlugins", BooleanChoices, "false");
    }

    private void RenderShortcutKeySetting(GameDetailsDialogState details, GameConfigViewModel config)
    {
        SyncShortcutKeyInput(details, config);

        ImGui.TextDisabled("Overlay Shortcut");

        var buttonLabel = details.IsCapturingShortcutKey
            ? "Press a key... (Esc = None)"
            : details.ShortcutKeyInput;
        if (string.IsNullOrWhiteSpace(buttonLabel))
        {
            buttonLabel = "Detect Shortcut";
        }

        if (details.IsCapturingShortcutKey)
        {
            if (TryDetectShortcutCapture(out var capturedVirtualKey))
            {
                var configValue = FormatShortcutKeyConfigValue(capturedVirtualKey);
                config.SetSetting("Menu", "ShortcutKey", configValue);
                details.ShortcutKeyConfigValue = configValue;
                details.ShortcutKeyInput = FormatShortcutKeyDisplay(capturedVirtualKey);
                details.ShortcutKeyErrorMessage = null;
                details.IsCapturingShortcutKey = false;
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Button, details.IsCapturingShortcutKey ? PanelRaisedBackgroundColor : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
        ImGui.SetNextItemWidth(GetConfigControlWidth());
        if (ImGui.Button(buttonLabel, new Vector2(GetConfigControlWidth(), 0f)))
        {
            details.IsCapturingShortcutKey = !details.IsCapturingShortcutKey;
            details.ShortcutKeyErrorMessage = null;
        }
        ImGui.PopStyleColor();

        TextMuted("Click the button, then press the key you want. Press Esc to disable the shortcut.");
        if (details.IsCapturingShortcutKey)
        {
            ImGui.TextColored(InfoColor, "Waiting for key press...");
        }
        TextMuted($"Stored as {config.GetSetting("Menu", "ShortcutKey")}.");
    }

    private static void ApplyNukemsFrameGenerationCoupling(GameConfigViewModel config, string previousInput, string previousOutput, bool inputChanged, bool outputChanged)
    {
        var currentInput = config.GetSetting("FrameGen", "FGInput");
        var currentOutput = config.GetSetting("FrameGen", "FGOutput");

        if (inputChanged &&
            previousInput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            currentOutput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            !currentInput.Equals("nukems", StringComparison.OrdinalIgnoreCase))
        {
            config.SetSetting("FrameGen", "FGOutput", "nofg");
            return;
        }

        if (outputChanged &&
            previousOutput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            currentInput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            !currentOutput.Equals("nukems", StringComparison.OrdinalIgnoreCase))
        {
            config.SetSetting("FrameGen", "FGInput", "nofg");
            return;
        }

        if (currentInput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            !currentOutput.Equals("nukems", StringComparison.OrdinalIgnoreCase))
        {
            config.SetSetting("FrameGen", "FGOutput", "nukems");
            return;
        }

        if (currentOutput.Equals("nukems", StringComparison.OrdinalIgnoreCase) &&
            !currentInput.Equals("nukems", StringComparison.OrdinalIgnoreCase))
        {
            config.SetSetting("FrameGen", "FGInput", "nukems");
        }
    }

    private static void SyncShortcutKeyInput(GameDetailsDialogState details, GameConfigViewModel config, bool force = false)
    {
        var currentValue = config.GetSetting("Menu", "ShortcutKey");
        if (!force && string.Equals(details.ShortcutKeyConfigValue, currentValue, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        details.ShortcutKeyConfigValue = currentValue;
        details.ShortcutKeyInput = FormatShortcutKeyDisplay(currentValue);
        details.ShortcutKeyErrorMessage = null;
    }

    private GameConfigViewModel? EnsureGameDetailsConfig(GameDetailsDialogState details)
    {
        if (!details.Game.IsInstalled)
        {
            details.ConfigViewModel = null;
            return null;
        }

        if (details.ConfigViewModel != null &&
            details.ConfigViewModel.GamePath.Equals(details.Game.GamePath, StringComparison.OrdinalIgnoreCase))
        {
            return details.ConfigViewModel;
        }

        try
        {
            details.ConfigViewModel = _mainViewModel.Dashboard.CreateGameConfig(details.Game);
        }
        catch (Exception ex)
        {
            details.ConfigViewModel = null;
            SetNotification(ex.Message, NotificationKind.Error);
        }

        return details.ConfigViewModel;
    }

    private void RenderGameDetailsWindow()
    {
        if (_gameDetailsDialog == null)
        {
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("GameDetailsContent", new Vector2(0f, -54f), PaddedPanelChildFlags);
        RenderGameDetails(_mainViewModel.Dashboard, _gameDetailsDialog);
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.Separator();
        if (ImGui.Button("Close", new Vector2(110f, 0f)))
        {
            CloseGameDetailsDialog();
        }

        RenderConfirmationDialog(ConfirmationHost.GameDetailsWindow);
    }

    private void RenderVersionManager()
    {
        var versions = _mainViewModel.Versions;
        var selectedVersion = ResolveSelectedVersion(versions);

        RenderPageHeader("Versions", "Download builds, inspect details, and manage local files.");
        RenderVersionsToolbar(versions, selectedVersion);

        ImGui.Spacing();
        var query = versions.SearchQuery;
        ImGui.SetNextItemWidth(320f);
        if (ImGui.InputText("Search##Versions", ref query, 256))
        {
            versions.SearchQuery = query;
            selectedVersion = ResolveSelectedVersion(versions);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear##VersionSearch"))
        {
            versions.ClearSearch();
            selectedVersion = ResolveSelectedVersion(versions);
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh", new Vector2(110f, 0f)))
        {
            StartUiTask(() => versions.LoadVersions(), "Could not refresh versions");
        }

        if (versions.IsLoading)
        {
            ImGui.SameLine();
            TextMuted("Loading release data...");
        }

        if (!string.IsNullOrWhiteSpace(versions.ErrorMessage))
        {
            ImGui.Spacing();
            ImGui.TextColored(ErrorColor, versions.ErrorMessage);
            if (versions.ErrorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                TextMuted("GitHub is rate limiting release requests right now. Local downloaded versions should still appear here if any are available.");
            }
        }

        ImGui.Spacing();
        if (versions.DownloadedVersions.Count == 0 && versions.AvailableVersions.Count == 0 && !versions.IsLoading)
        {
            RenderCallout(
                "No versions available",
                string.IsNullOrWhiteSpace(versions.ErrorMessage)
                    ? "The app could not find any remote release entries or local OptiScaler builds. Try refreshing or check your network connection."
                    : $"{versions.ErrorMessage} Downloaded versions will still show here when available.",
                WarningColor);
            return;
        }

        var listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.38f, 300f, 430f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("VersionList", new Vector2(listWidth, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        RenderVersionListSection("Downloaded", versions.DownloadedVersions, selectedVersion);
        ImGui.Spacing();
        RenderVersionListSection("Available", versions.AvailableVersions, selectedVersion);
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.SameLine();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("VersionDetail", new Vector2(0f, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        RenderVersionDetails(selectedVersion);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void RenderVersionListSection(string title, IReadOnlyList<OptiScalerVersion> versions, OptiScalerVersion? selectedVersion)
    {
        RenderSectionHeader($"{title} ({versions.Count})");

        if (versions.Count == 0)
        {
            TextMuted(title == "Downloaded"
                ? "No versions are stored locally yet."
                : "No remote-only versions matched the current filter.");
            return;
        }

        foreach (var version in versions)
        {
            var isSelected = selectedVersion != null && selectedVersion.TagName.Equals(version.TagName, StringComparison.OrdinalIgnoreCase);
            var accent = version.IsDownloaded ? SuccessColor : InfoColor;
            var detailText = version.IsDownloading
                ? version.DownloadStatus
                : string.IsNullOrWhiteSpace(version.FileSizeDisplay)
                    ? version.RelativeTime
                    : $"{version.RelativeTime}  {version.FileSizeDisplay}";

            if (DrawSelectableRow(
                $"Version::{title}::{version.TagName}",
                version.TagName,
                detailText,
                isSelected,
                accent,
                version.IsBleedingEdge ? "BE" : "Official",
                centerText: true))
            {
                _selectedVersionTag = version.TagName;
            }

        }
    }

    private void RenderVersionDetails(OptiScalerVersion? version)
    {
        if (version == null)
        {
            RenderCallout(
                "No version selected",
                "Select a downloaded or available release from the left column to inspect details and actions.",
                InfoColor);
            return;
        }

        ImGui.TextUnformatted(version.TagName);
        ImGui.SameLine();
        ImGui.TextColored(version.IsDownloaded ? SuccessColor : InfoColor, version.IsDownloaded ? "Downloaded" : "Online only");
        TextMuted(version.IsBleedingEdge ? "Bleeding Edge" : "Official");
        ImGui.Spacing();
        RenderSectionHeader("Release Details");

        if (ImGui.BeginTable("ReleaseDetailSummary", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn();
            RenderCompactKeyValue("Source", version.IsBleedingEdge ? "Bleeding Edge" : "Official");

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Published", version.PublishedAt == default ? "-" : version.PublishedAt.ToLocalTime().ToString("g"));

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Relative Time", version.RelativeTime);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        RenderKeyValue("File Size", string.IsNullOrWhiteSpace(version.FileSizeDisplay) ? "-" : version.FileSizeDisplay);
        RenderKeyValue("Local Path", version.IsDownloaded && !string.IsNullOrWhiteSpace(version.LocalPath) ? version.LocalPath : "-");

        ImGui.Spacing();
        RenderSectionHeader("Actions");
        if (version.IsDownloading)
        {
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(version.DownloadStatus) ? "Downloading..." : version.DownloadStatus);
            ImGui.ProgressBar((float)(version.DownloadProgress / 100d), new Vector2(-1f, 0f));
        }
        else if (version.IsDownloaded)
        {
            var openFolderWidth = GetButtonWidth("Open Folder", 140f);
            var deleteWidth = GetButtonWidth("Delete", 120f);
            var defaultInstallWidth = GetButtonWidth("Use as Default Install", 180f);

            if (ImGui.Button("Open Folder", new Vector2(openFolderWidth, 0f)))
            {
                _mainViewModel.Versions.OpenFolder(version);
            }

            ContinueOnSameLineIfFits(deleteWidth);
            if (ImGui.Button("Delete", new Vector2(deleteWidth, 0f)))
            {
                QueueConfirmation(
                    ConfirmationHost.MainWindow,
                    $"Delete {version.TagName}",
                    "This removes the downloaded version from local storage.",
                    "Delete",
                    () => _mainViewModel.Versions.DeleteVersion(version),
                    $"Deleted {version.TagName}.");
            }

            ContinueOnSameLineIfFits(defaultInstallWidth);
            if (ImGui.Button("Use as Default Install", new Vector2(defaultInstallWidth, 0f)))
            {
                _mainViewModel.Dashboard.SelectedVersion = version;
                SetNotification($"{version.TagName} is now the preferred install version.", NotificationKind.Success);
            }
        }
        else if (ImGui.Button("Download", new Vector2(140f, 0f)))
        {
            StartUiTask(
                () => _mainViewModel.Versions.DownloadVersion(version),
                $"Could not download {version.TagName}",
                $"Downloaded {version.TagName}.");
        }

        ImGui.Spacing();
        RenderSectionHeader("Release Notes");
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        ImGui.BeginChild("ReleaseNotesBody", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        RenderMarkdownText(string.IsNullOrWhiteSpace(version.Description)
            ? "No release notes were available for this entry."
            : version.Description);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void RenderSettings()
    {
        var settings = _mainViewModel.Settings;
        var preferredVersion = _mainViewModel.Dashboard.SelectedVersion;

        RenderPageHeader("Settings", "Simple defaults for this session and basic app info.");
        TextMuted("These settings only affect this app session.");
        ImGui.Spacing();

        if (!ImGui.BeginTable("SettingsLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.TableNextColumn();
        ImGui.BeginChild("SettingsDefaults", new Vector2(0f, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        ImGui.SeparatorText("Defaults");

        var overlayEnabled = settings.EnableOverlay;
        if (ImGui.Checkbox("Enable overlay by default", ref overlayEnabled))
        {
            settings.EnableOverlay = overlayEnabled;
        }

        ImGui.Spacing();
        if (_mainViewModel.Dashboard.DownloadedVersions.Count > 0)
        {
            DrawVersionCombo("Preferred install version", _mainViewModel.Dashboard.DownloadedVersions, ref preferredVersion);
            _mainViewModel.Dashboard.SelectedVersion = preferredVersion;
        }
        else
        {
            TextMuted("Download at least one version to set a preferred install target.");
        }

        ImGui.Spacing();
        TextMuted("These values currently affect the running session only.");
        ImGui.EndChild();

        ImGui.TableNextColumn();
        ImGui.BeginChild("SettingsRuntime", new Vector2(0f, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        ImGui.SeparatorText("Sources & Runtime");
        ImGui.TextUnformatted("OptiScaler release URL");

        var downloadUrl = settings.OptiScalerDownloadUrl;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##DownloadUrl", ref downloadUrl, 512))
        {
            settings.OptiScalerDownloadUrl = downloadUrl;
        }

        ImGui.Spacing();
        if (DrawCenteredButton("Settings.OpenReleasesPage", "Open Releases Page", new Vector2(160f, 0f)))
        {
            TryOpenExternalUrl(settings.OptiScalerDownloadUrl);
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Runtime");

        if (ImGui.BeginTable("RuntimeSummaryTop", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn();
            RenderCompactKeyValue("UI Host", "Dear ImGui + Win32 + Direct3D 11");

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Framework", Environment.Version.ToString());

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Window Size", $"{_windowWidth} x {_windowHeight}");

            ImGui.EndTable();
        }

        ImGui.Spacing();

        if (ImGui.BeginTable("RuntimeSummaryBottom", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn();
            RenderCompactKeyValue("Tracked Games", _mainViewModel.Dashboard.Games.Count.ToString());

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Downloaded Versions", _mainViewModel.Dashboard.DownloadedVersions.Count.ToString());

            ImGui.EndTable();
        }
        ImGui.EndChild();

        ImGui.EndTable();
    }

    private void RenderPageHeader(string title, string subtitle)
    {
        ImGui.SeparatorText(title);
        TextMuted(subtitle);
        ImGui.Spacing();
    }

    private static void RenderSectionHeader(string title)
    {
        ImGui.TextDisabled(title);
        ImGui.Separator();
    }

    private static void RenderMarkdownText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                if (!string.IsNullOrEmpty(trimmed))
                {
                    ImGui.TextUnformatted(trimmed);
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                ImGui.Spacing();
                continue;
            }

            if (trimmed.StartsWith("---", StringComparison.Ordinal) || trimmed.StartsWith("***", StringComparison.Ordinal))
            {
                ImGui.Separator();
                continue;
            }

            var headingLevel = 0;
            while (headingLevel < trimmed.Length && trimmed[headingLevel] == '#')
            {
                headingLevel++;
            }

            if (headingLevel > 0 && headingLevel < trimmed.Length && trimmed[headingLevel] == ' ')
            {
                var headingText = NormalizeMarkdownInline(trimmed[(headingLevel + 1)..]);
                ImGui.TextColored(headingLevel <= 2 ? InfoColor : new Vector4(0.90f, 0.92f, 0.88f, 1f), headingText);
                continue;
            }

            var listPrefix = string.Empty;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("+ ", StringComparison.Ordinal))
            {
                listPrefix = "- ";
                trimmed = trimmed[2..];
            }
            else
            {
                var numberedMatch = Regex.Match(trimmed, "^(\\d+)\\.\\s+");
                if (numberedMatch.Success)
                {
                    listPrefix = numberedMatch.Value;
                    trimmed = trimmed[numberedMatch.Length..];
                }
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                trimmed = trimmed.TrimStart('>', ' ');
                ImGui.PushStyleColor(ImGuiCol.Text, MutedTextColor);
                ImGui.TextWrapped(NormalizeMarkdownInline(trimmed));
                ImGui.PopStyleColor();
                continue;
            }

            var text = NormalizeMarkdownInline(trimmed);
            ImGui.TextWrapped(listPrefix + text);
        }
    }

    private static string NormalizeMarkdownInline(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text;
        normalized = Regex.Replace(normalized, @"!\[([^\]]*)\]\(([^\)]+)\)", "$1");
        normalized = Regex.Replace(normalized, @"\[([^\]]+)\]\(([^\)]+)\)", "$1 ($2)");
        normalized = normalized.Replace("`", string.Empty);
        normalized = normalized.Replace("**", string.Empty).Replace("__", string.Empty);
        normalized = normalized.Replace("*", string.Empty).Replace("_", string.Empty);
        normalized = normalized.Replace("~~", string.Empty);
        return normalized.Trim();
    }

    private static void LoadFonts(ImGuiIOPtr io)
    {
        var bundledHackFont = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Hack-Regular.ttf");
        if (File.Exists(bundledHackFont))
        {
            io.Fonts.AddFontFromFileTTF(bundledHackFont, 16f);
            return;
        }

        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var preferredFonts = new[]
        {
            "bahnschrift.ttf",
            "segoeui.ttf",
            "tahoma.ttf",
            "verdana.ttf",
        };

        foreach (var fontFile in preferredFonts)
        {
            var fontPath = Path.Combine(fontsDirectory, fontFile);
            if (File.Exists(fontPath))
            {
                io.Fonts.AddFontFromFileTTF(fontPath, 16f);
                return;
            }
        }

        io.Fonts.AddFontDefault();
    }

    private static void ConfigureImGuiIo(ImGuiIOPtr io)
    {
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.FontGlobalScale = 1.0f;
    }

    private void RenderDashboardToolbar(DashboardViewModel dashboard, int totalGames, int installedCount, int pendingCount)
    {
        if (ImGui.Button("Add Game", new Vector2(120f, 0f)))
        {
            PromptAddGame();
        }

        ImGui.SameLine();
        if (ImGui.Button("Rescan Library", new Vector2(130f, 0f)))
        {
            StartUiTask(() => dashboard.InitializeAsync(), "Could not rescan library", "Rescanned library and local versions.");
        }

        RenderToolbarStat($"Games {totalGames}");
        RenderToolbarStat($"Installed {installedCount}");
        RenderToolbarStat($"Ready {pendingCount}");
        RenderToolbarStat($"Versions {dashboard.DownloadedVersions.Count}", isLast: true);
    }

    private void RenderVersionsToolbar(VersionManagerViewModel versions, OptiScalerVersion? selectedVersion)
    {
        RenderToolbarStat($"All {versions.TotalCount}");
        RenderToolbarStat($"Downloaded {versions.DownloadedCount}");
        RenderToolbarStat($"Online {versions.AvailableVersions.Count}", selectedVersion == null);

        if (selectedVersion != null)
        {
            RenderToolbarStat($"Selected {selectedVersion.TagName}", isLast: true);
        }
    }

    private static void RenderToolbarStat(string text, bool isLast = false)
    {
        ImGui.SameLine(0f, 10f);
        ImGui.TextDisabled(text);

        if (isLast)
        {
            return;
        }

        ImGui.SameLine(0f, 10f);
        ImGui.TextDisabled("|");
    }

    private void RenderDashboardHero(DashboardViewModel dashboard, GameInstance? selectedGame, int totalGames, int installedCount, int pendingCount)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(InfoColor.X, InfoColor.Y, InfoColor.Z, 0.38f));
        ImGui.BeginChild("DashboardHero", new Vector2(0f, 154f), ImGuiChildFlags.Borders, PanelWindowFlags);

        if (ImGui.BeginTable("DashboardHeroLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextColumn();
            if (selectedGame == null)
            {
                ImGui.TextColored(InfoColor, "Games");
                ImGui.TextUnformatted("Add a game to get started");
                TextMuted("Choose the game's executable, then pick a downloaded version when you are ready to install.");
                ImGui.Spacing();
                if (ImGui.Button("Add Game", new Vector2(120f, 0f)))
                {
                    PromptAddGame();
                }
                ImGui.SameLine();
                if (ImGui.Button("Rescan Library", new Vector2(130f, 0f)))
                {
                    StartUiTask(() => dashboard.InitializeAsync(), "Could not rescan library", "Rescanned library and local versions.");
                }
            }
            else
            {
                var configureWidth = GetButtonWidth("Configure", 120f);
                var updateWidth = GetButtonWidth("Update Version", 150f);
                var installWidth = GetButtonWidth("Install OptiScaler", 160f);
                var openFolderWidth = GetButtonWidth("Open Folder", 130f);

                ImGui.TextColored(selectedGame.IsInstalled ? SuccessColor : InfoColor, "Selected Game");
                ImGui.TextUnformatted(selectedGame.Name);
                TextMuted(TrimText(selectedGame.GamePath));
                ImGui.Spacing();
                RenderInlinePill(selectedGame.IsInstalled ? "Installed" : "Pending install", selectedGame.IsInstalled ? SuccessColor : InfoColor);

                if (selectedGame.IsInstalled)
                {
                    ImGui.SameLine();
                    if (RenderClickableInlinePill($"DashboardHero::Version::{selectedGame.GamePath}", GetOptiScalerQuickPillText(selectedGame), SuccessColor))
                    {
                        OpenUpdateDialog(selectedGame);
                    }

                    ImGui.SameLine();
                    if (RenderClickableInlinePill($"DashboardHero::Components::{selectedGame.GamePath}", GetUpscalersFgQuickPillText(), InfoColor))
                    {
                        OpenComponentDllDialog(selectedGame);
                    }

                    if (!string.IsNullOrWhiteSpace(selectedGame.InstalledFilename))
                    {
                        ImGui.SameLine();
                        RenderInlinePill(selectedGame.InstalledFilename, InfoColor);
                    }
                }
                else
                {
                    ImGui.SameLine();
                    RenderInlinePill(selectedGame.CurrentVersion, WarningColor);
                }

                if (!string.IsNullOrWhiteSpace(selectedGame.AntiCheatProvider))
                {
                    ImGui.SameLine();
                    RenderInlinePill(selectedGame.AntiCheatProvider, WarningColor);
                }

                ImGui.Spacing();
                if (selectedGame.IsInstalled)
                {
                    if (ImGui.Button("Configure", new Vector2(configureWidth, 0f)))
                    {
                        OpenConfigDialog(selectedGame);
                    }
                    ContinueOnSameLineIfFits(updateWidth);
                    if (ImGui.Button("Update Version", new Vector2(updateWidth, 0f)))
                    {
                        OpenUpdateDialog(selectedGame);
                    }
                }
                else
                {
                    ImGui.BeginDisabled(dashboard.DownloadedVersions.Count == 0);
                    if (ImGui.Button("Install OptiScaler", new Vector2(installWidth, 0f)))
                    {
                        OpenInstallationDialog(selectedGame);
                    }
                    ImGui.EndDisabled();
                }

                ContinueOnSameLineIfFits(openFolderWidth);
                if (ImGui.Button("Open Folder", new Vector2(openFolderWidth, 0f)))
                {
                    dashboard.OpenGameFolder(selectedGame);
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(InfoColor, "Summary");
            RenderHeroSignal("Games", totalGames.ToString(), InfoColor);
            RenderHeroSignal("Installed", installedCount.ToString(), SuccessColor);
            RenderHeroSignal("Pending", pendingCount.ToString(), WarningColor);
            RenderHeroSignal("Saved versions", dashboard.DownloadedVersions.Count.ToString(), new Vector4(0.72f, 0.84f, 0.55f, 1f));
            if (selectedGame != null)
            {
                RenderHeroSignal("Anti-cheat", string.IsNullOrWhiteSpace(selectedGame.AntiCheatProvider) ? "None detected" : selectedGame.AntiCheatProvider, string.IsNullOrWhiteSpace(selectedGame.AntiCheatProvider) ? InfoColor : WarningColor);
            }
            TextMuted($"Default install version: {dashboard.SelectedVersion?.TagName ?? "None selected"}");

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void RenderGameHeroCard(DashboardViewModel dashboard, GameInstance game)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4((game.IsInstalled ? SuccessColor : InfoColor).X, (game.IsInstalled ? SuccessColor : InfoColor).Y, (game.IsInstalled ? SuccessColor : InfoColor).Z, 0.38f));
        ImGui.BeginChild("GameHeroCard", new Vector2(0f, 158f), ImGuiChildFlags.Borders, PanelWindowFlags);

        if (ImGui.BeginTable("GameHeroCardLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Secondary", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextColumn();
            ImGui.TextColored(game.IsInstalled ? SuccessColor : InfoColor, "Selected Game");
            ImGui.TextUnformatted(game.Name);
            TextMuted(TrimText(game.GamePath));
            ImGui.Spacing();
            RenderInlinePill(game.IsInstalled ? "Installed" : "Not installed", game.IsInstalled ? SuccessColor : InfoColor);
            ImGui.SameLine();
            RenderInlinePill(string.IsNullOrWhiteSpace(game.InstalledFilename) ? "No DLL staged" : game.InstalledFilename, WarningColor);

            ImGui.TableNextColumn();
            ImGui.TextColored(InfoColor, "Status");
            RenderHeroSignal("Version", game.CurrentVersion, game.IsInstalled ? SuccessColor : WarningColor);
            RenderHeroSignal("Anti-cheat", string.IsNullOrWhiteSpace(game.AntiCheatProvider) ? "None detected" : game.AntiCheatProvider, string.IsNullOrWhiteSpace(game.AntiCheatProvider) ? InfoColor : WarningColor);
            RenderHeroSignal("Default version", dashboard.SelectedVersion?.TagName ?? "None selected", InfoColor);
            RenderHeroSignal("Next step", game.IsInstalled ? "Configure or update" : "Install", game.IsInstalled ? SuccessColor : InfoColor);

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void RenderVersionsHero(VersionManagerViewModel versions, OptiScalerVersion? selectedVersion)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(InfoColor.X, InfoColor.Y, InfoColor.Z, 0.38f));
        ImGui.BeginChild("VersionsHero", new Vector2(0f, 148f), ImGuiChildFlags.Borders, PanelWindowFlags);

        if (ImGui.BeginTable("VersionsHeroLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextColumn();
            if (selectedVersion == null)
            {
                ImGui.TextColored(InfoColor, "Versions");
                ImGui.TextUnformatted("Pick a version to inspect or download");
                TextMuted("Saved versions are ready to install. The online list shows what else you can download.");
            }
            else
            {
                ImGui.TextColored(selectedVersion.IsDownloaded ? SuccessColor : InfoColor, "Selected Version");
                ImGui.TextUnformatted(selectedVersion.TagName);
                TextMuted(TrimText(selectedVersion.Description));
                ImGui.Spacing();
                RenderInlinePill(selectedVersion.IsBleedingEdge ? "Bleeding Edge" : "Official", selectedVersion.IsBleedingEdge ? WarningColor : InfoColor);
                ImGui.SameLine();
                RenderInlinePill(selectedVersion.IsDownloaded ? "Stored locally" : "Remote only", selectedVersion.IsDownloaded ? SuccessColor : InfoColor);
                if (!string.IsNullOrWhiteSpace(selectedVersion.FileSizeDisplay))
                {
                    ImGui.SameLine();
                    RenderInlinePill(selectedVersion.FileSizeDisplay, WarningColor);
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(InfoColor, "Summary");
            RenderHeroSignal("All versions", versions.TotalCount.ToString(), InfoColor);
            RenderHeroSignal("Downloaded", versions.DownloadedCount.ToString(), SuccessColor);
            RenderHeroSignal("Online only", versions.AvailableVersions.Count.ToString(), WarningColor);
            RenderHeroSignal("Shown", versions.FilteredCount.ToString(), InfoColor);

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void RenderVersionHeroCard(OptiScalerVersion version)
    {
        var pulse = version.IsDownloading ? 0.55f + (0.45f * (0.5f + (0.5f * MathF.Sin(_uiTime * 4f)))) : 0f;
        var accent = version.IsDownloaded ? SuccessColor : InfoColor;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Lerp(PanelBackgroundColor, new Vector4(0.27f, 0.31f, 0.20f, 1f), pulse));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.38f + (0.18f * pulse)));
        ImGui.BeginChild("VersionHeroCard", new Vector2(0f, 160f), ImGuiChildFlags.Borders, PanelWindowFlags);

        if (ImGui.BeginTable("VersionHeroCardLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Primary", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Secondary", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextColumn();
            ImGui.TextColored(accent, "Selected Version");
            ImGui.TextUnformatted(version.TagName);
            TextMuted(version.IsBleedingEdge ? "Bleeding Edge source" : "Official source");
            ImGui.Spacing();
            RenderInlinePill(version.RelativeTime, InfoColor);
            if (!string.IsNullOrWhiteSpace(version.FileSizeDisplay))
            {
                ImGui.SameLine();
                RenderInlinePill(version.FileSizeDisplay, WarningColor);
            }
            ImGui.SameLine();
            RenderInlinePill(version.IsDownloaded ? "Stored locally" : "Remote only", version.IsDownloaded ? SuccessColor : InfoColor);

            ImGui.TableNextColumn();
            ImGui.TextColored(InfoColor, "Status");
            RenderHeroSignal("Source", version.IsBleedingEdge ? "Bleeding Edge" : "Official", version.IsBleedingEdge ? WarningColor : InfoColor);
            RenderHeroSignal("Published", version.PublishedAt == default ? "-" : version.PublishedAt.ToLocalTime().ToString("g"), InfoColor);
            RenderHeroSignal("Download", version.IsDownloading ? (string.IsNullOrWhiteSpace(version.DownloadStatus) ? "Downloading" : version.DownloadStatus) : (version.IsDownloaded ? "Ready to use" : "Not downloaded"), version.IsDownloaded ? SuccessColor : InfoColor);

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void RenderSettingsHero()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(InfoColor.X, InfoColor.Y, InfoColor.Z, 0.38f));
        ImGui.BeginChild("SettingsHero", new Vector2(0f, 124f), ImGuiChildFlags.Borders, PanelWindowFlags);
        ImGui.TextColored(InfoColor, "Settings");
        ImGui.TextUnformatted("Simple app defaults");
        TextMuted("These only affect the current app session. Per-game config still lives with each game.");
        ImGui.Spacing();
        RenderInlinePill("Session only", WarningColor);
        ImGui.SameLine();
        RenderInlinePill("UI only", InfoColor);
        ImGui.SameLine();
        RenderInlinePill("Desktop app", SuccessColor);
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void PromptAddGame()
    {
        var selectedPath = NativeDialogs.PickFile(
            "Select Game Executable",
            "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            _hwnd);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var selectedDirectory = Path.GetDirectoryName(selectedPath);
        var suspectedUe5RootExecutable = !string.IsNullOrWhiteSpace(selectedDirectory) &&
            Directory.Exists(Path.Combine(selectedDirectory, "Engine"));

        StartUiTask(async () =>
        {
            var addedGame = await _mainViewModel.Dashboard.AddGameFromExecutable(selectedPath);
            if (addedGame != null)
            {
                var resolvedExecutablePath = string.IsNullOrWhiteSpace(addedGame.ExecutableName)
                    ? null
                    : Path.Combine(addedGame.GamePath, addedGame.ExecutableName);
                var autoDetectedUnrealExecutable = suspectedUe5RootExecutable &&
                    !string.IsNullOrWhiteSpace(resolvedExecutablePath) &&
                    !Path.GetFullPath(selectedPath).Equals(Path.GetFullPath(resolvedExecutablePath), StringComparison.OrdinalIgnoreCase);

                if (autoDetectedUnrealExecutable)
                {
                    SetNotification(
                        $"Added {addedGame.Name}. Auto-detected the Unreal executable as {addedGame.ExecutableName}. If that's not the right one, remove it and pick the correct exe from the game's Binaries\\Win64 folder.",
                        NotificationKind.Warning);
                    return;
                }

                if (suspectedUe5RootExecutable)
                {
                    SetNotification(
                        $"Added {addedGame.Name}. This might still be a UE5 root executable. If so, use the exe in the game's Binaries\\Win64 folder instead, usually the one with Win64 in the name.",
                        NotificationKind.Warning);
                    return;
                }

                SetNotification($"Added {addedGame.Name}.", NotificationKind.Success);
                return;
            }

            SetNotification("That executable's folder is already in the library.", NotificationKind.Info);
        }, "Could not add the selected game");
    }

    private void PromptChangeGamePath(DashboardViewModel dashboard, GameInstance game)
    {
        var selectedPath = NativeDialogs.PickFile(
            "Select Game Executable",
            "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            _gameDetailsWindow?.WindowHandle ?? _hwnd,
            game.GamePath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        StartUiTask(async () =>
        {
            await dashboard.UpdateGameExecutable(game, selectedPath);
            _selectedGamePath = game.GamePath;
            if (_gameDetailsDialog != null)
            {
                _gameDetailsDialog.ConfigViewModel = null;
            }

            if (_gameDetailsWindow != null && !_gameDetailsWindow.IsClosed)
            {
                _gameDetailsWindow.SetTitle($"Game Details - {game.Name}");
            }
        }, "Could not update game path", $"Updated the tracked path for {game.Name}.");
    }

    private float AnimateValue(string key, float target, float speed = 12f)
    {
        var current = _animationValues.GetValueOrDefault(key);
        var delta = Math.Clamp(ImGui.GetIO().DeltaTime * speed, 0f, 1f);
        current += (target - current) * delta;
        _animationValues[key] = current;
        return current;
    }

    private static Vector2 GetInlinePillSize(string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(10f, 6f);
        return textSize + (padding * 2f);
    }

    private static void DrawInlinePill(ImDrawListPtr drawList, Vector2 min, string text, Vector4 accent)
    {
        var padding = new Vector2(10f, 6f);
        var size = GetInlinePillSize(text);
        var max = min + size;

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.15f)), 2f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.45f)), 2f);
        drawList.AddText(min + padding, ImGui.ColorConvertFloat4ToU32(accent), text);
    }

    private static void RenderInlinePill(string text, Vector4 accent)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = GetInlinePillSize(text);
        var drawList = ImGui.GetWindowDrawList();

        ImGui.Dummy(size);
        DrawInlinePill(drawList, min, text, accent);
    }

    private static bool RenderClickableInlinePill(string id, string text, Vector4 accent)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = GetInlinePillSize(text);
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        DrawInlinePill(drawList, min, text, hovered
            ? new Vector4(MathF.Min(1f, accent.X + 0.10f), MathF.Min(1f, accent.Y + 0.10f), MathF.Min(1f, accent.Z + 0.10f), accent.W)
            : accent);
        return clicked;
    }

    private static string GetOptiScalerQuickPillText(GameInstance game)
    {
        return $"OptiScaler {game.CurrentVersion}";
    }

    private static string GetUpscalersFgQuickPillText()
    {
        return "Upscalers/FG";
    }

    private static IReadOnlyList<ComponentDllEntry> GetComponentDllEntries(GameInstance game)
    {
        return new[]
        {
            CreateComponentDllEntry(game.GamePath, "FSR Upscaler", "amd_fidelityfx_upscaler_dx12.dll"),
            CreateComponentDllEntry(game.GamePath, "FSR FG", "amd_fidelityfx_framegeneration_dx12.dll"),
            CreateComponentDllEntry(game.GamePath, "XeSS", "libxess.dll"),
            CreateComponentDllEntry(game.GamePath, "XeFG", "libxess_fg.dll"),
        };
    }

    private static ComponentDllEntry CreateComponentDllEntry(string gamePath, string label, string fileName)
    {
        var version = GetInstalledDllVersion(gamePath, fileName, out var isDetected);
        return new ComponentDllEntry(label, fileName, version, isDetected);
    }

    private static string GetInstalledDllVersion(string gamePath, string fileName, out bool isDetected)
    {
        var path = Path.Combine(gamePath, fileName);
        if (!File.Exists(path))
        {
            isDetected = false;
            return "Not detected";
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var productVersion = NormalizeDetectedDllVersion(versionInfo.ProductVersion, trimTrailingBuildSegment: false);
            if (!string.IsNullOrWhiteSpace(productVersion))
            {
                isDetected = true;
                return productVersion;
            }

            var fileVersion = NormalizeDetectedDllVersion(versionInfo.FileVersion, trimTrailingBuildSegment: true);
            isDetected = true;
            return string.IsNullOrWhiteSpace(fileVersion) ? "Unknown" : fileVersion;
        }
        catch
        {
            isDetected = true;
            return "Unknown";
        }
    }

    private static string NormalizeDetectedDllVersion(string? rawVersion, bool trimTrailingBuildSegment)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return string.Empty;
        }

        var version = rawVersion.Trim();
        version = Regex.Replace(version, @"\s+\(([0-9a-f]{7,40})\)$", string.Empty, RegexOptions.IgnoreCase);
        if (trimTrailingBuildSegment && version.EndsWith(".0", StringComparison.Ordinal) && version.Split('.').Length >= 4)
        {
            version = version[..^2];
        }

        return version.Trim();
    }

    private static void RenderHeroSignal(string label, string value, Vector4 accent)
    {
        ImGui.TextColored(accent, value);
        ImGui.SameLine();
        ImGui.TextDisabled(label);
    }

    private static Vector2 ClampWindowPositionToViewport(Vector2 windowPos, Vector2 windowSize)
    {
        var viewport = ImGui.GetMainViewport();
        var minPos = viewport.WorkPos;
        var maxPos = new Vector2(
            viewport.WorkPos.X + Math.Max(0f, viewport.WorkSize.X - windowSize.X),
            viewport.WorkPos.Y + Math.Max(0f, viewport.WorkSize.Y - windowSize.Y));
        return new Vector2(
            Math.Clamp(windowPos.X, minPos.X, maxPos.X),
            Math.Clamp(windowPos.Y, minPos.Y, maxPos.Y));
    }

    private void UpdateConfirmationPopupPositionFromDrag()
    {
        if (!_confirmationPopupPosition.HasValue || !_isDraggingConfirmationPopup)
        {
            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _isDraggingConfirmationPopup = false;
            return;
        }

        var desiredPos = ImGui.GetIO().MousePos - _confirmationPopupDragOffset;
        _confirmationPopupPosition = ClampWindowPositionToViewport(desiredPos, _confirmationPopupSize);
    }

    private void RenderConfirmationDialogHeader()
    {
        if (_confirmation == null || !_confirmationPopupPosition.HasValue)
        {
            return;
        }

        var style = ImGui.GetStyle();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var contentWidth = Math.Max(1f, windowSize.X - (style.WindowPadding.X * 2f));
        var headerMin = windowPos;
        var headerHeight = Math.Max(34f, ImGui.GetFrameHeight() + 8f);
        var headerSize = new Vector2(contentWidth, headerHeight);
        var headerMax = headerMin + headerSize;
        var headerDrawMax = new Vector2(windowPos.X + windowSize.X, headerMax.Y);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(headerMin, headerDrawMax, ImGui.ColorConvertFloat4ToU32(PanelRaisedBackgroundColor), 0f);
        drawList.AddLine(
            new Vector2(headerMin.X, headerMax.Y - 1f),
            new Vector2(headerDrawMax.X, headerMax.Y - 1f),
            ImGui.ColorConvertFloat4ToU32(PanelBorderColor));

        var titleSize = ImGui.CalcTextSize(_confirmation.Title);
        var titlePos = new Vector2(
            headerMin.X + style.WindowPadding.X,
            headerMin.Y + ((headerSize.Y - titleSize.Y) * 0.5f));

        drawList.AddText(titlePos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), _confirmation.Title);

        ImGui.SetCursorScreenPos(new Vector2(windowPos.X + style.WindowPadding.X, headerMin.Y));
        ImGui.InvisibleButton("##ConfirmationDragHandle", headerSize);
        if (ImGui.IsItemActivated())
        {
            _isDraggingConfirmationPopup = true;
            _confirmationPopupDragOffset = ImGui.GetIO().MousePos - _confirmationPopupPosition.Value;
        }

        ImGui.SetCursorScreenPos(new Vector2(
            windowPos.X + style.WindowPadding.X,
            windowPos.Y + headerHeight + style.ItemSpacing.Y));
    }

    private void RenderConfirmationDialog(ConfirmationHost host)
    {
        if (_confirmation == null || _confirmationHost != host)
        {
            return;
        }

        var popupId = _confirmation.PopupId;
        if (_openConfirmationPopup)
        {
            ImGui.OpenPopup(popupId);
            _confirmationPopupPosition = null;
            _confirmationPopupSize = new Vector2(460f, 0f);
            _confirmationPopupDragOffset = Vector2.Zero;
            _isDraggingConfirmationPopup = false;
            _openConfirmationPopup = false;
        }

        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(
            viewport.WorkPos.X + (viewport.WorkSize.X * 0.5f),
            viewport.WorkPos.Y + (viewport.WorkSize.Y * 0.5f));

        UpdateConfirmationPopupPositionFromDrag();
        if (_confirmationPopupPosition.HasValue)
        {
            ImGui.SetNextWindowPos(_confirmationPopupPosition.Value, ImGuiCond.Always);
        }
        else
        {
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        }

        ImGui.SetNextWindowSize(new Vector2(460f, 0f), ImGuiCond.Appearing);

        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar))
        {
            return;
        }

        _confirmationPopupSize = ImGui.GetWindowSize();
        _confirmationPopupPosition = ClampWindowPositionToViewport(ImGui.GetWindowPos(), _confirmationPopupSize);

        RenderConfirmationDialogHeader();

        ImGui.TextWrapped(_confirmation.Message);
        ImGui.Spacing();

        var confirmButtonWidth = GetButtonWidth(_confirmation.ConfirmLabel, 110f);
        var cancelButtonWidth = GetButtonWidth("Cancel", 110f);

        if (ImGui.Button(_confirmation.ConfirmLabel, new Vector2(confirmButtonWidth, 0f)))
        {
            var action = _confirmation.ConfirmAction;
            var successMessage = _confirmation.SuccessMessage;
            var failureMessage = _confirmation.FailureMessage ?? $"{_confirmation.Title} failed";

            StartUiTask(async () =>
            {
                await action();
                if (!string.IsNullOrWhiteSpace(successMessage))
                {
                    SetNotification(successMessage, NotificationKind.Success);
                }
            }, failureMessage);

            ImGui.CloseCurrentPopup();
            CloseConfirmationDialog();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(cancelButtonWidth, 0f)))
        {
            ImGui.CloseCurrentPopup();
            CloseConfirmationDialog();
        }

        ImGui.EndPopup();
    }

    private void RenderUpdateWindow()
    {
        if (_updateDialog == null)
        {
            return;
        }

        RenderNotification();
        RenderConfirmationDialog(ConfirmationHost.UpdateWindow);

        ImGui.TextWrapped("Select the downloaded OptiScaler version to apply.");
        ImGui.Spacing();

        var selectedVersion = _updateDialog.SelectedVersion;
        DrawVersionCombo("Version", _mainViewModel.Dashboard.DownloadedVersions, ref selectedVersion);
        _updateDialog.SelectedVersion = selectedVersion;

        ImGui.Spacing();
        if (ImGui.Button("Update", new Vector2(110f, 0f)))
        {
            if (_updateDialog.SelectedVersion == null)
            {
                SetNotification("Select a version before updating.", NotificationKind.Info);
            }
            else
            {
                var game = _updateDialog.Game;
                var versionToInstall = _updateDialog.SelectedVersion;

                StartProtectedUiTask(
                    game.GamePath,
                    ConfirmationHost.MainWindow,
                    BuildAdministratorPromptMessage(game.GamePath, "update files in this directory"),
                    () => _mainViewModel.Dashboard.UpdateOptiScaler(game, versionToInstall),
                    $"Could not update {game.Name}",
                    $"Updated {game.Name} to {versionToInstall.TagName}.");

                CloseUpdateDialog();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
        {
            CloseUpdateDialog();
        }
    }

    private void RenderComponentDllWindow()
    {
        if (_componentDllDialog == null)
        {
            return;
        }

        var game = _componentDllDialog.Game;
        ImGui.TextWrapped("Review the optional upscaler and frame generation DLLs next to OptiScaler. DLL swapping is not wired yet, so the change controls are placeholders for now.");
        ImGui.Spacing();

        if (ImGui.BeginTable("ComponentDllTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Component", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("DLL", ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableHeadersRow();

            foreach (var component in GetComponentDllEntries(game))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(component.Label);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(component.FileName);
                ImGui.TableNextColumn();
                ImGui.TextColored(component.IsDetected ? SuccessColor : WarningColor, component.Version);
                ImGui.TableNextColumn();
                if (ImGui.Button($"Change##{component.FileName}", new Vector2(90f, 0f)))
                {
                    SetNotification($"Changing {component.Label} DLLs is not implemented yet.", NotificationKind.Info);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.Button("Close", new Vector2(110f, 0f)))
        {
            CloseComponentDllDialog();
        }
    }

    private void RenderConfigWindow()
    {
        if (_configDialog == null)
        {
            return;
        }

        var enableSpoofing = _configDialog.ViewModel.EnableSpoofing;
        if (ImGui.Checkbox("Enable DLSS spoofing", ref enableSpoofing))
        {
            _configDialog.ViewModel.EnableSpoofing = enableSpoofing;
        }

        var enableOverlay = _configDialog.ViewModel.EnableOverlay;
        if (ImGui.Checkbox("Enable overlay menu", ref enableOverlay))
        {
            _configDialog.ViewModel.EnableOverlay = enableOverlay;
        }

        var upscalerIndex = _configDialog.ViewModel.UpscalerIndex;
        DrawStringCombo("Backend upscaler", _configDialog.ViewModel.Upscalers, ref upscalerIndex);
        _configDialog.ViewModel.UpscalerIndex = upscalerIndex;

        var renderScale = _configDialog.ViewModel.RenderScale;
        if (ImGui.SliderFloat("Render scale", ref renderScale, 0.5f, 1.0f, "%.1f"))
        {
            _configDialog.ViewModel.RenderScale = renderScale;
        }

        var sharpness = _configDialog.ViewModel.Sharpness;
        if (ImGui.SliderFloat("Sharpness", ref sharpness, 0f, 1f, "%.1f"))
        {
            _configDialog.ViewModel.Sharpness = sharpness;
        }

        ImGui.Spacing();
        if (ImGui.Button("Open File", new Vector2(110f, 0f)))
        {
            try
            {
                _configDialog.ViewModel.OpenFile();
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message, NotificationKind.Error);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Save", new Vector2(110f, 0f)))
        {
            try
            {
                _configDialog.ViewModel.Save();
                SetNotification($"Saved configuration for {_configDialog.Game.Name}.", NotificationKind.Success);
                CloseConfigDialog();
            }
            catch (Exception ex)
            {
                SetNotification(ex.Message, NotificationKind.Error);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
        {
            CloseConfigDialog();
        }
    }

    private void RenderInstallationWindow()
    {
        if (_installationDialog == null)
        {
            return;
        }

        RenderNotification();
        RenderConfirmationDialog(ConfirmationHost.InstallationWindow);

        var wizard = _installationDialog.ViewModel;

        ImGui.BeginChild("WizardSteps", new Vector2(220f, -54f), ImGuiChildFlags.Borders);
        RenderWizardStepList(wizard);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("WizardContent", new Vector2(0f, -54f), ImGuiChildFlags.Borders);
        var stepCount = wizard.IsNvidia ? 6f : 7f;
        var stepProgress = Math.Clamp((wizard.StepIndex + 1f) / stepCount, 0f, 1f);
        ImGui.ProgressBar(stepProgress, new Vector2(-1f, 0f), wizard.Title);
        ImGui.Spacing();
        RenderWizardStep(wizard);
        ImGui.EndChild();

        ImGui.Separator();
        if (wizard.CanGoBack && ImGui.Button("Back", new Vector2(100f, 0f)))
        {
            wizard.Back();
        }

        if (!wizard.InstallSuccess)
        {
            if (wizard.CanGoBack)
            {
                ImGui.SameLine();
            }

            if (!wizard.IsInstalling && ImGui.Button("Cancel", new Vector2(100f, 0f)))
            {
                CloseInstallationDialog(showSuccessMessage: false);
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(wizard.IsInstalling || !wizard.CanGoNext);
            if (ImGui.Button(wizard.NextButtonText, new Vector2(120f, 0f)))
            {
                if (wizard.StepIndex == 5 && wizard.RequiresAdministratorAccess)
                {
                    QueueConfirmation(
                        ConfirmationHost.InstallationWindow,
                        "Administrator access required",
                        $"OptiManager needs administrator privileges to install files in this protected folder:\n{wizard.Options.GamePath}\n\nContinue to show the Windows UAC prompt, or Cancel to stop the installation.",
                        "Continue",
                        () => wizard.Next(),
                        null,
                        "The installation wizard failed");
                }
                else
                {
                    StartUiTask(() => wizard.Next(), "The installation wizard failed");
                }
            }
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Finish", new Vector2(120f, 0f)))
        {
            CloseInstallationDialog(showSuccessMessage: true);
        }
    }

    private void RenderWizardStepList(InstallationWizardViewModel wizard)
    {
        var steps = wizard.IsNvidia
            ? new[] { "Welcome", "Select Version", "Select Filename", "Configuration", "Ready to Install", "Finish" }
            : new[] { "Welcome", "Select Version", "Select Filename", "Configuration", "OptiPatcher", "Ready to Install", "Finish" };

        var visualStep = wizard.IsNvidia && wizard.StepIndex > 3
            ? Math.Max(0, wizard.StepIndex - 1)
            : wizard.StepIndex;

        ImGui.TextUnformatted("INSTALLATION STEPS");
        ImGui.Spacing();

        for (var index = 0; index < steps.Length; index++)
        {
            var color = index < visualStep
                ? SuccessColor
                : index == visualStep
                    ? InfoColor
                    : MutedTextColor;

            ImGui.TextColored(color, $"{index + 1}. {steps[index]}");
            ImGui.Spacing();
        }
    }

    private void RenderWizardStep(InstallationWizardViewModel wizard)
    {
        switch (wizard.StepIndex)
        {
            case 0:
                ImGui.TextWrapped("This wizard configures and installs OptiScaler into the selected game folder.");
                if (!wizard.IsSupportedArchitecture)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(ErrorColor, wizard.UnsupportedArchitectureMessage);
                }
                if (wizard.ShowEngineWarning)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(WarningColor, "Engine folder detected. Open the folder with the game's name, then Binaries\\Win64, and choose the shipping exe, usually the one with Win64-Shipping in the name.");
                }
                break;

            case 1:
                ImGui.TextWrapped("Choose which downloaded version to install.");
                ImGui.Spacing();
                var wizardVersion = wizard.SelectedVersion;
                DrawVersionCombo("Version", wizard.AvailableVersions, ref wizardVersion);
                wizard.SelectedVersion = wizardVersion;
                break;

            case 2:
                ImGui.TextWrapped("Select the DLL filename the game should load.");
                ImGui.Spacing();
                var selectedFilename = wizard.SelectedFilename;
                DrawStringCombo("Target filename", wizard.Filenames, ref selectedFilename);
                wizard.SelectedFilename = selectedFilename;

                var conflict = wizard.SelectedFilenameConflict;
                if (conflict.IsOptiScaler)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(SuccessColor, $"{wizard.SelectedFilename} already belongs to OptiScaler and will be updated in place.");
                }
                else if (conflict.HasRiskyConflict)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(WarningColor, $"{wizard.SelectedFilename} is already used by {conflict.ExistingProvider}.");
                    if (!string.IsNullOrWhiteSpace(conflict.ExistingDetails))
                    {
                        TextMuted(conflict.ExistingDetails);
                    }

                    if (conflict.HasChainedLoaderRecommendation)
                    {
                        TextMuted(conflict.ChainedLoaderInstructions);
                    }
                    else if (conflict.HasRecommendedFilename)
                    {
                        var recommendation = BuildWizardFilenameRecommendation(conflict, wizard.SelectedFilename);
                        TextMuted(recommendation);

                        var buttonWidth = GetButtonWidth($"Use {conflict.RecommendedFilename}", 180f);
                        if (ImGui.Button($"Use {conflict.RecommendedFilename}", new Vector2(buttonWidth, 0f)))
                        {
                            wizard.UseRecommendedFilename();
                        }
                    }

                    if (wizard.FileExistsWarning)
                    {
                        ImGui.Spacing();
                        ImGui.TextColored(ErrorColor, "Click Next again only if you intentionally want to overwrite the existing file.");
                    }
                }
                else if (conflict.HasChainedLoaderRecommendation)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(InfoColor, $"Detected {conflict.ChainedLoaderProvider}. {wizard.SelectedFilename} can stay selected and OptiScaler can manage the hand-off automatically.");
                    TextMuted(conflict.ChainedLoaderInstructions);
                }
                else if (conflict.ShouldPreferAsiInstall)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(InfoColor, $"Detected {conflict.AsiLoaderProvider}. Using OptiScaler.asi is safer than installing a second proxy DLL.");
                    TextMuted(BuildWizardFilenameRecommendation(conflict, wizard.SelectedFilename));
                    var buttonWidth = GetButtonWidth("Use OptiScaler.asi", 180f);
                    if (ImGui.Button("Use OptiScaler.asi", new Vector2(buttonWidth, 0f)))
                    {
                        wizard.UseRecommendedFilename();
                    }
                }
                else if (conflict.RequiresAsiLoader)
                {
                    ImGui.Spacing();
                    if (conflict.HasDetectedAsiLoader)
                    {
                        ImGui.TextColored(SuccessColor, $"Detected {conflict.AsiLoaderProvider}. OptiScaler.asi can be used without replacing the current proxy DLL.");
                        if (!string.IsNullOrWhiteSpace(conflict.AsiLoaderInstructions))
                        {
                            TextMuted(conflict.AsiLoaderInstructions);
                        }
                    }
                    else
                    {
                        ImGui.TextColored(WarningColor, "OptiScaler.asi needs an existing ASI loader in the game folder. None was detected automatically.");
                    }
                }
                break;

            case 3:
                ImGui.TextWrapped(wizard.GpuName);
                ImGui.Spacing();
                var enableSpoofing = wizard.EnableSpoofing;
                if (ImGui.Checkbox("Enable DXGI spoofing", ref enableSpoofing))
                {
                    wizard.EnableSpoofing = enableSpoofing;
                }
                TextMuted("Required for some frame generation paths. Some games render better with it disabled.");
                break;

            case 4:
                if (wizard.CheckingOptiPatcher)
                {
                    ImGui.TextWrapped("Checking OptiPatcher compatibility...");
                    break;
                }

                ImGui.TextWrapped(wizard.OptiPatcherStatus);
                ImGui.Spacing();

                if (wizard.OptiPatcherSupported)
                {
                    var useOptiPatcher = wizard.UseOptiPatcher;
                    if (ImGui.Checkbox("Install OptiPatcher.asi", ref useOptiPatcher))
                    {
                        wizard.UseOptiPatcher = useOptiPatcher;
                    }
                }
                else if (ImGui.Button("Force Install OptiPatcher"))
                {
                    wizard.ForceOptiPatcher();
                }
                break;

            case 5:
                ImGui.TextUnformatted($"Version: {wizard.SelectedVersion?.TagName ?? "None"}");
                ImGui.TextUnformatted($"Filename: {wizard.SelectedFilename}");
                ImGui.TextUnformatted($"Spoofing: {wizard.EnableSpoofing}");
                ImGui.TextUnformatted($"OptiPatcher: {wizard.UseOptiPatcher}");
                if (wizard.RequiresAdministratorAccess)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(WarningColor, "This folder is protected. OptiManager will ask for administrator approval before writing files.");
                }
                ImGui.Spacing();
                var createUninstaller = wizard.CreateUninstaller;
                if (ImGui.Checkbox("Create uninstaller script", ref createUninstaller))
                {
                    wizard.CreateUninstaller = createUninstaller;
                }

                if (!string.IsNullOrWhiteSpace(wizard.InstallStatus))
                {
                    ImGui.Spacing();
                    ImGui.TextWrapped(wizard.InstallStatus);
                }
                break;

            case 6:
                if (wizard.IsInstalling)
                {
                    ImGui.TextWrapped("Installing...");
                    if (!string.IsNullOrWhiteSpace(wizard.InstallStatus))
                    {
                        ImGui.Spacing();
                        ImGui.TextWrapped(wizard.InstallStatus);
                    }
                }
                else
                {
                    ImGui.TextColored(SuccessColor, "Installation complete.");
                    ImGui.TextWrapped("OptiScaler has been written to the selected game directory.");
                }
                break;
        }
    }

    private void OpenGameDetailsDialog(GameInstance game)
    {
        _gameDetailsDialog = new GameDetailsDialogState
        {
            Game = game,
        };

        var (detailsWidth, detailsHeight) = GetGameDetailsClientSize();

        if (_gameDetailsWindow == null || _gameDetailsWindow.IsClosed)
        {
            _gameDetailsWindow = PreserveImGuiContext(() =>
                CreateNativeWindow(
                    $"Game Details - {game.Name}",
                    detailsWidth,
                    detailsHeight,
                    "GameDetailsRoot",
                    RenderGameDetailsWindow));
            return;
        }

        _gameDetailsWindow.SetMinClientSize(detailsWidth, detailsHeight);
        _gameDetailsWindow.SetTitle($"Game Details - {game.Name}");
        _gameDetailsWindow.Focus();
    }

    private void OpenInstallationDialog(GameInstance game)
    {
        try
        {
            if (_installationWindow != null && !_installationWindow.IsClosed && !CanCloseInstallationWindow())
            {
                _installationWindow.Focus();
                return;
            }

            _installationDialog = new InstallationDialogState
            {
                Game = game,
                ViewModel = _mainViewModel.Dashboard.CreateInstallationWizard(game),
            };

            PreserveImGuiContext(() =>
            {
                _installationWindow?.Dispose();
                _installationWindow = CreateNativeWindow(
                    $"Install OptiScaler - {game.Name}",
                    920,
                    600,
                    "InstallationRoot",
                    RenderInstallationWindow,
                    CanCloseInstallationWindow);
            });
        }
        catch (Exception ex)
        {
            SetNotification(ex.Message, NotificationKind.Error);
        }
    }

    private void StartQuickInstall(GameInstance game, DashboardViewModel dashboard)
    {
        try
        {
            var wizard = dashboard.CreateInstallationWizard(game);
            wizard.SelectedFilename = dashboard.TargetFilename;
            wizard.EnableSpoofing = dashboard.EnableSpoofing;

            if (TryHandleQuickInstallConflict(game, wizard))
            {
                return;
            }

            StartProtectedUiTask(
                game.GamePath,
                ConfirmationHost.MainWindow,
                BuildAdministratorPromptMessage(game.GamePath, "install OptiScaler in this directory"),
                () => RunQuickInstallAsync(game, wizard),
                "Could not install OptiScaler",
                $"Installed OptiScaler for {game.Name}.");
        }
        catch (Exception ex)
        {
            SetNotification(ex.Message, NotificationKind.Error);
        }
    }

    private bool TryHandleQuickInstallConflict(GameInstance game, InstallationWizardViewModel wizard)
    {
        var conflict = wizard.SelectedFilenameConflict;
        var protectedFolderMessage = ElevatedOperationService.RequiresElevation(game.GamePath)
            ? BuildProtectedFolderNotice("Continuing will require administrator approval before OptiManager can write to this directory.")
            : string.Empty;
        if (conflict.HasChainedLoaderRecommendation)
        {
            QueueConfirmation(
                ConfirmationHost.MainWindow,
                $"{conflict.ChainedLoaderProvider} detected",
                BuildQuickInstallManagedChainMessage(conflict) + protectedFolderMessage,
                $"Install + load {conflict.ChainedLoaderProvider}",
                async () =>
                {
                    await RunQuickInstallAsync(game, wizard);
                },
                $"Installed OptiScaler for {game.Name} and configured {conflict.ChainedLoaderProvider} chaining.",
                "Could not install OptiScaler");

            return true;
        }

        if (conflict.ShouldPreferAsiInstall)
        {
            QueueConfirmation(
                ConfirmationHost.MainWindow,
                $"{conflict.AsiLoaderProvider} detected",
                BuildQuickInstallAsiRecommendationMessage(conflict) + protectedFolderMessage,
                "Use OptiScaler.asi",
                async () =>
                {
                    wizard.SelectedFilename = "OptiScaler.asi";
                    await RunQuickInstallAsync(game, wizard);
                },
                $"Installed OptiScaler for {game.Name} using OptiScaler.asi.",
                "Could not install OptiScaler");

            return true;
        }

        if (conflict.HasRiskyConflict)
        {
            if (conflict.HasRecommendedFilename)
            {
                var recommendedFilename = conflict.RecommendedFilename;
                QueueConfirmation(
                    ConfirmationHost.MainWindow,
                    $"{conflict.TargetFilename} already in use",
                    BuildQuickInstallConflictMessage(conflict) + protectedFolderMessage,
                    $"Use {recommendedFilename}",
                    async () =>
                    {
                        wizard.SelectedFilename = recommendedFilename;
                        await RunQuickInstallAsync(game, wizard);
                    },
                    $"Installed OptiScaler for {game.Name} using {recommendedFilename}.",
                    "Could not install OptiScaler");
            }
            else
            {
                QueueConfirmation(
                    ConfirmationHost.MainWindow,
                    $"{conflict.TargetFilename} already in use",
                    BuildQuickInstallConflictMessage(conflict) + protectedFolderMessage,
                    "Open Installer",
                    () =>
                    {
                        OpenInstallationDialog(game);
                        return Task.CompletedTask;
                    },
                    null);
            }

            return true;
        }

        if (conflict.RequiresAsiLoader && !conflict.HasDetectedAsiLoader)
        {
            QueueConfirmation(
                ConfirmationHost.MainWindow,
                "ASI loader required",
                "OptiScaler.asi needs an existing ASI loader in the game folder. Open the installer to choose another target filename unless you already know an ASI loader is present." + protectedFolderMessage,
                "Open Installer",
                () =>
                {
                    OpenInstallationDialog(game);
                    return Task.CompletedTask;
                },
                null);
            return true;
        }

        return false;
    }

    private static string BuildQuickInstallConflictMessage(InstallTargetConflictInfo conflict)
    {
        var details = string.IsNullOrWhiteSpace(conflict.ExistingDetails)
            ? conflict.ExistingProvider
            : $"{conflict.ExistingProvider} ({conflict.ExistingDetails})";

        if (conflict.HasRecommendedFilename)
        {
            if (conflict.RecommendedFilename.Equals("OptiScaler.asi", StringComparison.OrdinalIgnoreCase) && conflict.HasDetectedAsiLoader)
            {
                var instructions = string.IsNullOrWhiteSpace(conflict.AsiLoaderInstructions)
                    ? string.Empty
                    : $" {conflict.AsiLoaderInstructions}";
                return $"{conflict.TargetFilename} is already used by {details}. Use {conflict.RecommendedFilename} instead and keep the existing loader in place.{instructions}";
            }

            return $"{conflict.TargetFilename} is already used by {details}. Use {conflict.RecommendedFilename} for this install, or open the full installer if you want to choose another target manually.";
        }

        return $"{conflict.TargetFilename} is already used by {details}. Open the full installer to pick another target or intentionally overwrite it.";
    }

    private static string BuildQuickInstallManagedChainMessage(InstallTargetConflictInfo conflict)
    {
        return string.IsNullOrWhiteSpace(conflict.ChainedLoaderInstructions)
            ? $"{conflict.ChainedLoaderProvider} was detected. OptiScaler can keep using {conflict.TargetFilename} and manage the loader hand-off automatically."
            : conflict.ChainedLoaderInstructions;
    }

    private static string BuildQuickInstallAsiRecommendationMessage(InstallTargetConflictInfo conflict)
    {
        return string.IsNullOrWhiteSpace(conflict.AsiLoaderInstructions)
            ? $"{conflict.AsiLoaderProvider} was detected in the game folder. Using OptiScaler.asi is safer than installing a second proxy DLL."
            : $"{conflict.AsiLoaderProvider} was detected in the game folder. Use OptiScaler.asi instead. {conflict.AsiLoaderInstructions}";
    }

    private static string BuildWizardFilenameRecommendation(InstallTargetConflictInfo conflict, string currentFilename)
    {
        if (conflict.HasChainedLoaderRecommendation)
        {
            return conflict.ChainedLoaderInstructions;
        }

        if (conflict.ShouldPreferAsiInstall)
        {
            return string.IsNullOrWhiteSpace(conflict.AsiLoaderInstructions)
                ? $"Recommended: use OptiScaler.asi instead of {currentFilename} so the existing {conflict.AsiLoaderProvider} install stays in charge of DLL loading."
                : $"Recommended: use OptiScaler.asi instead of {currentFilename}. {conflict.AsiLoaderInstructions}";
        }

        return $"Recommended: install OptiScaler as {conflict.RecommendedFilename} instead of overwriting {currentFilename}.";
    }

    private async Task RunQuickInstallAsync(GameInstance game, InstallationWizardViewModel wizard)
    {
        try
        {
            await wizard.InstallWithDefaultsAsync();
        }
        finally
        {
            _mainViewModel.Dashboard.RefreshGameInstallation(game);
        }
    }

    private void CloseInstallationDialog(bool showSuccessMessage)
    {
        FinalizeInstallationDialog(showSuccessMessage);
        _installationWindow?.RequestClose();
    }

    private void FinalizeInstallationDialog(bool showSuccessMessage)
    {
        if (_installationDialog == null)
        {
            return;
        }

        var game = _installationDialog.Game;
        _mainViewModel.Dashboard.RefreshGameInstallation(game);

        if (showSuccessMessage && game.IsInstalled)
        {
            SetNotification($"Installed OptiScaler for {game.Name}.", NotificationKind.Success);
        }

        _installationDialog = null;
    }

    private void OpenConfigDialog(GameInstance game)
    {
        try
        {
            _configDialog = new ConfigDialogState
            {
                Game = game,
                ViewModel = _mainViewModel.Dashboard.CreateGameConfig(game),
            };

            PreserveImGuiContext(() =>
            {
                _configWindow?.Dispose();
                _configWindow = CreateNativeWindow($"Configuration - {game.Name}", 560, 520, "ConfigRoot", RenderConfigWindow);
            });
        }
        catch (Exception ex)
        {
            SetNotification(ex.Message, NotificationKind.Error);
        }
    }

    private void OpenUpdateDialog(GameInstance game)
    {
        if (_mainViewModel.Dashboard.DownloadedVersions.Count == 0)
        {
            SetNotification("Download a version before updating a game.", NotificationKind.Info);
            return;
        }

        _updateDialog = new UpdateDialogState
        {
            Game = game,
            SelectedVersion = _mainViewModel.Dashboard.SelectedVersion ?? _mainViewModel.Dashboard.DownloadedVersions[0],
        };

        PreserveImGuiContext(() =>
        {
            _updateWindow?.Dispose();
            _updateWindow = CreateNativeWindow($"Update OptiScaler - {game.Name}", 520, 320, "UpdateRoot", RenderUpdateWindow);
        });
    }

    private void OpenComponentDllDialog(GameInstance game)
    {
        _componentDllDialog = new ComponentDllDialogState
        {
            Game = game,
        };

        PreserveImGuiContext(() =>
        {
            _componentDllWindow?.Dispose();
            _componentDllWindow = CreateNativeWindow($"Upscalers / FG - {game.Name}", 760, 340, "ComponentDllRoot", RenderComponentDllWindow);
        });
    }

    private void QueueConfirmation(ConfirmationHost host, string title, string message, string confirmLabel, Func<Task> confirmAction, string? successMessage, string? failureMessage = null)
    {
        _confirmation = new ConfirmationDialogState(
            $"{ConfirmationPopupIdPrefix}{Guid.NewGuid():N}",
            title,
            message,
            confirmLabel,
            confirmAction,
            successMessage,
            failureMessage);
        _confirmationHost = host;
        _confirmationPopupPosition = null;
        _confirmationPopupSize = new Vector2(460f, 0f);
        _confirmationPopupDragOffset = Vector2.Zero;
        _isDraggingConfirmationPopup = false;
        _openConfirmationPopup = true;
    }

    private void CloseGameDetailsDialog()
    {
        _gameDetailsWindow?.RequestClose();
    }

    private void CloseConfirmationDialog()
    {
        _confirmation = null;
        _confirmationHost = null;
        _confirmationPopupPosition = null;
        _confirmationPopupDragOffset = Vector2.Zero;
        _isDraggingConfirmationPopup = false;
        _openConfirmationPopup = false;
    }

    private void CloseUpdateDialog()
    {
        _updateDialog = null;
        _updateWindow?.RequestClose();
    }

    private void CloseComponentDllDialog()
    {
        _componentDllDialog = null;
        _componentDllWindow?.RequestClose();
    }

    private void CloseConfigDialog()
    {
        _configDialog = null;
        _configWindow?.RequestClose();
    }

    private void StartUiTask(Func<Task> action, string failureMessage, string? successMessage = null)
    {
        _ = RunUiTaskAsync(action, failureMessage, successMessage);
    }

    private void StartProtectedUiTask(
        string directoryPath,
        ConfirmationHost host,
        string promptMessage,
        Func<Task> action,
        string failureMessage,
        string? successMessage = null)
    {
        if (ElevatedOperationService.RequiresElevation(directoryPath))
        {
            QueueConfirmation(
                host,
                "Administrator access required",
                promptMessage,
                "Continue",
                action,
                successMessage,
                failureMessage);
            return;
        }

        StartUiTask(action, failureMessage, successMessage);
    }

    private async Task RunUiTaskAsync(Func<Task> action, string failureMessage, string? successMessage)
    {
        try
        {
            await action();

            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                SetNotification(successMessage, NotificationKind.Success);
            }
        }
        catch (Exception ex)
        {
            SetNotification($"{failureMessage}: {ex.Message}", NotificationKind.Error);
        }
    }

    private static string BuildAdministratorPromptMessage(string directoryPath, string actionDescription)
    {
        return $"This folder is protected:\n{directoryPath}\n\nOptiManager needs administrator privileges to {actionDescription}. Continue to show the Windows UAC prompt, or Cancel to stop.";
    }

    private static string BuildProtectedFolderNotice(string message)
    {
        return $"\n\n{message}";
    }

    private void SetNotification(string message, NotificationKind kind)
    {
        _notificationMessage = message;
        _notificationKind = kind;
        _notificationExpiresAt = DateTime.UtcNow.AddSeconds(kind switch
        {
            NotificationKind.Error => 12,
            NotificationKind.Warning => 8,
            _ => 6,
        });
    }

    private int CountInstalledGames()
    {
        var count = 0;
        foreach (var game in _mainViewModel.Dashboard.Games)
        {
            if (game.IsInstalled)
            {
                count++;
            }
        }

        return count;
    }

    private static List<GameInstance> GetFilteredGames(IEnumerable<GameInstance> games, string query)
    {
        var trimmedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return new List<GameInstance>(games);
        }

        var filtered = new List<GameInstance>();
        foreach (var game in games)
        {
            if (game.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                game.GamePath.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(game);
            }
        }

        return filtered;
    }

    private GameInstance? ResolveSelectedGame(DashboardViewModel dashboard, IReadOnlyList<GameInstance> games)
    {
        if (games.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(_dashboardSearchQuery))
            {
                _selectedGamePath = null;
            }

            dashboard.SelectedGame = null;
            return null;
        }

        GameInstance? selectedGame = null;
        if (_selectedGamePath != null)
        {
            foreach (var game in games)
            {
                if (game.GamePath.Equals(_selectedGamePath, StringComparison.OrdinalIgnoreCase))
                {
                    selectedGame = game;
                    break;
                }
            }
        }

        if (selectedGame == null && string.IsNullOrWhiteSpace(_dashboardSearchQuery))
        {
            _selectedGamePath = null;
        }

        dashboard.SelectedGame = selectedGame;
        return selectedGame;
    }

    private OptiScalerVersion? ResolveSelectedVersion(VersionManagerViewModel versions)
    {
        OptiScalerVersion? selectedVersion = null;
        if (_selectedVersionTag != null)
        {
            foreach (var version in versions.DownloadedVersions)
            {
                if (version.TagName.Equals(_selectedVersionTag, StringComparison.OrdinalIgnoreCase))
                {
                    selectedVersion = version;
                    break;
                }
            }

            if (selectedVersion == null)
            {
                foreach (var version in versions.AvailableVersions)
                {
                    if (version.TagName.Equals(_selectedVersionTag, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedVersion = version;
                        break;
                    }
                }
            }
        }

        selectedVersion ??= versions.DownloadedVersions.Count > 0
            ? versions.DownloadedVersions[0]
            : versions.AvailableVersions.Count > 0
                ? versions.AvailableVersions[0]
                : null;

        _selectedVersionTag = selectedVersion?.TagName;
        return selectedVersion;
    }

    private static void RenderMetricRow(string scopeId, params MetricCard[] cards)
    {
        if (cards.Length == 0)
        {
            return;
        }

        if (!ImGui.BeginTable($"{scopeId}Metrics", cards.Length, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            return;
        }

        foreach (var card in cards)
        {
            ImGui.TableNextColumn();
            RenderMetricCard(scopeId, card);
        }

        ImGui.EndTable();
    }

    private static void RenderMetricCard(string scopeId, MetricCard card)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelRaisedBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(card.Accent.X, card.Accent.Y, card.Accent.Z, 0.45f));
        ImGui.BeginChild($"Metric::{scopeId}::{card.Label}", new Vector2(-1f, 84f), ImGuiChildFlags.Borders, PanelWindowFlags);
        ImGui.TextColored(card.Accent, card.Value);
        ImGui.TextUnformatted(card.Label);
        TextMuted(card.Detail);
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private DashboardGameRowAction DrawInstalledGameRow(GameInstance game, bool selected)
    {
        var uninstallWidth = GetButtonWidth("Uninstall", 110f);
        var detailsWidth = GetButtonWidth("Details", 110f);
        var actionSpacing = 8f;
        var actionsWidth = uninstallWidth + actionSpacing + detailsWidth;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f);
        var min = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - 6f);
        var lineHeight = ImGui.GetTextLineHeight();
        var buttonHeight = ImGui.GetFrameHeight();
        var contentLeft = min.X + 16f;
        var contentRight = min.X + MathF.Max(1f, availableWidth - actionsWidth - 32f);
        var pillsWidth = MathF.Max(1f, contentRight - contentLeft);
        var pillsHeight = MeasureInstalledGamePillsHeight(game, pillsWidth);
        var contentHeight = (lineHeight * 2f) + 11f + pillsHeight;
        var rowHeight = MathF.Max(88f, MathF.Max(contentHeight + 16f, buttonHeight + 20f));
        var size = new Vector2(availableWidth, rowHeight);

        if (size.X < 1f || size.Y < 1f)
        {
            ImGui.Dummy(new Vector2(1f, MathF.Max(1f, rowHeight)));
            return DashboardGameRowAction.None;
        }

        ImGui.Dummy(size);
        var rowEndCursorPos = ImGui.GetCursorPos();

        var selectWidth = MathF.Max(1f, availableWidth - actionsWidth - 24f);
        var selectHeight = MathF.Min(rowHeight, MathF.Max(44f, (lineHeight * 2f) + 20f));
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton($"GameSelect::{game.GamePath}", new Vector2(selectWidth, selectHeight));
        var hovered = ImGui.IsItemHovered();
        var selectClicked = ImGui.IsItemClicked();

        var emphasis = AnimateValue($"Row::Game::{game.GamePath}", selected ? 1f : hovered ? 0.5f : 0f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var accent = SuccessColor;
        var background = Vector4.Lerp(new Vector4(0.22f, 0.23f, 0.22f, 0.55f), new Vector4(0.29f, 0.35f, 0.22f, 0.97f), emphasis);
        var border = Vector4.Lerp(new Vector4(PanelBorderColor.X, PanelBorderColor.Y, PanelBorderColor.Z, 0.50f), new Vector4(accent.X, accent.Y, accent.Z, 0.82f), emphasis);
        var textColor = ImGui.ColorConvertFloat4ToU32(PrimaryTextColor);
        var detailColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(MutedTextColor, accent, 0.35f));
        var detail = string.IsNullOrWhiteSpace(game.InstalledFilename)
            ? "Installed"
            : $"Installed - Target DLL: {game.InstalledFilename}";
        if (!string.IsNullOrWhiteSpace(game.AntiCheatProvider))
        {
            detail += $" - {game.AntiCheatProvider}";
        }

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), 2f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 2f, ImDrawFlags.None, 1f);

        if (emphasis > 0.02f)
        {
            drawList.AddLine(
                new Vector2(min.X + 8f, min.Y + 8f),
                new Vector2(min.X + 8f, max.Y - 8f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.85f * emphasis)),
                2f);
        }

        var textStartY = min.Y + MathF.Max(8f, (rowHeight - contentHeight) * 0.5f);
        var detailY = textStartY + lineHeight + 3f;
        var pillsOrigin = new Vector2(contentLeft, detailY + lineHeight + 8f);

        drawList.AddText(new Vector2(contentLeft, textStartY), textColor, game.Name);
        drawList.AddText(new Vector2(contentLeft, detailY), detailColor, detail);
        var pillLayouts = DrawInstalledGamePills(drawList, pillsOrigin, pillsWidth, game);

        var buttonY = min.Y + MathF.Max(8f, (rowHeight - buttonHeight) * 0.5f);
        var uninstallX = max.X - actionsWidth - 16f;
        DashboardGameRowAction action = selectClicked ? DashboardGameRowAction.Select : DashboardGameRowAction.None;

        ImGui.PushID(game.GamePath);
        ImGui.SetCursorScreenPos(pillLayouts.VersionPill.Min);
        if (ImGui.InvisibleButton("UpdatePill", pillLayouts.VersionPill.Size))
        {
            action = DashboardGameRowAction.UpdateVersion;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        ImGui.SetCursorScreenPos(pillLayouts.ComponentsPill.Min);
        if (ImGui.InvisibleButton("ComponentsPill", pillLayouts.ComponentsPill.Size))
        {
            action = DashboardGameRowAction.OpenComponentDlls;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        ImGui.SetCursorScreenPos(new Vector2(uninstallX, buttonY));
        if (ImGui.Button("Uninstall", new Vector2(uninstallWidth, 0f)))
        {
            action = DashboardGameRowAction.Uninstall;
        }

        ImGui.SetCursorScreenPos(new Vector2(uninstallX + uninstallWidth + actionSpacing, buttonY));
        if (ImGui.Button("Details", new Vector2(detailsWidth, 0f)))
        {
            action = DashboardGameRowAction.Details;
        }
        ImGui.PopID();

        ImGui.SetCursorPos(rowEndCursorPos);
        ImGui.Dummy(Vector2.Zero);
        return action;
    }

    private DashboardGameRowAction DrawPendingGameRow(GameInstance game, bool selected, bool canInstall)
    {
        var installWidth = GetButtonWidth("Install", 110f);
        var detailsWidth = GetButtonWidth("Details", 110f);
        var actionSpacing = 8f;
        var actionsWidth = installWidth + actionSpacing + detailsWidth;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f);
        var min = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var buttonHeight = ImGui.GetFrameHeight();
        var rowHeight = MathF.Max(56f, MathF.Max((lineHeight * 2f) + 20f, buttonHeight + 20f));
        var size = new Vector2(MathF.Max(0f, ImGui.GetContentRegionAvail().X - 6f), rowHeight);

        if (size.X < 1f || size.Y < 1f)
        {
            ImGui.Dummy(new Vector2(1f, MathF.Max(1f, rowHeight)));
            return DashboardGameRowAction.None;
        }

        ImGui.Dummy(size);
        var rowEndCursorPos = ImGui.GetCursorPos();

        var selectWidth = MathF.Max(1f, size.X - actionsWidth - 24f);
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton($"GameSelect::{game.GamePath}", new Vector2(selectWidth, rowHeight));
        var hovered = ImGui.IsItemHovered();
        var selectClicked = ImGui.IsItemClicked();

        var emphasis = AnimateValue($"Row::PendingGame::{game.GamePath}", selected ? 1f : hovered ? 0.5f : 0f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var accent = InfoColor;
        var background = Vector4.Lerp(new Vector4(0.22f, 0.23f, 0.22f, 0.55f), new Vector4(0.29f, 0.35f, 0.22f, 0.97f), emphasis);
        var border = Vector4.Lerp(new Vector4(PanelBorderColor.X, PanelBorderColor.Y, PanelBorderColor.Z, 0.50f), new Vector4(accent.X, accent.Y, accent.Z, 0.82f), emphasis);
        var textColor = ImGui.ColorConvertFloat4ToU32(PrimaryTextColor);
        var detailColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(MutedTextColor, accent, 0.35f));
        var detail = string.IsNullOrWhiteSpace(game.AntiCheatProvider)
            ? "Not installed"
            : $"Not installed - {game.AntiCheatProvider}";

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), 2f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 2f, ImDrawFlags.None, 1f);

        if (emphasis > 0.02f)
        {
            drawList.AddLine(
                new Vector2(min.X + 8f, min.Y + 8f),
                new Vector2(min.X + 8f, max.Y - 8f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.85f * emphasis)),
                2f);
        }

        var contentHeight = (lineHeight * 2f) + 3f;
        var textStartY = min.Y + MathF.Max(8f, (rowHeight - contentHeight) * 0.5f);
        var contentLeft = min.X + 16f;
        drawList.AddText(new Vector2(contentLeft, textStartY), textColor, game.Name);
        drawList.AddText(new Vector2(contentLeft, textStartY + lineHeight + 3f), detailColor, detail);

        var buttonY = min.Y + MathF.Max(8f, (rowHeight - buttonHeight) * 0.5f);
        var installX = max.X - actionsWidth - 16f;
        DashboardGameRowAction action = selectClicked ? DashboardGameRowAction.Select : DashboardGameRowAction.None;

        ImGui.PushID(game.GamePath);
        ImGui.SetCursorScreenPos(new Vector2(installX, buttonY));
        ImGui.BeginDisabled(!canInstall);
        if (ImGui.Button("Install", new Vector2(installWidth, 0f)))
        {
            action = DashboardGameRowAction.Install;
        }
        ImGui.EndDisabled();

        ImGui.SetCursorScreenPos(new Vector2(installX + installWidth + actionSpacing, buttonY));
        if (ImGui.Button("Details", new Vector2(detailsWidth, 0f)))
        {
            action = DashboardGameRowAction.Details;
        }
        ImGui.PopID();

        ImGui.SetCursorPos(rowEndCursorPos);
        ImGui.Dummy(Vector2.Zero);
        return action;
    }

    private static float MeasureInstalledGamePillsHeight(GameInstance game, float maxWidth)
    {
        var cursorX = 0f;
        var cursorY = 0f;
        var lineHeight = 0f;

        MeasureInlinePillLayout(GetOptiScalerQuickPillText(game), maxWidth, ref cursorX, ref cursorY, ref lineHeight);
        MeasureInlinePillLayout(GetUpscalersFgQuickPillText(), maxWidth, ref cursorX, ref cursorY, ref lineHeight);
        MeasureInlinePillLayout(GetOptiPatcherQuickPillText(game), maxWidth, ref cursorX, ref cursorY, ref lineHeight);

        return lineHeight <= 0f ? 0f : cursorY + lineHeight;
    }

    private static InstalledGamePillLayouts DrawInstalledGamePills(ImDrawListPtr drawList, Vector2 origin, float maxWidth, GameInstance game)
    {
        var cursorX = 0f;
        var cursorY = 0f;
        var lineHeight = 0f;

        var versionText = GetOptiScalerQuickPillText(game);
        var componentsText = GetUpscalersFgQuickPillText();
        var optiPatcherText = GetOptiPatcherQuickPillText(game);

        var versionMin = DrawInlinePillLayout(drawList, origin, versionText, SuccessColor, maxWidth, ref cursorX, ref cursorY, ref lineHeight);
        var componentsMin = DrawInlinePillLayout(drawList, origin, componentsText, InfoColor, maxWidth, ref cursorX, ref cursorY, ref lineHeight);
        DrawInlinePillLayout(drawList, origin, optiPatcherText, game.IsOptiPatcherInstalled ? SuccessColor : PanelBorderColor, maxWidth, ref cursorX, ref cursorY, ref lineHeight);

        return new InstalledGamePillLayouts(
            new InlinePillLayout(versionMin, GetInlinePillSize(versionText)),
            new InlinePillLayout(componentsMin, GetInlinePillSize(componentsText)));
    }

    private static string GetOptiPatcherQuickPillText(GameInstance game)
    {
        return game.IsOptiPatcherInstalled
            ? "OptiPatcher installed"
            : "OptiPatcher off";
    }

    private static void MeasureInlinePillLayout(string text, float maxWidth, ref float cursorX, ref float cursorY, ref float lineHeight)
    {
        LayoutInlinePill(Vector2.Zero, GetInlinePillSize(text), maxWidth, ref cursorX, ref cursorY, ref lineHeight);
    }

    private static Vector2 DrawInlinePillLayout(
        ImDrawListPtr drawList,
        Vector2 origin,
        string text,
        Vector4 accent,
        float maxWidth,
        ref float cursorX,
        ref float cursorY,
        ref float lineHeight)
    {
        var pillMin = LayoutInlinePill(origin, GetInlinePillSize(text), maxWidth, ref cursorX, ref cursorY, ref lineHeight);
        DrawInlinePill(drawList, pillMin, text, accent);
        return pillMin;
    }

    private static Vector2 LayoutInlinePill(
        Vector2 origin,
        Vector2 size,
        float maxWidth,
        ref float cursorX,
        ref float cursorY,
        ref float lineHeight)
    {
        var availableWidth = Math.Max(1f, maxWidth);
        if (cursorX > 0f && cursorX + size.X > availableWidth)
        {
            cursorX = 0f;
            cursorY += lineHeight + 8f;
            lineHeight = 0f;
        }

        var min = new Vector2(origin.X + cursorX, origin.Y + cursorY);
        cursorX += size.X + 8f;
        lineHeight = MathF.Max(lineHeight, size.Y);
        return min;
    }

    private bool DrawSelectableRow(
        string id,
        string title,
        string detail,
        bool selected,
        Vector4 accent,
        string badge,
        bool centerText = false,
        bool persistentIndicator = false)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f);
        var min = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var hasDetail = !string.IsNullOrWhiteSpace(detail);
        var rowHeight = hasDetail
            ? MathF.Max(56f, (lineHeight * 2f) + 20f)
            : MathF.Max(34f, lineHeight + 14f);
        var size = new Vector2(Math.Max(0f, ImGui.GetContentRegionAvail().X - 6f), rowHeight);

        if (size.X < 1f || size.Y < 1f)
        {
            ImGui.Dummy(new Vector2(1f, MathF.Max(1f, rowHeight)));
            return false;
        }

        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        var emphasis = AnimateValue($"Row::{id}", selected ? 1f : hovered ? 0.5f : 0f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var background = Vector4.Lerp(new Vector4(0.22f, 0.23f, 0.22f, 0.55f), new Vector4(0.29f, 0.35f, 0.22f, 0.97f), emphasis);
        var border = Vector4.Lerp(new Vector4(PanelBorderColor.X, PanelBorderColor.Y, PanelBorderColor.Z, 0.50f), new Vector4(accent.X, accent.Y, accent.Z, 0.82f), emphasis);
        var textColor = ImGui.ColorConvertFloat4ToU32(PrimaryTextColor);
        var detailColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(MutedTextColor, accent, 0.35f));
        var badgeColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(new Vector4(accent.X, accent.Y, accent.Z, 0.75f), accent, emphasis));
        var titleSize = ImGui.CalcTextSize(title);
        var detailSize = hasDetail ? ImGui.CalcTextSize(detail) : Vector2.Zero;
        var hasBadge = !string.IsNullOrWhiteSpace(badge);
        var badgeSize = hasBadge ? ImGui.CalcTextSize(badge) : Vector2.Zero;

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), 2f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 2f, ImDrawFlags.None, 1f);

        if (persistentIndicator)
        {
            if (selected)
            {
                drawList.AddLine(
                    new Vector2(min.X + 8f, min.Y + 8f),
                    new Vector2(min.X + 8f, max.Y - 8f),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.85f)),
                    2f);
            }
            else
            {
                var indicatorWidth = 2f;
                var indicatorHeight = 14f;
                var indicatorCenter = new Vector2(min.X + 8f, min.Y + (rowHeight * 0.5f));
                var indicatorHalfSize = new Vector2(indicatorWidth * 0.5f, indicatorHeight * 0.5f);
                var indicatorMin = indicatorCenter - indicatorHalfSize;
                var indicatorMax = indicatorCenter + indicatorHalfSize;

                drawList.AddRect(
                    indicatorMin,
                    indicatorMax,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.34f)),
                    indicatorWidth * 0.5f,
                    ImDrawFlags.None,
                    1f);
            }
        }
        else if (emphasis > 0.02f)
        {
            drawList.AddLine(
                new Vector2(min.X + 8f, min.Y + 8f),
                new Vector2(min.X + 8f, max.Y - 8f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.85f * emphasis)),
                2f);
        }

        var contentHeight = hasDetail ? (lineHeight * 2f) + 3f : lineHeight;
        var textStartY = min.Y + MathF.Max(8f, (rowHeight - contentHeight) * 0.5f);
        var textBlockWidth = MathF.Max(titleSize.X, detailSize.X);
        var contentLeft = min.X + 16f;
        var contentRight = hasBadge ? max.X - badgeSize.X - 24f : max.X - 16f;
        var availableWidth = MathF.Max(0f, contentRight - contentLeft);
        var textX = centerText
            ? contentLeft + MathF.Max(0f, (availableWidth - textBlockWidth) * 0.5f)
            : contentLeft;
        var titleX = centerText ? textX + MathF.Max(0f, (textBlockWidth - titleSize.X) * 0.5f) : textX;

        drawList.AddText(new Vector2(titleX, textStartY), textColor, title);

        if (hasDetail)
        {
            var detailX = centerText
                ? textX + MathF.Max(0f, (textBlockWidth - detailSize.X) * 0.5f)
                : textX;
            drawList.AddText(new Vector2(detailX, textStartY + lineHeight + 3f), detailColor, detail);
        }

        if (hasBadge)
        {
            var badgeX = max.X - badgeSize.X - 14f;
            var badgeY = min.Y + MathF.Max(8f, (rowHeight - badgeSize.Y) * 0.5f);
            drawList.AddText(new Vector2(badgeX, badgeY), badgeColor, badge);
        }

        return clicked;
    }

    private static float GetButtonWidth(string label, float minWidth)
    {
        var paddedTextWidth = ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f);
        return MathF.Max(minWidth, MathF.Ceiling(paddedTextWidth));
    }

    private static bool DrawCenteredButton(string id, string label, Vector2 size)
    {
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleVar();
        ImGui.PopID();
        return clicked;
    }

    private static void ContinueOnSameLineIfFits(float nextItemWidth)
    {
        var nextItemRight = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextItemWidth;
        var contentRight = ImGui.GetWindowPos().X + ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        if (nextItemRight <= contentRight)
        {
            ImGui.SameLine();
        }
    }

    private static void RenderKeyValue(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextWrapped(value);
        ImGui.Spacing();
    }

    private static void RenderCompactKeyValue(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextWrapped(value);
    }

    private static void RenderCallout(string title, string message, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelRaisedBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.60f));
        ImGui.BeginChild($"Callout::{title}", new Vector2(0f, 94f), ImGuiChildFlags.Borders, PanelWindowFlags);
        ImGui.TextColored(accent, title);
        ImGui.TextWrapped(message);
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void TryOpenExternalUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SetNotification($"Could not open URL: {ex.Message}", NotificationKind.Error);
        }
    }

    private static void DrawVersionCombo(string label, IList<OptiScalerVersion> versions, ref OptiScalerVersion? selectedVersion)
    {
        var preview = selectedVersion?.TagName ?? "Select a version";

        if (!ImGui.BeginCombo(label, preview))
        {
            return;
        }

        foreach (var version in versions)
        {
            var isSelected = ReferenceEquals(selectedVersion, version) ||
                string.Equals(selectedVersion?.TagName, version.TagName, StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable(version.TagName, isSelected))
            {
                selectedVersion = version;
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static void DrawStringCombo(string label, IReadOnlyList<string> values, ref string selectedValue)
    {
        if (!ImGui.BeginCombo(label, selectedValue))
        {
            return;
        }

        foreach (var value in values)
        {
            var isSelected = string.Equals(selectedValue, value, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(value, isSelected))
            {
                selectedValue = value;
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static void DrawStringCombo(string label, IReadOnlyList<string> values, ref int selectedIndex)
    {
        if (values.Count == 0)
        {
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, values.Count - 1);
        var preview = values[selectedIndex];

        if (!ImGui.BeginCombo(label, preview))
        {
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            var isSelected = selectedIndex == i;
            if (ImGui.Selectable(values[i], isSelected))
            {
                selectedIndex = i;
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static bool RenderConfigChoiceCombo(string label, GameConfigViewModel config, string section, string key, IReadOnlyList<ConfigChoice> choices)
    {
        var currentValue = config.GetSetting(section, key);
        var preview = currentValue;
        for (var index = 0; index < choices.Count; index++)
        {
            if (!choices[index].Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            preview = choices[index].Label;
            break;
        }

        var changed = false;
        ImGui.TextDisabled(label);
        ImGui.SetNextItemWidth(GetConfigControlWidth());
        if (!ImGui.BeginCombo($"##{section}.{key}", preview))
        {
            return false;
        }

        foreach (var choice in choices)
        {
            var isSelected = choice.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(choice.Label, isSelected))
            {
                config.SetSetting(section, key, choice.Value);
                currentValue = choice.Value;
                changed = true;
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool RenderConfigFloatSlider(
        string label,
        GameConfigViewModel config,
        string section,
        string key,
        float min,
        float max,
        string displayFormat,
        string storageFormat)
    {
        var rawValue = config.GetSetting(section, key);
        var value = min;
        if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value))
        {
            value = min;
        }

        var changed = false;
        ImGui.PushID($"{section}.{key}");
        ImGui.TextDisabled(label);

        ImGui.SetNextItemWidth(GetConfigControlWidth());
        if (ImGui.SliderFloat("##Value", ref value, min, max, displayFormat))
        {
            config.SetSetting(section, key, value.ToString(storageFormat, CultureInfo.InvariantCulture));
            changed = true;
        }

        ImGui.PopID();
        return changed;
    }

    private static void EnsureConfigChoiceValue(GameConfigViewModel config, string section, string key, IReadOnlyList<ConfigChoice> choices, string defaultValue)
    {
        var currentValue = config.GetSetting(section, key);
        var hasMatch = false;
        foreach (var choice in choices)
        {
            if (!choice.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasMatch = true;
            break;
        }

        if (!hasMatch)
        {
            config.SetSetting(section, key, defaultValue);
        }
    }

    private static void EnsureConfigFloatValue(GameConfigViewModel config, string section, string key, float defaultValue, string storageFormat)
    {
        var rawValue = config.GetSetting(section, key);
        if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value))
        {
            config.SetSetting(section, key, defaultValue.ToString(storageFormat, CultureInfo.InvariantCulture));
        }
    }

    private static void EnsureConfigShortcutValue(GameConfigViewModel config, string section, string key, string defaultValue)
    {
        var rawValue = config.GetSetting(section, key);
        if (!TryParseShortcutKeyConfigValue(rawValue, out _))
        {
            config.SetSetting(section, key, defaultValue);
        }
    }

    private static bool TryDetectShortcutCapture(out int virtualKey)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            virtualKey = -1;
            return true;
        }

        foreach (var key in ShortcutCaptureKeys)
        {
            if (!ImGui.IsKeyPressed(key.ImGuiKey))
            {
                continue;
            }

            virtualKey = key.VirtualKey;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static bool TryParseShortcutKeyInput(string input, out string configValue, out string normalizedDisplay)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            configValue = string.Empty;
            normalizedDisplay = string.Empty;
            return false;
        }

        if (TryParseShortcutKeyConfigValue(trimmed, out var rawVirtualKey))
        {
            configValue = FormatShortcutKeyConfigValue(rawVirtualKey);
            normalizedDisplay = FormatShortcutKeyDisplay(rawVirtualKey);
            return true;
        }

        if (trimmed.Length == 1 && TryResolveShortcutKeyCharacter(trimmed[0], out var characterVirtualKey))
        {
            configValue = FormatShortcutKeyConfigValue(characterVirtualKey);
            normalizedDisplay = FormatShortcutKeyDisplay(characterVirtualKey);
            return true;
        }

        var normalizedName = NormalizeShortcutKeyName(trimmed);
        if (TryResolveShortcutKeyName(normalizedName, out var namedVirtualKey))
        {
            configValue = FormatShortcutKeyConfigValue(namedVirtualKey);
            normalizedDisplay = FormatShortcutKeyDisplay(namedVirtualKey);
            return true;
        }

        configValue = string.Empty;
        normalizedDisplay = trimmed;
        return false;
    }

    private static bool TryParseShortcutKeyConfigValue(string rawValue, out int virtualKey)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Equals("-1", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = -1;
            return true;
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey) &&
            virtualKey is >= 0 and <= 0xFF)
        {
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out virtualKey) &&
            virtualKey is >= 0 and <= 0xFF)
        {
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static string FormatShortcutKeyConfigValue(int virtualKey)
    {
        return virtualKey == -1
            ? "-1"
            : $"0x{virtualKey:X2}";
    }

    private static string FormatShortcutKeyDisplay(string rawValue)
    {
        return TryParseShortcutKeyConfigValue(rawValue, out var virtualKey)
            ? FormatShortcutKeyDisplay(virtualKey)
            : rawValue;
    }

    private static string FormatShortcutKeyDisplay(int virtualKey)
    {
        if (virtualKey == -1)
        {
            return "None";
        }

        if (virtualKey is >= 'A' and <= 'Z')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= Win32Native.VK_F1 and <= 0x87)
        {
            return $"F{virtualKey - Win32Native.VK_F1 + 1}";
        }

        if (virtualKey is >= Win32Native.VK_NUMPAD0 and <= Win32Native.VK_NUMPAD9)
        {
            return $"Numpad {virtualKey - Win32Native.VK_NUMPAD0}";
        }

        return virtualKey switch
        {
            Win32Native.VK_INSERT => "Insert",
            Win32Native.VK_HOME => "Home",
            Win32Native.VK_END => "End",
            Win32Native.VK_PRIOR => "Page Up",
            Win32Native.VK_NEXT => "Page Down",
            Win32Native.VK_BACK => "Backspace",
            Win32Native.VK_DELETE => "Delete",
            Win32Native.VK_RETURN => "Enter",
            Win32Native.VK_ESCAPE => "Escape",
            Win32Native.VK_TAB => "Tab",
            Win32Native.VK_SPACE => "Space",
            Win32Native.VK_LEFT => "Left Arrow",
            Win32Native.VK_RIGHT => "Right Arrow",
            Win32Native.VK_UP => "Up Arrow",
            Win32Native.VK_DOWN => "Down Arrow",
            Win32Native.VK_SNAPSHOT => "Print Screen",
            Win32Native.VK_PAUSE => "Pause",
            Win32Native.VK_CAPITAL => "Caps Lock",
            Win32Native.VK_SCROLL => "Scroll Lock",
            Win32Native.VK_NUMLOCK => "Num Lock",
            Win32Native.VK_APPS => "Menu",
            Win32Native.VK_ADD => "Numpad +",
            Win32Native.VK_SUBTRACT => "Numpad -",
            Win32Native.VK_MULTIPLY => "Numpad *",
            Win32Native.VK_DIVIDE => "Numpad /",
            Win32Native.VK_DECIMAL => "Numpad .",
            Win32Native.VK_OEM_3 => "`",
            Win32Native.VK_OEM_MINUS => "-",
            Win32Native.VK_OEM_PLUS => "=",
            Win32Native.VK_OEM_4 => "[",
            Win32Native.VK_OEM_6 => "]",
            Win32Native.VK_OEM_5 => "\\",
            Win32Native.VK_OEM_1 => ";",
            Win32Native.VK_OEM_7 => "'",
            Win32Native.VK_OEM_COMMA => ",",
            Win32Native.VK_OEM_PERIOD => ".",
            Win32Native.VK_OEM_2 => "/",
            _ => FormatShortcutKeyConfigValue(virtualKey),
        };
    }

    private static string NormalizeShortcutKeyName(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..length]);
    }

    private static bool TryResolveShortcutKeyName(string normalizedName, out int virtualKey)
    {
        if (normalizedName.Length > 1 &&
            normalizedName[0] == 'F' &&
            int.TryParse(normalizedName[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = Win32Native.VK_F1 + functionKey - 1;
            return true;
        }

        if (normalizedName.StartsWith("NUMPAD", StringComparison.Ordinal) &&
            int.TryParse(normalizedName[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var numpadKey) &&
            numpadKey is >= 0 and <= 9)
        {
            virtualKey = Win32Native.VK_NUMPAD0 + numpadKey;
            return true;
        }

        virtualKey = normalizedName switch
        {
            "NONE" or "DISABLED" or "DISABLE" or "OFF" => -1,
            "INSERT" or "INS" => Win32Native.VK_INSERT,
            "HOME" => Win32Native.VK_HOME,
            "END" => Win32Native.VK_END,
            "PAGEUP" or "PGUP" or "PRIOR" => Win32Native.VK_PRIOR,
            "PAGEDOWN" or "PGDN" or "NEXT" => Win32Native.VK_NEXT,
            "BACKSPACE" or "BACK" or "BKSP" => Win32Native.VK_BACK,
            "DELETE" or "DEL" => Win32Native.VK_DELETE,
            "ENTER" or "RETURN" => Win32Native.VK_RETURN,
            "ESC" or "ESCAPE" => Win32Native.VK_ESCAPE,
            "TAB" => Win32Native.VK_TAB,
            "SPACE" or "SPACEBAR" => Win32Native.VK_SPACE,
            "LEFT" or "LEFTARROW" => Win32Native.VK_LEFT,
            "RIGHT" or "RIGHTARROW" => Win32Native.VK_RIGHT,
            "UP" or "UPARROW" => Win32Native.VK_UP,
            "DOWN" or "DOWNARROW" => Win32Native.VK_DOWN,
            "PRINTSCREEN" or "PRTSC" or "PRTSCN" or "SNAPSHOT" => Win32Native.VK_SNAPSHOT,
            "PAUSE" => Win32Native.VK_PAUSE,
            "CAPSLOCK" => Win32Native.VK_CAPITAL,
            "SCROLLLOCK" => Win32Native.VK_SCROLL,
            "NUMLOCK" => Win32Native.VK_NUMLOCK,
            "MENU" or "APPS" or "CONTEXTMENU" => Win32Native.VK_APPS,
            _ => 0,
        };

        return virtualKey != 0 || normalizedName is "NONE" or "DISABLED" or "DISABLE" or "OFF";
    }

    private static bool TryResolveShortcutKeyCharacter(char value, out int virtualKey)
    {
        var upper = char.ToUpperInvariant(value);
        if (upper is >= 'A' and <= 'Z' || upper is >= '0' and <= '9')
        {
            virtualKey = upper;
            return true;
        }

        virtualKey = value switch
        {
            ' ' => Win32Native.VK_SPACE,
            '`' or '~' => Win32Native.VK_OEM_3,
            '-' or '_' => Win32Native.VK_OEM_MINUS,
            '=' or '+' => Win32Native.VK_OEM_PLUS,
            '[' or '{' => Win32Native.VK_OEM_4,
            ']' or '}' => Win32Native.VK_OEM_6,
            '\\' or '|' => Win32Native.VK_OEM_5,
            ';' or ':' => Win32Native.VK_OEM_1,
            '\'' or '"' => Win32Native.VK_OEM_7,
            ',' or '<' => Win32Native.VK_OEM_COMMA,
            '.' or '>' => Win32Native.VK_OEM_PERIOD,
            '/' or '?' => Win32Native.VK_OEM_2,
            _ => 0,
        };

        return virtualKey != 0;
    }

    private static string GetDefaultDxgiConfigValue()
    {
        if (!string.IsNullOrWhiteSpace(_defaultDxgiConfigValue))
        {
            return _defaultDxgiConfigValue;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? string.Empty;
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        _defaultDxgiConfigValue = "false";
                        return _defaultDxgiConfigValue;
                    }
                }
            }
        }
        catch
        {
        }

        _defaultDxgiConfigValue = "true";
        return _defaultDxgiConfigValue;
    }

    private static float GetConfigControlWidth(float maxWidth = 360f)
    {
        return MathF.Min(maxWidth, MathF.Max(220f, ImGui.GetContentRegionAvail().X));
    }

    private static string TrimText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        var trimmed = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length > 180 ? trimmed[..177] + "..." : trimmed;
    }

    private static void TextMuted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, MutedTextColor);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static void ApplyTheme()
    {
        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        var style = ImGui.GetStyle();
        style.WindowRounding = 2f;
        style.ChildRounding = 2f;
        style.FrameRounding = 2f;
        style.PopupRounding = 2f;
        style.GrabRounding = 2f;
        style.ScrollbarRounding = 2f;
        style.TabRounding = 2f;
        style.FramePadding = new Vector2(9f, 6f);
        style.ItemSpacing = new Vector2(10f, 9f);
        style.ItemInnerSpacing = new Vector2(8f, 5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.AntiAliasedFill = true;
        style.AntiAliasedLines = true;
        style.AntiAliasedLinesUseTex = true;

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg] = WindowBackgroundColor;
        colors[(int)ImGuiCol.ChildBg] = PanelBackgroundColor;
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.23f, 0.24f, 0.25f, 0.99f);
        colors[(int)ImGuiCol.Border] = PanelBorderColor;
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.24f, 0.26f, 0.27f, 1f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.30f, 0.35f, 0.24f, 1f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.36f, 0.43f, 0.26f, 1f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.20f, 0.21f, 0.22f, 1f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.20f, 0.21f, 0.22f, 1f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.19f, 0.20f, 0.21f, 1f);
        colors[(int)ImGuiCol.Button] = new Vector4(0.34f, 0.45f, 0.24f, 1f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.42f, 0.55f, 0.29f, 1f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.49f, 0.63f, 0.34f, 1f);
        colors[(int)ImGuiCol.Header] = new Vector4(0.29f, 0.38f, 0.22f, 1f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.37f, 0.49f, 0.28f, 1f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.45f, 0.58f, 0.33f, 1f);
        colors[(int)ImGuiCol.NavCursor] = new Vector4(0.63f, 0.82f, 0.42f, 1f);
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(0.55f, 0.74f, 0.36f, 1f);
        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.74f, 0.89f, 0.55f, 1f);
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.55f, 0.74f, 0.36f, 1f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.63f, 0.82f, 0.42f, 1f);
        colors[(int)ImGuiCol.Separator] = PanelBorderColor;
        colors[(int)ImGuiCol.Text] = PrimaryTextColor;
        colors[(int)ImGuiCol.TextDisabled] = MutedTextColor;
    }

    private sealed class NativeWindowHost : IDisposable
    {
        private readonly nint _hInstance;
        private readonly Action _renderContent;
        private readonly Func<bool>? _canClose;
        private int _minClientWidth;
        private int _minClientHeight;
        private readonly string _windowClassName = $"OptinstallerNativeWindowClass_{Guid.NewGuid():N}";
        private readonly Win32Native.WndProcDelegate _windowProcedureDelegate;

        private GCHandle _selfHandle;
        private nint _hwnd;
        private Dx11ImGuiRenderer? _renderer;
        private bool _classRegistered;
        private bool _disposed;
        private bool _isMinimized;
        private bool _isInSizeMove;
        private bool _isRenderingFrame;
        private int _windowWidth;
        private int _windowHeight;

        public NativeWindowHost(nint hInstance, nint ownerHwnd, string title, int width, int height, int minClientWidth, int minClientHeight, Action renderContent, Func<bool>? canClose)
        {
            _hInstance = hInstance;
            _renderContent = renderContent;
            _canClose = canClose;
            _windowWidth = Math.Max(1, width);
            _windowHeight = Math.Max(1, height);
            _minClientWidth = Math.Max(1, minClientWidth);
            _minClientHeight = Math.Max(1, minClientHeight);
            _windowProcedureDelegate = WindowProcedure;
            _selfHandle = GCHandle.Alloc(this);

            try
            {
                RegisterWindowClass();
                CreateWindow(ownerHwnd, title);
                _renderer = new Dx11ImGuiRenderer(_hwnd, _windowWidth, _windowHeight, ConfigureImGuiIo, LoadFonts, ApplyTheme);

                Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOW);
                Win32Native.UpdateWindow(_hwnd);
            }
            catch
            {
                PreserveImGuiContext(Dispose);
                throw;
            }
        }

        public bool IsClosed { get; private set; }

        public bool IsInSizeMove => _isInSizeMove;

        public nint WindowHandle => _hwnd;

        public void SetMinClientSize(int width, int height)
        {
            _minClientWidth = Math.Max(1, width);
            _minClientHeight = Math.Max(1, height);
        }

        public bool RenderFrame(float delta, bool enableVsync = true)
        {
            if (IsClosed || _isMinimized || _renderer == null || _isRenderingFrame)
            {
                return false;
            }

            _isRenderingFrame = true;
            try
            {
                if (!_renderer.BeginFrame(delta, _windowWidth, _windowHeight))
                {
                    return false;
                }

                _renderContent();
                _renderer.Render(WindowBackgroundColor, enableVsync);
                return true;
            }
            finally
            {
                _isRenderingFrame = false;
            }
        }

        private void ResizeRenderer(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            _windowWidth = width;
            _windowHeight = height;
            _renderer?.Resize(width, height);
        }

        public void SetTitle(string title)
        {
            if (_hwnd != 0)
            {
                PreserveImGuiContext(() => Win32Native.SetWindowText(_hwnd, title));
            }
        }

        public void Focus()
        {
            if (_hwnd == 0 || IsClosed)
            {
                return;
            }

            PreserveImGuiContext(() =>
            {
                Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOW);
                Win32Native.SetForegroundWindow(_hwnd);
            });
        }

        public void RequestClose()
        {
            if (_hwnd != 0 && !IsClosed)
            {
                Win32Native.PostMessage(_hwnd, Win32Native.WM_CLOSE, 0, IntPtr.Zero);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_hwnd != 0)
            {
                Win32Native.DestroyWindow(_hwnd);
                _hwnd = 0;
            }

            _renderer?.Dispose();
            _renderer = null;

            if (_classRegistered)
            {
                Win32Native.UnregisterClass(_windowClassName, _hInstance);
                _classRegistered = false;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        private void RegisterWindowClass()
        {
            var (largeIcon, smallIcon) = GetAppIcons();
            var windowClass = new Win32Native.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEXW>(),
                style = Win32Native.CS_HREDRAW | Win32Native.CS_VREDRAW | Win32Native.CS_OWNDC,
                lpfnWndProc = _windowProcedureDelegate,
                hInstance = _hInstance,
                hIcon = largeIcon,
                hCursor = Win32Native.LoadCursor(IntPtr.Zero, (nint)Win32Native.IDC_ARROW),
                hbrBackground = Win32Native.DarkBackgroundBrush,
                lpszClassName = _windowClassName,
                hIconSm = smallIcon,
            };

            if (Win32Native.RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
            }

            _classRegistered = true;
        }

        private void CreateWindow(nint ownerHwnd, string title)
        {
            var windowRect = new Win32Native.RECT
            {
                Left = 0,
                Top = 0,
                Right = _windowWidth,
                Bottom = _windowHeight,
            };

            Win32Native.AdjustWindowRectEx(ref windowRect, Win32Native.WS_OVERLAPPEDWINDOW, false, 0);

            _hwnd = Win32Native.CreateWindowEx(
                0,
                _windowClassName,
                title,
                Win32Native.WS_OVERLAPPEDWINDOW,
                Win32Native.CW_USEDEFAULT,
                Win32Native.CW_USEDEFAULT,
                windowRect.Right - windowRect.Left,
                windowRect.Bottom - windowRect.Top,
                ownerHwnd,
                IntPtr.Zero,
                _hInstance,
                GCHandle.ToIntPtr(_selfHandle));

            if (_hwnd == 0)
            {
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            }

            Win32Native.ApplyDarkWindowTheme(_hwnd);
            ApplyWindowIcons(_hwnd);

            if (Win32Native.GetClientRect(_hwnd, out var clientRect))
            {
                _windowWidth = Math.Max(1, clientRect.Right - clientRect.Left);
                _windowHeight = Math.Max(1, clientRect.Bottom - clientRect.Top);
            }
        }

        private static nint WindowProcedure(nint hwnd, uint msg, nuint wParam, nint lParam)
        {
            if (msg == Win32Native.WM_NCCREATE)
            {
                var createStruct = Marshal.PtrToStructure<Win32Native.CREATESTRUCTW>(lParam);
                Win32Native.SetWindowLongPtr(hwnd, Win32Native.GWLP_USERDATA, createStruct.lpCreateParams);
            }

            var userData = Win32Native.GetWindowLongPtr(hwnd, Win32Native.GWLP_USERDATA);
            if (userData != IntPtr.Zero)
            {
                var handle = GCHandle.FromIntPtr(userData);
                if (handle.Target is NativeWindowHost window)
                {
                    return window.HandleWindowMessage(hwnd, msg, wParam, lParam);
                }
            }

            return msg == Win32Native.WM_NCCREATE ? 1 : Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private nint HandleWindowMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
        {
            if (_renderer?.HandleMessage(msg, wParam, lParam) == true)
            {
                return 0;
            }

            switch (msg)
            {
                case Win32Native.WM_ERASEBKGND:
                    return _isInSizeMove ? Win32Native.DefWindowProc(hwnd, msg, wParam, lParam) : 1;

                case Win32Native.WM_SYSCOMMAND when ((uint)wParam & 0xFFF0) == Win32Native.SC_KEYMENU:
                    return 0;

                case Win32Native.WM_GETMINMAXINFO:
                    var minMaxInfo = Marshal.PtrToStructure<Win32Native.MINMAXINFO>(lParam);
                    var minWindowRect = new Win32Native.RECT
                    {
                        Left = 0,
                        Top = 0,
                        Right = _minClientWidth,
                        Bottom = _minClientHeight,
                    };

                    Win32Native.AdjustWindowRectEx(ref minWindowRect, Win32Native.WS_OVERLAPPEDWINDOW, false, 0);
                    minMaxInfo.ptMinTrackSize.X = minWindowRect.Right - minWindowRect.Left;
                    minMaxInfo.ptMinTrackSize.Y = minWindowRect.Bottom - minWindowRect.Top;
                    Marshal.StructureToPtr(minMaxInfo, lParam, false);
                    return 0;

                case Win32Native.WM_ENTERSIZEMOVE:
                    _isInSizeMove = true;
                    return 0;

                case Win32Native.WM_SIZING:
                    Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                    if (!_isRenderingFrame)
                    {
                        Win32Native.UpdateWindow(hwnd);
                    }
                    break;

                case Win32Native.WM_EXITSIZEMOVE:
                    _isInSizeMove = false;
                    Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                    Win32Native.UpdateWindow(hwnd);
                    return 0;

                case Win32Native.WM_SIZE:
                    if ((uint)wParam == Win32Native.SIZE_MINIMIZED)
                    {
                        _isMinimized = true;
                        return 0;
                    }

                    _isMinimized = false;
                    var width = Win32Native.GetXFromLParam(lParam);
                    var height = Win32Native.GetYFromLParam(lParam);
                    if (width > 0 && height > 0)
                    {
                        ResizeRenderer(width, height);
                        Win32Native.InvalidateRect(hwnd, IntPtr.Zero, false);
                        if (_isInSizeMove)
                        {
                            Win32Native.UpdateWindow(hwnd);
                        }
                    }
                    return 0;

                case Win32Native.WM_PAINT:
                    var paint = Win32Native.BeginPaint(hwnd, out var paintStruct);
                    if (paint != IntPtr.Zero)
                    {
                        if (!_isMinimized)
                        {
                            RenderFrame(1f / 60f);
                        }

                        Win32Native.EndPaint(hwnd, ref paintStruct);
                    }
                    return 0;

                case Win32Native.WM_CLOSE:
                    if (_canClose != null && !_canClose())
                    {
                        return 0;
                    }

                    IsClosed = true;
                    if (_isRenderingFrame)
                    {
                        return 0;
                    }

                    if (_hwnd != 0)
                    {
                        var handleToDestroy = _hwnd;
                        _hwnd = 0;
                        Win32Native.DestroyWindow(handleToDestroy);
                    }
                    return 0;

                case Win32Native.WM_DESTROY:
                    return 0;
            }

            return Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private sealed record MetricCard(string Label, string Value, string Detail, Vector4 Accent);

    private sealed record ConfirmationDialogState(
        string PopupId,
        string Title,
        string Message,
        string ConfirmLabel,
        Func<Task> ConfirmAction,
        string? SuccessMessage,
        string? FailureMessage);

    private enum ConfirmationHost
    {
        MainWindow,
        GameDetailsWindow,
        UpdateWindow,
        InstallationWindow,
    }

    private enum DashboardGameRowAction
    {
        None,
        Select,
        Details,
        Install,
        Uninstall,
        UpdateVersion,
        OpenComponentDlls,
    }

    private sealed class UpdateDialogState
    {
        public required GameInstance Game { get; init; }

        public OptiScalerVersion? SelectedVersion { get; set; }
    }

    private sealed class ComponentDllDialogState
    {
        public required GameInstance Game { get; init; }
    }

    private sealed class ConfigDialogState
    {
        public required GameInstance Game { get; init; }

        public required GameConfigViewModel ViewModel { get; init; }
    }

    private sealed class InstallationDialogState
    {
        public required GameInstance Game { get; init; }

        public required InstallationWizardViewModel ViewModel { get; init; }
    }

    private sealed class GameDetailsDialogState
    {
        public required GameInstance Game { get; init; }

        public GameConfigViewModel? ConfigViewModel { get; set; }

        public bool IsCapturingShortcutKey { get; set; }

        public string ShortcutKeyInput { get; set; } = string.Empty;

        public string? ShortcutKeyConfigValue { get; set; }

        public string? ShortcutKeyErrorMessage { get; set; }
    }

    private sealed record ConfigChoice(string Value, string Label);

    private sealed record ShortcutCaptureKey(ImGuiKey ImGuiKey, int VirtualKey);

    private sealed record ComponentDllEntry(string Label, string FileName, string Version, bool IsDetected);

    private sealed record InlinePillLayout(Vector2 Min, Vector2 Size);

    private sealed record InstalledGamePillLayouts(InlinePillLayout VersionPill, InlinePillLayout ComponentsPill);

    private enum AppPage
    {
        Dashboard,
        Versions,
        Settings,
    }

    private enum NotificationKind
    {
        Info,
        Success,
        Warning,
        Error,
    }
}
