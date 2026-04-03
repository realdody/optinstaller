using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Optinstaller.Models;
using Optinstaller.Platform;
using Optinstaller.ViewModels;

namespace Optinstaller.UI;

public sealed class OptinstallerImGuiApp : IDisposable
{
    private const int MinMainClientWidth = 1100;
    private const int MinMainClientHeight = 720;
    private const ImGuiWindowFlags PanelWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
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
    private static readonly Win32Native.WndProcDelegate WindowProcedureDelegate = WindowProcedure;

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
    private UpdateDialogState? _updateDialog;
    private ConfigDialogState? _configDialog;
    private InstallationDialogState? _installationDialog;
    private GameDetailsDialogState? _gameDetailsDialog;

    private NativeWindowHost? _confirmationWindow;
    private NativeWindowHost? _updateWindow;
    private NativeWindowHost? _configWindow;
    private NativeWindowHost? _installationWindow;
    private NativeWindowHost? _gameDetailsWindow;

    public OptinstallerImGuiApp(UiSynchronizationContext syncContext)
    {
        _syncContext = syncContext;
        _selfHandle = GCHandle.Alloc(this);
        _hInstance = Win32Native.GetModuleHandle(null);

        RegisterWindowClass();
        CreateWindow();
        _renderer = new Dx11ImGuiRenderer(_hwnd, _windowWidth, _windowHeight, ConfigureImGuiIo, LoadFonts, ApplyTheme);
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
            renderedAnyWindow |= RenderNativeWindow(_confirmationWindow, delta);
            renderedAnyWindow |= RenderNativeWindow(_updateWindow, delta);
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
            _renderer.BeginFrame(delta, _windowWidth, _windowHeight);
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
        var windowClass = new Win32Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEXW>(),
            style = Win32Native.CS_HREDRAW | Win32Native.CS_VREDRAW | Win32Native.CS_OWNDC,
            lpfnWndProc = WindowProcedureDelegate,
            hInstance = _hInstance,
            hCursor = Win32Native.LoadCursor(IntPtr.Zero, (nint)Win32Native.IDC_ARROW),
            hbrBackground = Win32Native.DarkBackgroundBrush,
            lpszClassName = _windowClassName,
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
        CleanupClosedNativeWindow(ref _gameDetailsWindow, () => _gameDetailsDialog = null);
        CleanupClosedNativeWindow(ref _confirmationWindow, () => _confirmation = null);
        CleanupClosedNativeWindow(ref _updateWindow, () => _updateDialog = null);
        CleanupClosedNativeWindow(ref _configWindow, () => _configDialog = null);
        CleanupClosedNativeWindow(ref _installationWindow, () => FinalizeInstallationDialog(showSuccessMessage: false));
    }

    private void DisposeNativeWindows()
    {
        DisposeNativeWindow(ref _gameDetailsWindow);
        DisposeNativeWindow(ref _confirmationWindow);
        DisposeNativeWindow(ref _updateWindow);
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
        if (DrawSelectableRow($"Nav::{page}", title, subtitle, selected, InfoColor, string.Empty, centerText: false))
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
            NotificationKind.Error => (ErrorColor, new Vector4(0.24f, 0.14f, 0.13f, 1f), "Error"),
            _ => (InfoColor, new Vector4(0.18f, 0.21f, 0.16f, 1f), "Info"),
        };

        ImGui.PushStyleColor(ImGuiCol.ChildBg, background);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.55f));

        ImGui.BeginChild("NotificationBanner", new Vector2(0f, 58f), PaddedPanelChildFlags, PanelWindowFlags);
        if (ImGui.BeginTable("NotificationBannerTable", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dismiss", ImGuiTableColumnFlags.WidthFixed, 90f);

            ImGui.TableNextColumn();
            ImGui.TextColored(accent, label);
            ImGui.SameLine();
            ImGui.TextWrapped(_notificationMessage);

            ImGui.TableNextColumn();
            if (ImGui.Button("Dismiss", new Vector2(-1f, 0f)))
            {
                _notificationMessage = null;
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

    private void RenderDashboard()
    {
        var dashboard = _mainViewModel.Dashboard;
        var allGames = new List<GameInstance>(dashboard.Games);
        var filteredGames = GetFilteredGames(allGames, _dashboardSearchQuery);
        var selectedGame = ResolveSelectedGame(dashboard, filteredGames);
        var installedCount = CountInstalledGames();
        var pendingCount = allGames.Count - installedCount;

        RenderPageHeader("Dashboard", "Click a game to open details and manage OptiScaler.");
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
        ImGui.BeginChild("DashboardList", new Vector2(0f, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        RenderSectionHeader($"Games ({filteredGames.Count})");
        TextMuted("Click any tracked game to open its details and actions in a separate window.");
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
        foreach (var game in filteredGames)
        {
            var isSelected = selectedGame != null && selectedGame.GamePath.Equals(game.GamePath, StringComparison.OrdinalIgnoreCase);
            var detailText = game.IsInstalled
                ? game.CurrentVersion
                : "Not installed";
            var accent = game.IsInstalled ? SuccessColor : InfoColor;
            var badge = game.IsInstalled ? "Installed" : "Ready";

            if (DrawSelectableRow(
                $"Game::{game.GamePath}",
                game.Name,
                detailText,
                isSelected,
                accent,
                badge,
                centerText: false))
            {
                _selectedGamePath = game.GamePath;
                dashboard.SelectedGame = game;
                selectedGame = game;
                OpenGameDetailsDialog(game);
            }

        }
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void RenderGameDetails(DashboardViewModel dashboard, GameInstance? game)
    {
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
        TextMuted(game.GamePath);
        ImGui.Spacing();
        RenderSectionHeader("Install Defaults");
        if (dashboard.DownloadedVersions.Count > 0)
        {
            var preferredVersion = dashboard.SelectedVersion;
            DrawVersionCombo("Preferred version", dashboard.DownloadedVersions, ref preferredVersion);
            dashboard.SelectedVersion = preferredVersion;
        }
        else
        {
            RenderCallout(
                "No downloaded versions available",
                "Download at least one OptiScaler build from the Versions page before starting an install.",
                WarningColor);
        }

        ImGui.Spacing();
        RenderSectionHeader("Details");
        RenderKeyValue("Game Path", game.GamePath);

        if (ImGui.BeginTable("GameDetailSummary", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextColumn();
            RenderCompactKeyValue("Current Version", game.CurrentVersion);

            ImGui.TableNextColumn();
            RenderCompactKeyValue("Install State", game.IsInstalled ? "Installed" : "Not installed");

            ImGui.TableNextColumn();
            RenderCompactKeyValue("DLL", string.IsNullOrWhiteSpace(game.InstalledFilename) ? "-" : game.InstalledFilename);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        RenderSectionHeader("Actions");
        var openFolderWidth = GetButtonWidth("Open Folder", 140f);
        var installWidth = GetButtonWidth("Install OptiScaler", 160f);
        var configureWidth = GetButtonWidth("Configure", 120f);
        var updateWidth = GetButtonWidth("Update Version", 150f);
        var uninstallWidth = GetButtonWidth("Uninstall", 120f);
        var removeWidth = GetButtonWidth("Remove From Library", 180f);

        if (ImGui.Button("Open Folder", new Vector2(openFolderWidth, 0f)))
        {
            dashboard.OpenGameFolder(game);
        }

        if (!game.IsInstalled)
        {
            ContinueOnSameLineIfFits(installWidth);
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
            ContinueOnSameLineIfFits(configureWidth);
            if (ImGui.Button("Configure", new Vector2(configureWidth, 0f)))
            {
                OpenConfigDialog(game);
            }

            ContinueOnSameLineIfFits(updateWidth);
            if (ImGui.Button("Update Version", new Vector2(updateWidth, 0f)))
            {
                OpenUpdateDialog(game);
            }

            ContinueOnSameLineIfFits(uninstallWidth);
            if (ImGui.Button("Uninstall", new Vector2(uninstallWidth, 0f)))
            {
                QueueConfirmation(
                    $"Uninstall from {game.Name}",
                    "This removes OptiScaler files from the selected game but keeps the game in your library.",
                    "Uninstall",
                    () => dashboard.UninstallOptiScaler(game),
                    $"Uninstalled OptiScaler from {game.Name}.");
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Remove From Library", new Vector2(removeWidth, 0f)))
        {
            QueueConfirmation(
                $"Remove {game.Name}",
                game.IsInstalled
                    ? "This removes the game from the library and uninstalls OptiScaler from it."
                    : "This removes the game from the library.",
                "Remove",
                async () =>
                {
                    await dashboard.RemoveGame(game);
                    CloseGameDetailsDialog();
                },
                $"Removed {game.Name} from the library.");
        }
    }

    private void RenderGameDetailsWindow()
    {
        if (_gameDetailsDialog == null)
        {
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("GameDetailsContent", new Vector2(0f, -54f), PaddedPanelChildFlags, PanelWindowFlags);
        RenderGameDetails(_mainViewModel.Dashboard, _gameDetailsDialog.Game);
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.Separator();
        if (ImGui.Button("Close", new Vector2(110f, 0f)))
        {
            CloseGameDetailsDialog();
        }
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

            if (version.IsDownloading)
            {
                ImGui.ProgressBar((float)(version.DownloadProgress / 100d), new Vector2(-1f, 4f));
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
        if (ImGui.Button("Open Releases Page", new Vector2(160f, 0f)))
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
                TextMuted("Choose a game folder, then pick a downloaded version when you are ready to install.");
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
                ImGui.SameLine();
                RenderInlinePill(selectedGame.CurrentVersion, selectedGame.IsInstalled ? SuccessColor : WarningColor);
                if (!string.IsNullOrWhiteSpace(selectedGame.InstalledFilename))
                {
                    ImGui.SameLine();
                    RenderInlinePill(selectedGame.InstalledFilename, InfoColor);
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
        var selectedPath = NativeDialogs.PickFolder("Select Game Directory", _hwnd);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        StartUiTask(async () =>
        {
            var added = await _mainViewModel.Dashboard.AddGameFromPath(selectedPath);
            SetNotification(
                added
                    ? $"Added {selectedPath}."
                    : "That folder is already in the library or could not be used.",
                added ? NotificationKind.Success : NotificationKind.Info);
        }, "Could not add the selected game");
    }

    private float AnimateValue(string key, float target, float speed = 12f)
    {
        var current = _animationValues.GetValueOrDefault(key);
        var delta = Math.Clamp(ImGui.GetIO().DeltaTime * speed, 0f, 1f);
        current += (target - current) * delta;
        _animationValues[key] = current;
        return current;
    }

    private static void RenderInlinePill(string text, Vector4 accent)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(10f, 6f);
        var min = ImGui.GetCursorScreenPos();
        var size = textSize + (padding * 2f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.Dummy(size);

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.15f)), 2f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.45f)), 2f);
        drawList.AddText(min + padding, ImGui.ColorConvertFloat4ToU32(accent), text);
    }

    private static void RenderHeroSignal(string label, string value, Vector4 accent)
    {
        ImGui.TextColored(accent, value);
        ImGui.SameLine();
        ImGui.TextDisabled(label);
    }

    private void RenderConfirmationWindow()
    {
        if (_confirmation == null)
        {
            return;
        }

        ImGui.TextWrapped(_confirmation.Message);
        ImGui.Spacing();

        if (ImGui.Button(_confirmation.ConfirmLabel, new Vector2(110f, 0f)))
        {
            var action = _confirmation.ConfirmAction;
            var successMessage = _confirmation.SuccessMessage;

            StartUiTask(async () =>
            {
                await action();
                if (!string.IsNullOrWhiteSpace(successMessage))
                {
                    SetNotification(successMessage, NotificationKind.Success);
                }
            }, $"{_confirmation.Title} failed");

            CloseConfirmationDialog();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
        {
            CloseConfirmationDialog();
        }
    }

    private void RenderUpdateWindow()
    {
        if (_updateDialog == null)
        {
            return;
        }

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

                StartUiTask(
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
            ImGui.BeginDisabled(wizard.IsInstalling);
            if (ImGui.Button(wizard.NextButtonText, new Vector2(120f, 0f)))
            {
                StartUiTask(() => wizard.Next(), "The installation wizard failed");
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
                if (wizard.ShowEngineWarning)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(WarningColor, "Engine folder detected. Unreal Engine installs usually belong in Binaries\\Win64.");
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
                if (wizard.FileExistsWarning)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(ErrorColor, "A file with that name already exists. Click Next again to overwrite it.");
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

        if (_gameDetailsWindow == null || _gameDetailsWindow.IsClosed)
        {
            _gameDetailsWindow = PreserveImGuiContext(() =>
                CreateNativeWindow($"Game Details - {game.Name}", 760, 560, "GameDetailsRoot", RenderGameDetailsWindow));
            return;
        }

        _gameDetailsWindow.SetTitle($"Game Details - {game.Name}");
        _gameDetailsWindow.Focus();
    }

    private void OpenInstallationDialog(GameInstance game)
    {
        try
        {
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

    private void QueueConfirmation(string title, string message, string confirmLabel, Func<Task> confirmAction, string? successMessage)
    {
        _confirmation = new ConfirmationDialogState(title, message, confirmLabel, confirmAction, successMessage);
        PreserveImGuiContext(() =>
        {
            _confirmationWindow?.Dispose();
            _confirmationWindow = CreateNativeWindow(title, 460, 220, "ConfirmationRoot", RenderConfirmationWindow);
        });
    }

    private void CloseGameDetailsDialog()
    {
        _gameDetailsWindow?.RequestClose();
    }

    private void CloseConfirmationDialog()
    {
        _confirmation = null;
        _confirmationWindow?.RequestClose();
    }

    private void CloseUpdateDialog()
    {
        _updateDialog = null;
        _updateWindow?.RequestClose();
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

    private void SetNotification(string message, NotificationKind kind)
    {
        _notificationMessage = message;
        _notificationKind = kind;
        _notificationExpiresAt = DateTime.UtcNow.AddSeconds(kind == NotificationKind.Error ? 12 : 6);
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

    private bool DrawSelectableRow(
        string id,
        string title,
        string detail,
        bool selected,
        Vector4 accent,
        string badge,
        bool centerText = false)
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

        if (emphasis > 0.02f)
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
        private readonly int _minClientWidth;
        private readonly int _minClientHeight;
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

            RegisterWindowClass();
            CreateWindow(ownerHwnd, title);
            _renderer = new Dx11ImGuiRenderer(_hwnd, _windowWidth, _windowHeight, ConfigureImGuiIo, LoadFonts, ApplyTheme);

            Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOW);
            Win32Native.UpdateWindow(_hwnd);
        }

        public bool IsClosed { get; private set; }

        public bool IsInSizeMove => _isInSizeMove;

        public bool RenderFrame(float delta, bool enableVsync = true)
        {
            if (IsClosed || _isMinimized || _renderer == null || _isRenderingFrame)
            {
                return false;
            }

            _isRenderingFrame = true;
            try
            {
                _renderer.BeginFrame(delta, _windowWidth, _windowHeight);
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
                Win32Native.SetWindowText(_hwnd, title);
            }
        }

        public void Focus()
        {
            if (_hwnd == 0 || IsClosed)
            {
                return;
            }

            Win32Native.ShowWindow(_hwnd, Win32Native.SW_SHOW);
            Win32Native.SetForegroundWindow(_hwnd);
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
            var windowClass = new Win32Native.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEXW>(),
                style = Win32Native.CS_HREDRAW | Win32Native.CS_VREDRAW | Win32Native.CS_OWNDC,
                lpfnWndProc = _windowProcedureDelegate,
                hInstance = _hInstance,
                hCursor = Win32Native.LoadCursor(IntPtr.Zero, (nint)Win32Native.IDC_ARROW),
                hbrBackground = Win32Native.DarkBackgroundBrush,
                lpszClassName = _windowClassName,
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
                Win32Native.WS_OVERLAPPEDWINDOW | Win32Native.WS_VISIBLE,
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
        string Title,
        string Message,
        string ConfirmLabel,
        Func<Task> ConfirmAction,
        string? SuccessMessage);

    private sealed class UpdateDialogState
    {
        public required GameInstance Game { get; init; }

        public OptiScalerVersion? SelectedVersion { get; set; }
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
    }

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
        Error,
    }
}
