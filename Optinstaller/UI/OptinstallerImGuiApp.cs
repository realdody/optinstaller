using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using Optinstaller.Models;
using Optinstaller.Platform;
using Optinstaller.ViewModels;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace Optinstaller.UI;

public sealed class OptinstallerImGuiApp : IDisposable
{
    private const string ConfirmationPopupId = "##confirm";
    private const string UpdatePopupId = "##update";
    private const string ConfigPopupId = "##config";
    private const string WizardPopupId = "##wizard";
    private const ImGuiWindowFlags PanelWindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    private const ImGuiChildFlags PaddedPanelChildFlags = ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding;

    private static readonly Vector4 InfoColor = new(0.67f, 0.78f, 0.97f, 1f);
    private static readonly Vector4 SuccessColor = new(0.48f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 WarningColor = new(0.96f, 0.76f, 0.32f, 1f);
    private static readonly Vector4 ErrorColor = new(0.96f, 0.43f, 0.43f, 1f);
    private static readonly Vector4 MutedTextColor = new(0.63f, 0.67f, 0.73f, 1f);

    private readonly UiSynchronizationContext _syncContext;
    private readonly MainWindowViewModel _mainViewModel = new();
    private readonly IWindow _window;

    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imgui;
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
    private bool _openConfirmationPopup;

    private UpdateDialogState? _updateDialog;
    private bool _openUpdatePopup;

    private ConfigDialogState? _configDialog;
    private bool _openConfigPopup;

    private InstallationDialogState? _installationDialog;
    private bool _openWizardPopup;

    public OptinstallerImGuiApp(UiSynchronizationContext syncContext)
    {
        _syncContext = syncContext;

        var options = WindowOptions.Default with
        {
            Title = "Optinstaller",
            Size = new Vector2D<int>(1440, 900),
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
    }

    public void Run()
    {
        _window.Run();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _imgui?.Dispose();
        _input?.Dispose();
        _window.Dispose();
    }

    private async void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input);

        _gl.ClearColor(0.07f, 0.09f, 0.12f, 1f);
        _gl.Viewport(_window.FramebufferSize);

        ApplyTheme();

        try
        {
            await _mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            SetNotification($"Initialization failed: {ex.Message}", NotificationKind.Error);
        }
    }

    private void OnUpdate(double delta)
    {
        _uiTime += (float)delta;
        _syncContext.Pump();
        _imgui?.Update((float)delta);
    }

    private void OnRender(double delta)
    {
        _ = delta;

        if (_gl == null || _imgui == null)
        {
            return;
        }

        _syncContext.Pump();

        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        RenderUi();
        _imgui.Render();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        _gl?.Viewport(size);
    }

    private void OnClosing()
    {
        Dispose();
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

        RenderConfirmationPopup();
        RenderUpdatePopup();
        RenderConfigPopup();
        RenderInstallationPopup();
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
                _window.Close();
            }

            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void RenderSidebar()
    {
        ImGui.BeginChild("Sidebar", new Vector2(248f, 0f), ImGuiChildFlags.Borders);

        ImGui.TextColored(InfoColor, "OPTINSTALLER");
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
    }

    private void DrawPageButton(AppPage page, string title, string subtitle)
    {
        var selected = _currentPage == page;
        if (DrawSelectableRow($"Nav::{page}", title, subtitle, selected, InfoColor, string.Empty))
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
            NotificationKind.Success => (SuccessColor, new Vector4(0.09f, 0.17f, 0.12f, 1f), "Success"),
            NotificationKind.Error => (ErrorColor, new Vector4(0.18f, 0.09f, 0.10f, 1f), "Error"),
            _ => (InfoColor, new Vector4(0.08f, 0.12f, 0.19f, 1f), "Info"),
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

        RenderPageHeader("Dashboard", "Pick a game and install, update, or remove OptiScaler.");
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

        var listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.38f, 300f, 420f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("DashboardList", new Vector2(listWidth, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        RenderSectionHeader($"Games ({filteredGames.Count})");
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
                badge))
            {
                _selectedGamePath = game.GamePath;
                dashboard.SelectedGame = game;
                selectedGame = game;
            }

        }
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.SameLine();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 14f));
        ImGui.BeginChild("DashboardDetail", new Vector2(0f, 0f), PaddedPanelChildFlags, PanelWindowFlags);
        RenderGameDetails(dashboard, selectedGame);
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
        RenderKeyValue("Current Version", game.CurrentVersion);
        RenderKeyValue("Install State", game.IsInstalled ? "OptiScaler detected" : "Not installed");
        RenderKeyValue("Installed Filename", string.IsNullOrWhiteSpace(game.InstalledFilename) ? "-" : game.InstalledFilename);

        ImGui.Spacing();
        RenderSectionHeader("Actions");
        if (ImGui.Button("Open Folder", new Vector2(140f, 0f)))
        {
            dashboard.OpenGameFolder(game);
        }

        if (!game.IsInstalled)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(dashboard.DownloadedVersions.Count == 0);
            if (ImGui.Button("Install OptiScaler", new Vector2(160f, 0f)))
            {
                OpenInstallationDialog(game);
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.Button("Configure", new Vector2(120f, 0f)))
            {
                OpenConfigDialog(game);
            }

            ImGui.SameLine();
            if (ImGui.Button("Update Version", new Vector2(150f, 0f)))
            {
                OpenUpdateDialog(game);
            }

            ImGui.SameLine();
            if (ImGui.Button("Uninstall", new Vector2(120f, 0f)))
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
        if (ImGui.Button("Remove From Library", new Vector2(180f, 0f)))
        {
            QueueConfirmation(
                $"Remove {game.Name}",
                game.IsInstalled
                    ? "This removes the game from the library and uninstalls OptiScaler from it."
                    : "This removes the game from the library.",
                "Remove",
                () => dashboard.RemoveGame(game),
                $"Removed {game.Name} from the library.");
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
                version.IsBleedingEdge ? "BE" : "Official"))
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
        RenderKeyValue("Source", version.IsBleedingEdge ? "Bleeding Edge" : "Official");
        RenderKeyValue("Published", version.PublishedAt == default ? "-" : version.PublishedAt.ToLocalTime().ToString("g"));
        RenderKeyValue("Relative Time", version.RelativeTime);
        RenderKeyValue("File Size", string.IsNullOrWhiteSpace(version.FileSizeDisplay) ? "-" : version.FileSizeDisplay);
        RenderKeyValue("Local Path", version.IsDownloaded && !string.IsNullOrWhiteSpace(version.LocalPath) ? version.LocalPath : "-");

        ImGui.Spacing();
        RenderSectionHeader("Release Notes");
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(version.Description)
            ? "No release notes were available for this entry."
            : version.Description);

        ImGui.Spacing();
        RenderSectionHeader("Actions");
        if (version.IsDownloading)
        {
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(version.DownloadStatus) ? "Downloading..." : version.DownloadStatus);
            ImGui.ProgressBar((float)(version.DownloadProgress / 100d), new Vector2(-1f, 0f));
            return;
        }

        if (version.IsDownloaded)
        {
            if (ImGui.Button("Open Folder", new Vector2(140f, 0f)))
            {
                _mainViewModel.Versions.OpenFolder(version);
            }

            ImGui.SameLine();
            if (ImGui.Button("Delete", new Vector2(120f, 0f)))
            {
                QueueConfirmation(
                    $"Delete {version.TagName}",
                    "This removes the downloaded version from local storage.",
                    "Delete",
                    () => _mainViewModel.Versions.DeleteVersion(version),
                    $"Deleted {version.TagName}.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Use as Default Install", new Vector2(180f, 0f)))
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
        RenderKeyValue("UI Host", "Dear ImGui + Silk.NET + OpenGL");
        RenderKeyValue("Framework", Environment.Version.ToString());
        RenderKeyValue("Window Size", $"{_window.Size.X} x {_window.Size.Y}");
        RenderKeyValue("Tracked Games", _mainViewModel.Dashboard.Games.Count.ToString());
        RenderKeyValue("Downloaded Versions", _mainViewModel.Dashboard.DownloadedVersions.Count.ToString());
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

        ImGui.SameLine();
        ImGui.TextDisabled($"Games {totalGames}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Installed {installedCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Ready {pendingCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Versions {dashboard.DownloadedVersions.Count}");
    }

    private void RenderVersionsToolbar(VersionManagerViewModel versions, OptiScalerVersion? selectedVersion)
    {
        ImGui.TextDisabled($"All {versions.TotalCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Downloaded {versions.DownloadedCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Online {versions.AvailableVersions.Count}");

        if (selectedVersion != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Selected {selectedVersion.TagName}");
        }
    }

    private void RenderDashboardHero(DashboardViewModel dashboard, GameInstance? selectedGame, int totalGames, int installedCount, int pendingCount)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.11f, 0.16f, 1f));
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
                    if (ImGui.Button("Configure", new Vector2(120f, 0f)))
                    {
                        OpenConfigDialog(selectedGame);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Update Version", new Vector2(150f, 0f)))
                    {
                        OpenUpdateDialog(selectedGame);
                    }
                }
                else
                {
                    ImGui.BeginDisabled(dashboard.DownloadedVersions.Count == 0);
                    if (ImGui.Button("Install OptiScaler", new Vector2(160f, 0f)))
                    {
                        OpenInstallationDialog(selectedGame);
                    }
                    ImGui.EndDisabled();
                }

                ImGui.SameLine();
                if (ImGui.Button("Open Folder", new Vector2(130f, 0f)))
                {
                    dashboard.OpenGameFolder(selectedGame);
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(InfoColor, "Summary");
            RenderHeroSignal("Games", totalGames.ToString(), InfoColor);
            RenderHeroSignal("Installed", installedCount.ToString(), SuccessColor);
            RenderHeroSignal("Pending", pendingCount.ToString(), WarningColor);
            RenderHeroSignal("Saved versions", dashboard.DownloadedVersions.Count.ToString(), new Vector4(0.63f, 0.73f, 0.98f, 1f));
            TextMuted($"Default install version: {dashboard.SelectedVersion?.TagName ?? "None selected"}");

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    private void RenderGameHeroCard(DashboardViewModel dashboard, GameInstance game)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.11f, 0.16f, 1f));
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
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.11f, 0.16f, 1f));
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

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Lerp(new Vector4(0.09f, 0.11f, 0.16f, 1f), new Vector4(0.12f, 0.16f, 0.24f, 1f), pulse));
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
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.11f, 0.16f, 1f));
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
        var selectedPath = NativeDialogs.PickFolder("Select Game Directory");
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

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.15f)), 999f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.45f)), 999f);
        drawList.AddText(min + padding, ImGui.ColorConvertFloat4ToU32(accent), text);
    }

    private static void RenderHeroSignal(string label, string value, Vector4 accent)
    {
        ImGui.TextColored(accent, value);
        ImGui.SameLine();
        ImGui.TextDisabled(label);
    }

    private void RenderConfirmationPopup()
    {
        if (_confirmation == null)
        {
            return;
        }

        if (_openConfirmationPopup)
        {
            ImGui.OpenPopup(ConfirmationPopupId);
            _openConfirmationPopup = false;
        }

        ImGui.SetNextWindowSize(new Vector2(460f, 0f), ImGuiCond.Appearing);
        var shouldClose = false;

        if (ImGui.BeginPopupModal(ConfirmationPopupId, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(_confirmation.Title);
            ImGui.Separator();
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

                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
            {
                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.EndPopup();
        }

        if (shouldClose)
        {
            _confirmation = null;
        }
    }

    private void RenderUpdatePopup()
    {
        if (_updateDialog == null)
        {
            return;
        }

        if (_openUpdatePopup)
        {
            ImGui.OpenPopup(UpdatePopupId);
            _openUpdatePopup = false;
        }

        ImGui.SetNextWindowSize(new Vector2(520f, 0f), ImGuiCond.Appearing);
        var shouldClose = false;

        if (ImGui.BeginPopupModal(UpdatePopupId, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Update {_updateDialog.Game.Name}");
            ImGui.Separator();
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

                    ImGui.CloseCurrentPopup();
                    shouldClose = true;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
            {
                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.EndPopup();
        }

        if (shouldClose)
        {
            _updateDialog = null;
        }
    }

    private void RenderConfigPopup()
    {
        if (_configDialog == null)
        {
            return;
        }

        if (_openConfigPopup)
        {
            ImGui.OpenPopup(ConfigPopupId);
            _openConfigPopup = false;
        }

        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Appearing);
        var shouldClose = false;

        if (ImGui.BeginPopupModal(ConfigPopupId, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Configuration: {_configDialog.Game.Name}");
            ImGui.Separator();

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
                _configDialog.ViewModel.OpenFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Save", new Vector2(110f, 0f)))
            {
                _configDialog.ViewModel.Save();
                SetNotification($"Saved configuration for {_configDialog.Game.Name}.", NotificationKind.Success);
                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110f, 0f)))
            {
                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.EndPopup();
        }

        if (shouldClose)
        {
            _configDialog = null;
        }
    }

    private void RenderInstallationPopup()
    {
        if (_installationDialog == null)
        {
            return;
        }

        if (_openWizardPopup)
        {
            ImGui.OpenPopup(WizardPopupId);
            _openWizardPopup = false;
        }

        ImGui.SetNextWindowSize(new Vector2(920f, 600f), ImGuiCond.Appearing);
        var shouldClose = false;
        var wizard = _installationDialog.ViewModel;

        if (ImGui.BeginPopupModal(WizardPopupId, ImGuiWindowFlags.NoResize))
        {
            ImGui.TextUnformatted($"Install OptiScaler: {_installationDialog.Game.Name}");
            ImGui.Separator();

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
                    ImGui.CloseCurrentPopup();
                    shouldClose = true;
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
                ImGui.CloseCurrentPopup();
                shouldClose = true;
            }

            ImGui.EndPopup();
        }

        if (shouldClose)
        {
            _installationDialog = null;
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

    private void OpenInstallationDialog(GameInstance game)
    {
        try
        {
            _installationDialog = new InstallationDialogState
            {
                Game = game,
                ViewModel = _mainViewModel.Dashboard.CreateInstallationWizard(game),
            };
            _openWizardPopup = true;
        }
        catch (Exception ex)
        {
            SetNotification(ex.Message, NotificationKind.Error);
        }
    }

    private void CloseInstallationDialog(bool showSuccessMessage)
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
            _openConfigPopup = true;
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
        _openUpdatePopup = true;
    }

    private void QueueConfirmation(string title, string message, string confirmLabel, Func<Task> confirmAction, string? successMessage)
    {
        _confirmation = new ConfirmationDialogState(title, message, confirmLabel, confirmAction, successMessage);
        _openConfirmationPopup = true;
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
            _selectedGamePath = null;
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

        selectedGame ??= games[0];
        _selectedGamePath = selectedGame.GamePath;
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
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.14f, 0.19f, 1f));
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
        string badge)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f);
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(0f, ImGui.GetContentRegionAvail().X - 8f), 50f);
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();

        var emphasis = AnimateValue($"Row::{id}", selected ? 1f : hovered ? 0.5f : 0f);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        var background = Vector4.Lerp(new Vector4(0.10f, 0.12f, 0.16f, 0.30f), new Vector4(0.15f, 0.20f, 0.28f, 0.82f), emphasis);
        var border = Vector4.Lerp(new Vector4(0.17f, 0.20f, 0.26f, 0.20f), new Vector4(accent.X, accent.Y, accent.Z, 0.65f), emphasis);
        var textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.93f, 0.95f, 0.98f, 1f));
        var detailColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(MutedTextColor, accent, 0.35f));
        var badgeColor = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(new Vector4(accent.X, accent.Y, accent.Z, 0.75f), accent, emphasis));

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), 10f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 10f, ImDrawFlags.None, 1f);

        if (emphasis > 0.02f)
        {
            drawList.AddLine(
                new Vector2(min.X + 8f, min.Y + 8f),
                new Vector2(min.X + 8f, max.Y - 8f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.85f * emphasis)),
                2f);
        }

        drawList.AddText(min + new Vector2(18f, 8f), textColor, title);
        drawList.AddText(min + new Vector2(18f, 27f), detailColor, detail);

        if (!string.IsNullOrWhiteSpace(badge))
        {
            var badgeSize = ImGui.CalcTextSize(badge);
            var badgeX = max.X - badgeSize.X - 14f;
            drawList.AddText(new Vector2(badgeX, min.Y + 16f), badgeColor, badge);
        }

        return clicked;
    }

    private static void RenderKeyValue(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextWrapped(value);
        ImGui.Spacing();
    }

    private static void RenderCallout(string title, string message, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.14f, 0.19f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.45f));
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
        style.WindowRounding = 10f;
        style.ChildRounding = 10f;
        style.FrameRounding = 8f;
        style.PopupRounding = 8f;
        style.GrabRounding = 8f;
        style.ScrollbarRounding = 8f;
        style.TabRounding = 8f;
        style.FramePadding = new Vector2(10f, 7f);
        style.ItemSpacing = new Vector2(10f, 10f);
        style.ItemInnerSpacing = new Vector2(8f, 6f);

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.07f, 0.09f, 0.12f, 1f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.12f, 0.16f, 1f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.09f, 0.11f, 0.15f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.18f, 0.22f, 0.29f, 1f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.13f, 0.17f, 0.23f, 1f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.18f, 0.24f, 0.33f, 1f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.21f, 0.30f, 0.41f, 1f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.08f, 0.10f, 0.14f, 1f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.08f, 0.10f, 0.14f, 1f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.09f, 0.11f, 0.15f, 1f);
        colors[(int)ImGuiCol.Button] = new Vector4(0.17f, 0.24f, 0.34f, 1f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.23f, 0.32f, 0.46f, 1f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.28f, 0.38f, 0.55f, 1f);
        colors[(int)ImGuiCol.Header] = new Vector4(0.19f, 0.27f, 0.39f, 1f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.24f, 0.34f, 0.49f, 1f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.28f, 0.39f, 0.57f, 1f);
        colors[(int)ImGuiCol.CheckMark] = new Vector4(0.65f, 0.83f, 1f, 1f);
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.40f, 0.61f, 0.90f, 1f);
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.47f, 0.69f, 0.97f, 1f);
        colors[(int)ImGuiCol.Separator] = new Vector4(0.18f, 0.22f, 0.29f, 1f);
        colors[(int)ImGuiCol.Text] = new Vector4(0.93f, 0.95f, 0.98f, 1f);
        colors[(int)ImGuiCol.TextDisabled] = MutedTextColor;
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
