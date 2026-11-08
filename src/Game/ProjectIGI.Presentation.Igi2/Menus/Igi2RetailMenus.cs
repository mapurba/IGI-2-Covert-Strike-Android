using System.IO;
using ProjectIGI.Core.Configuration;
using ProjectIGI.Core.Diagnostics;
using ProjectIGI.Engine.Titles;
using ProjectIGI.Formats.Sprites;
using ProjectIGI.Game.Igi2.Config;
using ProjectIGI.Game.Igi2.Menus;
using ProjectIGI.Game.Igi2.Saves;
using ProjectIGI.Game.Menus;
using ProjectIGI.Game.World;
using ProjectIGI.Presentation.Igi1.Menus;
using ProjectIGI.Presentation.Rendering;
using ProjectIGI.Scripting.Runtime;
using MenuStyle = ProjectIGI.Game.Menus.MenuLayout.MenuStyle;

namespace ProjectIGI.Presentation.Igi2.Menus;

/// <summary>I.G.I. 2's retail menus: <c>mainmenu.qvm</c> run in the loop and drawn by the menu renderer.</summary>
/// <remarks>
/// The original's <c>MenuSystem_Load(root, "LOCAL:menusystem\mainmenu.qsc", 1, 1, 1)</c>. The mission briefing images are
/// in their own archive beside the menu's. The manager runs under I.G.I. 2's numbering — the script's
/// <c>MENUMANAGER_STARTGAME</c> is 14, not Project I.G.I.'s 13 — so it takes the Igi2 dialect before it is initialised.
/// </remarks>
internal sealed class Igi2RetailMenus : IRetailMenus
{
    private readonly RetailMenuHost _shell;
    private readonly MenuSystem _menu;
    private readonly SpriteResource? _missionSelectBackground;

    /// <summary>Initializes a new instance of the <see cref="Igi2RetailMenus"/> class, and starts the menu script.</summary>
    /// <param name="request">What the menus are started with.</param>
    public Igi2RetailMenus(in RetailMenusRequest request)
    {
        RetailMenusRequest capturedRequest = request;

        // The loop's registry already holds this title's menu classes under the shared names
        // ("MenuManager" is Igi2MenuManagerTask, and so on), and MenuSystem.Load registers the
        // shared classes under those same names -- without the swap it throws on the first of
        // them. Swap the colliding names to the shared classes for the load, then swap them back,
        // so the menu is built from the shared classes and later scripts still spawn this
        // title's. The load's own registration tolerates the names it just swapped (see
        // TaskTypeRegistry.RegisterOrKeep), since they already make the shared classes.
        IReadOnlyList<string> swapped = Igi2MenuTypeSwap.SwapToShared(request.Loop.Types);
        MenuSystem menu;
        try
        {
            menu = MenuSystem.Load(
                request.FileSystem.ReadAllBytes("LOCAL:menusystem/mainmenu.qvm"),
                request.Loop,
                request.Logger);
        }
        finally
        {
            Igi2MenuTypeSwap.SwapToIgi2(request.Loop.Types, swapped);
        }

        _menu = menu;

        // The script's top-level code only creates tasks, so nothing has read the navigator yet; the manager takes
        // I.G.I. 2's numbering now, before Initialize captures the navigator into the script context.
        menu.Manager.SetDialect(MenuManagerDialect.Igi2);

        Action<bool> leave = request.Leave;
        _missionSelectBackground = LoadMissionSelectBackground(request);
        _shell = new RetailMenuHost(new RetailMenuPresentationRequest(
            request.Device,
            request.FileSystem,
            menu.Manager.ResourcePath,
            request.Strings() ?? throw new InvalidOperationException("The language's text is loaded before the menus start."),
            // The menu's text is drawn from font1/font3/font4.fnt, and I.G.I. 2 keeps its fonts in
            // common/fonts/fonts_med.res — not in the menu's own archive, as Project I.G.I.'s are.
            // Without it every MenuText resolves to a null font and draws nothing, which is exactly
            // what the first device test showed: backdrop, logo, frame and cursor, no entries.
            ["LOCAL:menusystem/missionsprites.res", "LOCAL:common/fonts/fonts_med.res"],
            request.Input,
            request.Batch,
            request.ScreenWidth,
            request.ScreenHeight,
            request.Scale,
            request.Sounds,
            // Leaving with StartGame starts the chosen mission; leaving any other way quits.
            leaving => leave(leaving == MenuManagerState.StartGame),
            request.Logger,
            // I.G.I. 2's own colour table, and none of the shared green chrome: no BackgroundFx
            // overlay, and the screen panel draws in blue. The presenter is where the title is
            // known, so it is set here.
            MenuStyle.Igi2,
            MissionSelectBackground: _missionSelectBackground));

        // The script passes 904 as "Current menuscreen": the screen it actually opens on comes
        // from its default-screen expression, Flow_IsPlayCredits() ? 916 : 923, which the shared
        // manager never evaluates -- it starts on CurrentMenuScreen. The logo screen 923 would
        // need its 169-tick timeout to reach the main menu, and nothing in the tree implements a
        // screen timeout (nothing reads TicksToTimeout), so open directly on the main menu
        // instead. Phase 2 owns the default-screen native and the splash timeout.
        menu.Manager.CurrentMenuScreen = 900;

        // -screen is a development shortcut, not a game feature: the script decides where the menu
        // opens, and this overwrites that one parameter before the manager reads it.
        if (request.StartScreen > 0)
        {
            menu.Manager.CurrentMenuScreen = request.StartScreen;
        }

        // The configuration the mission screen fills from and the settings screens read: I.G.I. 2's
        // own profiles over Igi2GameConfig, not Project I.G.I.'s ConfigNatives, which would read the
        // wrong profiles. Setting it also wires the manager's fallback, which is what opens the
        // ExternalNatives door Game_SetMission and Game_SetGameMode come through — without a
        // configuration the fallback stays null and every Config_* and Game_* call in the menu
        // evaluates to zero unimplemented.
        IServiceCatalog? services = request.Services;
        Igi2GameConfig config = services is not null && services.TryGet(out Igi2GameConfig? foundConfig)
            ? foundConfig
            : new Igi2GameConfig();
        Igi2SaveSession? saves = services is not null && services.TryGet(out Igi2SaveSession? foundSaves)
            ? foundSaves
            : null;
        Igi2ConfigNatives configuration = new(
            config,
            request.Missions,
            request.Strings,
            saves,
            request.Logger,
            request.Configuration);

        // The graphics screens' live values: the renderer's name for the device list, the
        // render-scale tier for the resolution list, and the applier OK runs to push the filed
        // options to the live stores and write settings.cfg. The record is seeded from the
        // live stores first, so the screen shows what the game is actually using rather than
        // what config.qvm last said — the mapped options have no writer for that file, so
        // after a relaunch the two disagree until the seed.
        configuration.ConfigureGraphics(
            request.Device.Capabilities.Renderer,
            () => Igi2GraphicsApplier.RenderScaleTier,
            tier => Igi2GraphicsApplier.RenderScaleTier = tier);
        config.ApplyGraphics = applied =>
        {
            Igi2GraphicsApplier.Apply(applied);
            capturedRequest.SaveSettings?.Invoke();
        };
        Igi2GraphicsApplier.SyncFromLive(config);

        menu.Manager.Configuration = configuration;

        // The mission screen's OK button starts whatever id the list holds, so the lock is enforced
        // where the id is taken as well as where the list is drawn: a locked row can never be
        // selected, but a stale or scripted id reaching Game_SetMission must not start its mission
        // either. Refusing it leaves the pending mission at zero, which the leave handler already
        // answers by reopening the main menu instead of starting nothing.
        menu.Manager.ExternalNatives = (name, arguments) =>
        {
            if (string.Equals(name, GameMissionState.SetMissionNative, StringComparison.Ordinal)
                && !configuration.IsUnlocked(arguments.GetInt32(0)))
            {
                capturedRequest.Logger.Warning("Menu", $"Refused to start locked mission {arguments.GetInt32(0)}.");
                return QvmValue.FromInt32(0);
            }

            return capturedRequest.ExternalNatives(name, arguments);
        };
        menu.Manager.Initialize(menu, _shell, request.Logger);
    }

    /// <summary>Loads the mission-select screen's cover art out of the app's own files.</summary>
    /// <remarks>
    /// The art is the game's cover, not retail data, so it ships in the APK under
    /// <c>public/Assets/menu_images/igi2-cover.jpg</c> and MainActivity extracts it into the app's
    /// files directory. The candidates mirror the old loading splash's file hunt: the user directory,
    /// then the extracted asset folders, then the package's files directory outright. Missing art is
    /// not an error — the screen falls back to the script's <c>mainmenu.jpg</c> background.
    /// </remarks>
    private static SpriteResource? LoadMissionSelectBackground(in RetailMenusRequest request)
    {
        string userDir = SettingsPaths.GetUserDirectory();
        string[] paths =
        [
            Path.Combine(userDir, "Assets", "menu_images", "igi2-cover.jpg"),
            Path.Combine(userDir, "menu_images", "igi2-cover.jpg"),
            "/data/user/0/com.projectigi2.app/files/Assets/menu_images/igi2-cover.jpg",
            "/data/user/0/com.projectigi2.app/files/managed/Assets/menu_images/igi2-cover.jpg",
            "/data/user/0/com.projectigi2.app/files/menu_images/igi2-cover.jpg",
        ];

        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                SpriteResource background = SpriteResource.Upload(
                    request.Device,
                    SpriteLoader.Load(File.ReadAllBytes(path), "igi2-cover.jpg"));
                request.Logger.Information("Menu", $"Loaded mission-select background from: {path}.");
                return background;
            }
            catch (Exception ex)
            {
                request.Logger.Warning("Menu", $"Failed to load mission-select background from {path}: {ex.Message}");
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void CloseScreens() => _shell.CloseScreens();

    /// <inheritdoc/>
    public void RunAction(string expression) => _menu.Manager.RunAction(expression);

    /// <inheritdoc/>
    public void Dispose()
    {
        _shell.Dispose();
        _missionSelectBackground?.Dispose();
    }
}
