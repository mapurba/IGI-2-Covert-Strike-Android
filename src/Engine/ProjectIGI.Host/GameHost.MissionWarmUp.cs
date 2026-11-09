using System.IO;
using ProjectIGI.Core.Configuration;
using ProjectIGI.Core.Diagnostics;
using ProjectIGI.Core.Graphics;
using ProjectIGI.Core.Numerics;
using ProjectIGI.Formats.Sprites;
using ProjectIGI.Frontend;
using ProjectIGI.Game.Menus;
using ProjectIGI.Game.World;
using ProjectIGI.Host.Lighting;
using ProjectIGI.Host.Menu;
using ProjectIGI.Presentation.Rendering;
using ProjectIGI.Rendering;
using ProjectIGI.UI;
using ProjectIGI.UI.Drawing;
using ProjectIGI.UI.Layout;
using ProjectIGI.UI.Text;
using ProjectIGI.UI.Widgets;

namespace ProjectIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The mission-name table, so the card titles a level the way the campaign screen did.</summary>
    private MenuStringTable? _menuStrings;

    /// <summary>The shell's mission list, for the number and the briefing the card sets under the title.</summary>
    private IReadOnlyList<MissionInfo>? _shellMissions;

    /// <summary>The 2D batch, held so a frame can be drawn from outside the render loop.</summary>
    private SpriteBatch? _batch;

    /// <summary>When the level may be shown: once its lighting exists, has converged and has settled.</summary>
    private MissionWarmUpGate _warmUp = new(0);

    /// <summary>The going-in presentation: the departure, the card over the held picture, the reveal.</summary>
    private readonly MissionLoadSequence _loadSequence = new();

    /// <summary>The load the sequence will run once the departure has played out.</summary>
    private Action? _pendingLoad;

    /// <summary>Gets a value indicating whether a mission is on its way in from the menu, the load or the shot still to run.</summary>
    private bool EnteringMission => _pendingLoad is not null || _entry.IsRunning;

    /// <summary>The mission that load brings up, for the load gate to be asked about.</summary>
    private int _pendingLoadMission;

    // The backdrop asked for while its mission's files were still arriving; see RequestBackdrop.
    private int? _backdropAwaitingFiles;

    // The mission -mission asked for while its files were still arriving; see StartMissionWhenReadable.
    private int? _startAwaitingFiles;

    /// <summary>What the load gate last said it was waiting for, so it is reported once rather than every frame.</summary>
    private string? _loadGateWaitingFor;

    /// <summary>The mission being loaded, for the card.</summary>
    private MissionDescriptor? _loadingMission;
    private MissionInfo? _loadingInfo;

    /// <summary>The loading mission's name and description for the load screen, resolved when the load begins.</summary>
    private string _loadingMissionName = string.Empty;
    private string _loadingMissionDescription = string.Empty;

    /// <summary>The loading art Ray sent (the I.G.I. 2 title art), drawn as the load's backdrop.</summary>
    private SpriteResource? _loadingArt;
    private bool _loadingArtAttempted;

    /// <summary>Whether the loading art is the mission's own briefing screen rather than the title art.</summary>
    private bool _loadingArtIsBriefing;

    /// <summary>The briefing screen a custom mission may ship in its own directory, as a baseline JPEG.</summary>
    private const string MissionBriefingImageFile = "briefing.jpg";

    /// <summary>Loads the loading art out of the app's own files.</summary>
    /// <remarks>
    /// The art is Ray's I.G.I. 2 title art, not retail data, so it ships in the APK under
    /// <c>public/Assets/menu_images/igi2-loading-title.jpg</c>; MainActivity extracts it into the app's
    /// files directory and refreshes it on every launch, so an update over an existing install gets
    /// it too. It is a JPEG on purpose: <see cref="SpriteLoader"/> decodes JPEG, ILFF fonts and LOOP
    /// sprites, but not PNG — the previous art shipped as a PNG (<c>igi2-loading.png</c>) and its
    /// decode always threw, so the load stood on its dark background even though the file was found.
    /// The old per-mission briefing sprites (<c>mission{id}.spr</c> in
    /// <c>LOCAL:menusystem/missionsprites.res</c>) were blurry upscaled thumbnails, not loading art
    /// — they are gone. When the art is missing the dark background stands on its own, never a
    /// sprite and never the old bundled splash. Linear filtering: the art is a photographic render
    /// shown fullscreen, not 1:1 pixel art.
    /// </remarks>
    private SpriteResource? GetLoadingArt()
    {
        if (_loadingArtAttempted)
        {
            return _loadingArt;
        }

        _loadingArtAttempted = true;
        _loadingArtIsBriefing = false;

        // A custom mission may ship its own briefing screen beside its scripts; it stands in for the
        // title art on that mission's card. Drawn letterboxed, since a briefing carries text that a
        // stretch to the screen's aspect would distort.
        if (_loadingMission is { } briefed
            && _fileSystem.TryOpenRead($"LOCAL:{briefed.MissionDirectory}/{MissionBriefingImageFile}", out Stream? briefingStream))
        {
            try
            {
                using (briefingStream)
                using (MemoryStream buffer = new())
                {
                    briefingStream.CopyTo(buffer);
                    _loadingArt = SpriteResource.Upload(
                        _device,
                        SpriteLoader.Load(buffer.ToArray(), MissionBriefingImageFile),
                        TextureFilter.Linear);
                }

                _loadingArtIsBriefing = true;
                _logger.Information("Mission", $"Loaded the mission's own briefing screen for its card ({briefed.MissionDirectory}).");
                return _loadingArt;
            }
            catch (Exception ex)
            {
                _logger.Warning("Mission", $"The mission's briefing screen could not be read: {ex.Message}");
            }
        }

        string userDir = SettingsPaths.GetUserDirectory();
        string[] paths =
        [
            Path.Combine(userDir, "Assets", "menu_images", "igi2-loading-title.jpg"),
            Path.Combine(userDir, "menu_images", "igi2-loading-title.jpg"),
            Path.Combine(userDir, "managed", "Assets", "menu_images", "igi2-loading-title.jpg"),
            "/data/user/0/com.projectigi2.app/files/Assets/menu_images/igi2-loading-title.jpg",
            "/data/user/0/com.projectigi2.app/files/managed/Assets/menu_images/igi2-loading-title.jpg",
        ];

        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                _loadingArt = SpriteResource.Upload(
                    _device,
                    SpriteLoader.Load(File.ReadAllBytes(path), "igi2-loading-title.jpg"),
                    TextureFilter.Linear);
                _logger.Information("Mission", $"Loaded mission load art from: {path}.");
                return _loadingArt;
            }
            catch (Exception ex)
            {
                _logger.Warning("Mission", $"Failed to load mission load art from {path}: {ex.Message}");
            }
        }

        _logger.Warning("Mission", "No mission load art found; the load stands on its dark background.");
        return null;
    }

    /// <summary>Resolves the loading mission's name and description for the load screen.</summary>
    /// <remarks>
    /// The same source the mission screen reads: the menu's configuration through
    /// <c>IMenuMissionStats</c>, which for I.G.I. 2 answers with Ray's corrected texts and falls
    /// back to the string table for a mission with none. The name is the check the stats are this
    /// mission's, not the one before it — the corrected names match the archive's, so a mismatch
    /// means stale, and the fallback below is the right text rather than another mission's. The
    /// menu manager is still in the loop's task tree while a load runs: leaving the menu only turns
    /// MenuMode off, it does not tear the manager down.
    /// </remarks>
    private void ResolveLoadingMissionText()
    {
        _loadingMissionName = string.Empty;
        _loadingMissionDescription = string.Empty;

        if (_loadingMission is not { } mission)
        {
            return;
        }

        string retailName = _loadingInfo?.Name ?? _menuStrings?.Get(mission.Name) ?? mission.Name;
        string retailDescription = _loadingInfo?.Description ?? _menuStrings?.Get(mission.Description) ?? mission.Description;

        MenuManagerTask? manager = _loop.Tasks.Root.Descendants().OfType<MenuManagerTask>().FirstOrDefault();
        if (manager?.Configuration is IMenuMissionStats stats
            && stats.TryGetActiveMissionStats(out string statsName, out string statsDescription)
            && string.Equals(statsName, retailName, StringComparison.OrdinalIgnoreCase))
        {
            _loadingMissionName = statsName;
            _loadingMissionDescription = statsDescription;
            return;
        }

        _loadingMissionName = retailName;
        _loadingMissionDescription = retailDescription;
    }

    /// <summary>How many of the blocking load's stages have finished, for the bar while it blocks.</summary>
    private int _loadStageIndex;

    private int _lastWarmUpMilliseconds;
    private int _lastLoadingPresentMilliseconds;
    private int _warmUpFramesDrawn;

    /// <summary>How many named stages <c>BringMissionUp</c> reports, so each moves the bar by one share.</summary>
    private const int LoadStageCount = 14;

    /// <summary>The fewest milliseconds between two loading frames presented from inside a blocking load.</summary>
    /// <remarks>
    /// With vertical sync on every present waits for a refresh, so presenting at every stage would add
    /// a refresh per stage to the load. Throttled, the card still animates a few times a second and the
    /// window never goes long enough without a present to be marked unresponsive.
    /// </remarks>
    private const int LoadingPresentIntervalMilliseconds = 40;

    private static readonly Bgra32 CardWhite = UiPalette.Text;
    private static readonly Bgra32 CardAccent = UiPalette.Accent;
    private static readonly Bgra32 CardMuted = UiPalette.TextSecondary;
    private static readonly Bgra32 CardDim = UiPalette.TextMuted;

    /// <summary>Whether the level is still being held and must not be revealed yet.</summary>
    private bool IsWarmingUp => _warmUp.IsHolding;

    /// <summary>
    /// Whether the presentation still owns the level -- it is withheld, the card is up, or the shot has
    /// the camera -- so nothing in it may run yet: a cut-scene that started under the card, or a patrol
    /// that walked off during the hold, would be one the player never saw begin.
    /// </summary>
    /// <remarks>
    /// Released as the reveal BEGINS rather than as it ends: the level's intro has to be on its first
    /// frame by the time the picture clears, and it cannot be until the level has ticked. Under the
    /// dissolve its first moments are seen arriving, which is what a dissolve is for; after it they would
    /// be a second of the player's own view before the cut.
    /// </remarks>
    private bool PresentationHoldsLevel =>
        IsWarmingUp
        || _loadSequence.Phase is MissionLoadPhase.Depart or MissionLoadPhase.Hold
        || _entry.IsCameraActive;

    /// <summary>Whether a loading screen may be drawn at all this run.</summary>
    /// <remarks>
    /// Never during a capture, for the same reason the console hides its pinned overlays there: a
    /// reference shot is taken at a numbered frame, <c>-screenshot</c> implies <c>-frames 5</c>, and a
    /// screen over frame five would put a loading card into every reference image and straight through
    /// the vanilla pixel gate. The warm-up itself still runs -- it draws no extra frames and moves no
    /// counters, it only withholds -- so a capture sees exactly the scene it always saw.
    /// </remarks>
    private bool ShowsLoadingPanel =>
        _options.ScreenshotPath is null && !_options.IsBenchmark && !_options.HasFlight;

    /// <summary>Whether a load may be presented as the full sequence rather than run on the spot.</summary>
    private bool CinematicLoadAvailable => ShowsLoadingPanel && !_options.Lockstep && _batch is not null;

    /// <summary>
    /// Starts the going-in sequence and defers the load until its departure has played.
    /// </summary>
    /// <param name="missionId">The mission the load brings up.</param>
    /// <param name="load">What brings the mission up: a promotion of the backdrop, or a cold start.</param>
    /// <remarks>
    /// The order is the whole design. The clean picture -- the level as the player sees it now, with no
    /// interface over it -- is held on the very next frame, before the interface is drawn; the interface
    /// then leaves and the picture falls out of focus over a beat; and only then does the render thread
    /// block on the load. Loading first and presenting after was the old order, and it froze the menu,
    /// interface and all, for the length of the load.
    /// </remarks>
    private void BeginCinematicLoad(int missionId, Action load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _pendingLoad = load;
        _pendingLoadMission = missionId;
        _loadGateWaitingFor = null;
        _loadSequence.Begin(capturePicture: true);
    }

    /// <summary>Holds the clean picture if the sequence is waiting for one. Call after the scene, before the interface.</summary>
    /// <remarks>
    /// Once, for the still a departure defocuses; every frame while a live level is being held out of
    /// focus under its caption, so the backdrop pass is defocusing what the shot is looking at now.
    /// </remarks>
    private void CaptureLoadPictureIfWanted()
    {
        if (_loadSequence.WantsCapture)
        {
            _menuFlow.HoldFrame(_window.Width, _window.Height);
            _loadSequence.NotifyCaptured();
            return;
        }

        if (_loadSequence.WantsLivePicture)
        {
            _menuFlow.HoldFrame(_window.Width, _window.Height);
        }
    }

    /// <summary>Whether a mission's files can all be read now, as the load gate says.</summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="waitingFor">What is still missing, when they cannot.</param>
    /// <returns>
    /// <see langword="true"/> when they can. A mission the catalogue does not know has no files to wait for;
    /// whatever then tries to load it reports it.
    /// </returns>
    private bool MissionFilesReady(int missionId, out string? waitingFor)
    {
        waitingFor = null;
        return _missions.Missions.FirstOrDefault(m => m.Id == missionId) is not { } mission
            || _loadGate.IsReady(mission, out waitingFor);
    }

    /// <summary>Asks for a menu backdrop, once its mission's files are there.</summary>
    /// <param name="loader">The backdrop builder, if the menu has one.</param>
    /// <param name="missionId">The mission to show.</param>
    /// <remarks>
    /// A backdrop is a mission loaded behind the menu, and reads the same files a played one does. Until
    /// the gate says they are there -- a browser reads a level's large files when it is first wanted --
    /// the request waits in <see cref="_backdropAwaitingFiles"/> and the menu asks again each frame; the
    /// newest request replaces one still waiting.
    /// </remarks>
    private void RequestBackdrop(MissionPreviewLoader? loader, int missionId)
    {
        if (loader is null)
        {
            return;
        }

        // Nothing could be played from it: a mission the catalogue does not offer cannot be promoted or
        // started, so it is not built behind the menu either.
        if (!_missions.TryGet(missionId, out _))
        {
            _logger.Warning("Mission", $"Asked for a backdrop of mission {missionId}, which is not offered.");
            return;
        }

        if (!MissionFilesReady(missionId, out _))
        {
            _backdropAwaitingFiles = missionId;
            return;
        }

        _backdropAwaitingFiles = null;
        loader.Request(missionId);
    }

    /// <summary>Starts a mission the command line asked for, now or once the gate lets its files be read.</summary>
    /// <param name="missionId">The mission.</param>
    /// <remarks>
    /// The desktop's gate always says yes, so this is the plain start there. A browser fetches a level's
    /// large files when the level is first wanted, and being asked is what starts the fetch: the start
    /// waits in <see cref="_startAwaitingFiles"/> and <see cref="RunCommandLineMissionIfDue"/> asks
    /// again each frame. Started regardless, the level came up with its terrain and models absent.
    /// </remarks>
    private void StartMissionWhenReadable(int missionId)
    {
        if (MissionFilesReady(missionId, out string? waitingFor))
        {
            StartMission(missionId);
            return;
        }

        _startAwaitingFiles = missionId;
        _logger.Information("Mission", $"Mission {missionId} waits for {waitingFor ?? "its files"}.");
    }

    /// <summary>Starts the command line's mission once its files are there. Call between frames.</summary>
    private void RunCommandLineMissionIfDue()
    {
        if (_startAwaitingFiles is not { } missionId || !MissionFilesReady(missionId, out _))
        {
            return;
        }

        _startAwaitingFiles = null;
        StartMission(missionId);
    }

    /// <summary>Runs the deferred load once the departure has played. Call between frames.</summary>
    private void RunPendingLoadIfDue()
    {
        if (!_loadSequence.WantsLoad || _pendingLoad is not { } load)
        {
            return;
        }

        // Not until the mission's files can be read. The sequence holds its picture meanwhile, and the
        // load runs on the frame the gate says yes -- on the desktop, always the first.
        if (!MissionFilesReady(_pendingLoadMission, out string? waitingFor))
        {
            if (!string.Equals(waitingFor, _loadGateWaitingFor, StringComparison.Ordinal))
            {
                _loadGateWaitingFor = waitingFor;
                _logger.Information("Mission", $"Mission {_pendingLoadMission} waits for {waitingFor ?? "its files"}.");
            }

            return;
        }

        _pendingLoad = null;
        load();

        if (!_loadSequence.Loaded)
        {
            // Nothing came up -- a promotion with no backdrop left to promote. The sequence has nothing
            // to hold for; let it go rather than hold a card over whatever the view is.
            _loadSequence.Cancel();
            return;
        }

        // The level is up. The card stays over the held picture until the lighting is ready, and then
        // dissolves straight into the level -- its intro, or the player's own eyes.
    }

    /// <summary>
    /// Starts holding a level back, from before its load begins until the picture it will be shown with is
    /// final.
    /// </summary>
    /// <param name="mission">The mission being brought up.</param>
    /// <remarks>
    /// <para>
    /// <b>Why a load ending is not the same as a level being ready.</b> Most of this renderer's lighting
    /// is temporal: the screen probe gather accumulates its estimate per pixel across frames, the world
    /// radiance cache fills its probes over several, the temporal resolve needs a history before it has
    /// anything to reconstruct from, and the auto-exposure adapts towards the scene rather than jumping
    /// to it. None of that can be computed in the load, because all of it is defined in terms of FRAMES
    /// -- there is no way to have a history without having drawn some. And under the real-time model the
    /// structures it lights from are built rather than switched on: a distance field that may still be
    /// baking, a surface cache that converges over dispatches.
    /// </para>
    /// <para>
    /// So the level is held by <see cref="MissionWarmUpGate"/> until those structures exist and have
    /// converged, and then for a few frames more -- drawn as normal, with every temporal system
    /// accumulating exactly as it would in play, but with the finished picture kept off the screen. That
    /// is what every shipping game does behind a loading screen, and it is the only honest way to promise
    /// that what appears is final: the alternative is showing the player the renderer converging, which
    /// reads as the game breaking and then repairing itself.
    /// </para>
    /// <para>
    /// Every route in gets <see cref="MissionLoadSequence"/>: the held picture, the card, the shot over
    /// the start, the reveal. The card covers the blocking part; the shot's hold covers the lighting.
    /// </para>
    /// </remarks>
    private void BeginMissionWarmUp(MissionDescriptor mission)
    {
        _loadingMission = mission;
        _loadingInfo = _shellMissions?.FirstOrDefault(m => m.Id == mission.Id);
        _loadingArt?.Dispose();
        _loadingArt = null;
        _loadingArtAttempted = false;
        ResolveLoadingMissionText();
        _loadStageIndex = 0;
        _warmUpFramesDrawn = 0;
        _lastWarmUpMilliseconds = _loop.TimeSource.Milliseconds;

        _warmUp = new MissionWarmUpGate(FrameCVars.MissionWarmUp.Value);
        _warmUp.Begin();

        // A level started with nothing on screen -- the command line's -mission -- has no picture to
        // hold and no interface to leave, so the sequence opens straight onto the card over its own
        // surface. Never under the entry shot, which is the cover for that route.
        if (!_loadSequence.IsActive && !_entry.IsRunning && ShowsLoadingPanel)
        {
            _loadSequence.Begin(capturePicture: false);
        }

        _logger.Information(
            "Mission",
            $"Holding the level until its lighting is built and settled ({FrameCVars.MissionWarmUp.Value} "
            + "frame(s) once it is), so what appears is what it will look like.");
    }

    /// <summary>Tells the sequence the level has been built; the hold now only waits on its lighting.</summary>
    private void NotifyMissionBuilt() => _loadSequence.NotifyLoaded();

    /// <summary>Abandons the warm-up, so the level is shown on the next frame.</summary>
    /// <remarks>
    /// For anything that takes the player out of the level before it was ever shown — abandoning to
    /// the campaign, a mission that fails during its own warm-up — where holding on would keep a card
    /// over a menu.
    /// </remarks>
    private void CancelMissionWarmUp()
    {
        _warmUp.Cancel();
        _loadSequence.Cancel();
        _pendingLoad = null;
    }

    /// <summary>Reports one more stage of the blocking load finished, and shows it.</summary>
    /// <remarks>
    /// Called from <c>EndStage</c>, so the fourteen named stages of bringing a mission up each move the
    /// bar by one share while the render thread is otherwise not presenting at all.
    /// </remarks>
    private void ReportLoadStage()
    {
        if (!_warmUp.IsActive)
        {
            return;
        }

        _loadStageIndex++;
        _warmUp.ReportLoad(_loadStageIndex / (float)LoadStageCount);
        PresentLoadingFrame();
    }

    /// <summary>
    /// Advances the hold by one drawn frame and draws the loading screen over the withheld picture.
    /// </summary>
    /// <param name="batch">The 2D batch, between its own begin and end.</param>
    /// <returns><see langword="true"/> when the screen was drawn.</returns>
    /// <remarks>
    /// Drawn over a frame that was fully rendered rather than instead of one. That is the whole point:
    /// the scene has to be drawn for its temporal passes to advance, so what this does is withhold it,
    /// not skip it. Once the gate opens the sequence reveals: the card leaves and the held picture comes
    /// back into focus over the live, settled level.
    /// </remarks>
    private bool DrawMissionWarmUp(SpriteBatch batch)
    {
        int now = _loop.TimeSource.Milliseconds;
        float elapsed = Math.Clamp((now - _lastWarmUpMilliseconds) / 1000f, 0f, 0.1f);
        _lastWarmUpMilliseconds = now;

        if (_warmUp.IsActive)
        {
            _warmUpFramesDrawn++;
            bool fieldPending = _gi.FieldBake is not null;
            bool lightingReady = !_gi.Pending;

            _warmUp.Update(lightingReady, fieldPending, _gi.ConvergeProgress, elapsed);

            if (!_warmUp.IsActive)
            {
                // The level is up. A promoted backdrop still carries the decoded pictures its arming had
                // not reached; the mission decodes those as it meets them, like a level loaded cold does,
                // rather than holding the lot for as long as it is played.
                _memory.ReleaseLoadLeftovers(_world, _detailMaps);

                if (_warmUp.GaveUp)
                {
                    _logger.Warning(
                        "Mission",
                        $"Showing the level after {MissionWarmUpGate.PatienceSeconds:0} s without its lighting "
                        + "finished; it will cross over when it lands.");
                }
                else
                {
                    _logger.Information(
                        "Mission",
                        $"Revealed the level after {_warmUpFramesDrawn} withheld frame(s).");
                }
            }
        }

        _loadSequence.Update(elapsed, ready: _loadSequence.Loaded && !_warmUp.IsActive);
        _switch.AdvanceHold(revealed: _loadSequence.Loaded && !_warmUp.IsActive, loadActive: _loadSequence.IsActive);

        if (!_loadSequence.IsActive || !ShowsLoadingPanel)
        {
            return false;
        }

        PaintLoadingScreen(batch);
        return true;
    }

    /// <summary>
    /// Draws and presents a single loading frame, from outside the render loop, during a blocking load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bringing a mission up is a few hundred milliseconds of work on the render thread -- uploading meshes
    /// and textures, reading nav graphs, capturing the surface cache -- and none of it presents a frame.
    /// So whatever was last on screen stayed there, frozen, and a window that stops presenting for long
    /// enough is dimmed and marked as not responding by the compositor. The loop cannot help with that:
    /// it is precisely what is blocked.
    /// </para>
    /// <para>
    /// So frames are drawn and swapped by hand as each stage of the load finishes, with the same sequence
    /// the loop draws before and after -- the picture already held, the card already up -- so the load is
    /// invisible in the presentation: the bar moves and the light keeps drifting.
    /// </para>
    /// </remarks>
    private void PresentLoadingFrame()
    {
        if (_batch is not { } batch || !ShowsLoadingPanel || !_loadSequence.IsActive)
        {
            return;
        }

        int now = _loop.TimeSource.Milliseconds;
        if (now - _lastLoadingPresentMilliseconds < LoadingPresentIntervalMilliseconds)
        {
            return;
        }

        float elapsed = Math.Clamp((now - _lastWarmUpMilliseconds) / 1000f, 0f, 0.1f);
        _lastWarmUpMilliseconds = now;
        _lastLoadingPresentMilliseconds = now;
        _loadSequence.Update(elapsed, ready: false);

        _device.Clear(Bgra32.FromRgb(8, 10, 13));
        PaintLoadingScreen(batch);
        _scheduler.Flush();
    }

    /// <summary>Paints the loading screen: Ray's I.G.I. 2 art, the mission's name and description, and the bottom progress bar.</summary>
    /// <param name="batch">The 2D batch, not begun.</param>
    private void PaintLoadingScreen(SpriteBatch batch)
    {
        // After a switch of renderer the other renderer's picture stands in for the whole screen.
        if (_switch.PaintResumeFrame(_window.Width, _window.Height))
        {
            return;
        }

        float width = _window.Width;
        float height = _window.Height;
        float seconds = _loop.TimeSource.Milliseconds / 1000f;
        float scale = UiScale.Resolve(_options.UiScale, _window.Width, _window.Height);

        _device.BeginStageTiming(RenderStage.UserInterface);
        _device.Set2DProjection(_window.Width, _window.Height);

        batch.Begin();
        var painter = UiPainter.Create(batch, new LayoutContext(_window.Width, _window.Height));

        // 1. Deep authentic IGI tactical black background
        painter.FillRect(new UiRect(0f, 0f, width, height), Bgra32.FromRgb(8, 10, 18));

        // 2. Fullscreen: Ray's I.G.I. 2 art. When the art cannot be found the dark background
        // stands on its own — never a blurry sprite, never the old bundled splash.
        if (GetLoadingArt() is { } art)
        {
            RectF artRect = new(0f, 0f, width, height);
            if (_loadingArtIsBriefing && art.Asset.DefaultWidth > 0 && art.Asset.DefaultHeight > 0)
            {
                float fit = Math.Min(width / art.Asset.DefaultWidth, height / art.Asset.DefaultHeight);
                float fitWidth = art.Asset.DefaultWidth * fit;
                float fitHeight = art.Asset.DefaultHeight * fit;
                artRect = new RectF((width - fitWidth) * 0.5f, (height - fitHeight) * 0.5f, fitWidth, fitHeight);
            }

            art.DrawStretched(batch, 0, artRect, Bgra32.White, BlendMode.Opaque);
        }

        // 3. The bottom progress bar's geometry, needed to place the text block above it.
        Bgra32 brightGreen = Bgra32.FromRgb(80, 160, 255); // #39e365
        float barMargin = 32f * scale;
        float barWidth = width - (barMargin * 2f);
        float barHeight = 8f * scale;
        float barY = height - (20f * scale) - barHeight;
        float progress = Math.Clamp(_warmUp.Progress, 0.05f, 1f);

        // 4. The text block, stacked from the bar upward: kicker, name, rule, description.
        // Laid out first so the scrim behind it knows where the block starts.
        float left = width * 0.085f;
        float labelY = barY - (17f * scale);
        float labelSize = 13f * scale;
        float blockTop = labelY;
        float kickerTop = 0f;
        float kickerSize = 0f;
        float kickerHeight = 0f;
        float nameTop = 0f;
        float nameSize = 0f;
        float ruleTop = 0f;
        float descTop = 0f;
        float bodySize = 0f;
        float lineHeight = 0f;
        List<string> descLines = [];
        string title = string.Empty;
        string kicker = string.Empty;
        if (_shellType is { } type)
        {
            title = _loadingMissionName.ToUpperInvariant();
            kickerSize = 13f * scale;
            kickerHeight = type.Mono.LineHeight(kickerSize);
            bodySize = 20f * scale;
            lineHeight = type.Body.LineHeight(bodySize) * 1.15f;

            if (_loadingMissionDescription.Length > 0)
            {
                descLines = WrapLines(type.Body, _loadingMissionDescription, bodySize, width * 0.62f, maxLines: 4);
            }

            if (title.Length > 0)
            {
                nameSize = 72f * scale;
                float fit = MathF.Min(1f, (width - (left * 2f)) / MathF.Max(1f, UiPainter.MeasureLabel(type.Display, title, nameSize, 0.01f)));
                nameSize *= fit;
                float nameHeight = nameSize * 0.85f;

                float labelsTop = labelY;
                float descBottom = labelsTop - (16f * scale);
                descTop = descBottom - (descLines.Count * lineHeight);
                if (descLines.Count > 0)
                {
                    ruleTop = descTop - (20f * scale);
                    nameTop = ruleTop - (12f * scale) - nameHeight;
                }
                else
                {
                    nameTop = labelsTop - (16f * scale) - nameHeight;
                }

                kickerTop = nameTop - (8f * scale) - kickerHeight;
                kicker = _loadingInfo is { } info
                    ? FormattableString.Invariant($"MISSION {info.Number:00}")
                    : "MISSION";
                blockTop = kickerTop;
            }
        }

        // 5. A dark scrim over the art's lower half, so the text reads over the picture: flat
        // and dark, the way the menu's own panels are — no glass, no glow.
        if (_shellType is not null && title.Length > 0)
        {
            float scrimTop = MathF.Max(0f, blockTop - (28f * scale));
            Span<UI.Drawing.UiGradientStop> scrim =
            [
                new(0f, new Bgra32(8, 10, 18, 0)),
                new(0.45f, new Bgra32(8, 10, 18, 110)),
                new(1f, new Bgra32(8, 10, 18, 235)),
            ];
            painter.FillGradient(new UiRect(0f, scrimTop, width, height - scrimTop), scrim, UI.Drawing.UiGradientDirection.Vertical);
        }

        // 6. Classic Authentic IGI Phosphor Green Bottom Progress Bar
        // Progress bar background track & border
        painter.FillRect(new UiRect(barMargin, barY, barWidth, barHeight), Bgra32.FromRgb(15, 28, 55));
        painter.FillRect(new UiRect(barMargin - 1f, barY - 1f, barWidth + 2f, 1f), new Bgra32(120, 180, 255, 100));
        painter.FillRect(new UiRect(barMargin - 1f, barY + barHeight, barWidth + 2f, 1f), new Bgra32(120, 180, 255, 100));

        // Green progress fill
        painter.FillRect(new UiRect(barMargin, barY, barWidth * progress, barHeight), brightGreen);

        // Animated glowing sweep effect across progress bar
        float sweep = (seconds * 0.75f) % 1.5f;
        float glowWidth = barWidth * 0.16f;
        float glowLeft = barMargin + (barWidth * (sweep - 0.16f));
        float glowRight = MathF.Min(glowLeft + glowWidth, barMargin + (barWidth * progress));
        glowLeft = MathF.Max(glowLeft, barMargin);
        if (glowRight > glowLeft)
        {
            painter.FillRect(new UiRect(glowLeft, barY, glowRight - glowLeft, barHeight), Bgra32.White);
        }

        // 7. The mission block and the labels above the progress bar.
        if (_shellType is { } st)
        {
            if (title.Length > 0)
            {
                // "MISSION 08 —— DEPLOYING", letterspaced, in the accent.
                UiVec2 pen = painter.DrawLabel(st.Mono, kicker, new UiVec2(left, kickerTop), kickerSize, CardAccent, 0.28f);
                float ruleX = pen.X + (16f * scale);
                painter.FillRect(new UiRect(ruleX, kickerTop + (kickerHeight * 0.5f), 48f * scale, MathF.Max(1f, scale)), UiPalette.AccentAt(0.6f));
                painter.DrawLabel(st.Mono, "DEPLOYING", new UiVec2(ruleX + (64f * scale), kickerTop), kickerSize, CardMuted, 0.28f);

                // The name, in the title face.
                var titleBox = new UiRect(left, nameTop, width, nameSize * 0.85f);
                painter.DrawLabel(st.Display, title, new UiVec2(left, UiRowStyle.CapsCenterY(st.Display, nameSize, titleBox)), nameSize, CardWhite, 0.01f);

                // A hairline under the name, and the description under it.
                if (descLines.Count > 0)
                {
                    painter.FillRect(new UiRect(left, ruleTop, MathF.Min(width * 0.18f, 300f * scale), 2f * scale), CardAccent);

                    float y = descTop;
                    foreach (string line in descLines)
                    {
                        painter.DrawText(st.Body, line, new UiVec2(left, y), bodySize, UiPalette.TextBody);
                        y += lineHeight;
                    }
                }
            }

            // Left: stage / status label
            string stage = _warmUp.IsActive && !string.IsNullOrEmpty(_warmUp.Stage) ? _warmUp.Stage : "LOADING";
            int dots = (int)(seconds * 3f) % 4;
            string dotStr = new('.', dots);
            painter.DrawLabel(st.Mono, $"◆ {stage.ToUpperInvariant()}{dotStr}", new UiVec2(barMargin, labelY), labelSize, Bgra32.FromRgb(110, 160, 210), 0.18f);

            // Right: percentage
            string pct = FormattableString.Invariant($"{(int)(progress * 100f)}%");
            float pctW = st.Mono.MeasureWidth(pct, labelSize);
            painter.DrawText(st.Mono, pct, new UiVec2(barMargin + barWidth - pctW, labelY), labelSize, Bgra32.FromRgb(180, 205, 240));
        }

        batch.End();
        _device.EndStageTiming();
    }

    /// <summary>Paints the card: the mission's number, name and briefing, and what the load is doing.</summary>
    /// <remarks>
    /// Set the way the campaign page sets its mission: a monospaced eyebrow and a rule over the name in the
    /// title face, the briefing under it, and the design's thin amber bar for the progress.
    /// </remarks>
    private void PaintLoadingCard(UiPainter painter, float width, float height, float seconds)
    {
        if (_shellType is not { } type)
        {
            return;
        }

        UiFont body = type.Body;
        float scale = UiScale.Resolve(_options.UiScale, _window.Width, _window.Height);
        float slide = _loadSequence.CardSlide * scale;
        float left = width * 0.085f;
        float bars = height * MissionLoadSequence.LetterboxFraction;

        // The title block sits in the lower third, above the bar, the way a title does over a place: a
        // caption, not a dialog.
        float titleSize = 112f * scale;
        float kickerSize = 13f * scale;
        float bodySize = 20f * scale;
        float baseline = height * 0.60f + slide;
        float titleTop = baseline - (titleSize * 0.85f);

        // "MISSION 08 —— DEPLOYING", letterspaced, in the accent -- on the card, where the card carries the name.
        if (!_loadSequence.LiveScene)
        {
            string kicker = _loadingInfo is { } info
                ? FormattableString.Invariant($"MISSION {info.Number:00}")
                : "MISSION";
            float kickerTop = titleTop - (28f * scale) - type.Mono.LineHeight(kickerSize);
            UiVec2 pen = painter.DrawLabel(type.Mono, kicker, new UiVec2(left, kickerTop), kickerSize, CardAccent, 0.28f);
            float ruleX = pen.X + (16f * scale);
            painter.FillRect(new UiRect(ruleX, kickerTop + (type.Mono.LineHeight(kickerSize) * 0.5f), 48f * scale, MathF.Max(1f, scale)), UiPalette.AccentAt(0.6f));
            painter.DrawLabel(type.Mono, "DEPLOYING", new UiVec2(ruleX + (64f * scale), kickerTop), kickerSize, CardMuted, 0.28f);
        }

        // The name and the briefing are on the card only until the level is up.
        if (!_loadSequence.LiveScene)
        {
            string title = (_loadingInfo?.Name
                ?? (_loadingMission is { } mission ? _menuStrings?.Get(mission.Name) ?? mission.Name : string.Empty))
                .ToUpperInvariant();
            float fit = MathF.Min(1f, (width - (left * 2f)) / MathF.Max(1f, UiPainter.MeasureLabel(type.Display, title, titleSize, 0.01f)));
            float size = titleSize * fit;
            var titleBox = new UiRect(left, titleTop, width, titleSize * 0.85f);
            painter.DrawLabel(type.Display, title, new UiVec2(left, UiRowStyle.CapsCenterY(type.Display, size, titleBox)), size, CardWhite, 0.01f);

            // A hairline that draws itself under the name as the card comes in.
            float ruleWidth = MathF.Min(width * 0.18f, 300f * scale) * _loadSequence.Card;
            float ruleTop = baseline + (18f * scale);
            painter.FillRect(new UiRect(left, ruleTop, ruleWidth, 2f * scale), CardAccent);

            // The briefing, a few lines of it, under the rule.
            if (_loadingInfo is { Description.Length: > 0 } briefed)
            {
                float lineHeight = body.LineHeight(bodySize) * 1.15f;
                float y = ruleTop + (24f * scale);
                foreach (string line in WrapLines(body, briefed.Description, bodySize, width * 0.36f, maxLines: 3))
                {
                    painter.DrawText(body, line, new UiVec2(left, y), bodySize, UiPalette.TextBody);
                    y += lineHeight;
                }
            }
        }

        // Bottom left: what is happening, with a slow pulse of dots.
        float footer = height - bars - (52f * scale);
        int dots = (int)(seconds * 2.5f) % 4;
        float captionSize = 12f * scale;
        painter.FillDiamond(new UiVec2(left + (3f * scale), footer + (type.Mono.LineHeight(captionSize) * 0.5f)), 6f * scale, CardAccent);
        painter.DrawLabel(type.Mono, LoadingCaptions[dots], new UiVec2(left + (16f * scale), footer), captionSize, CardMuted, 0.28f);

        // Bottom right: the stage, the percentage and the bar.
        float barWidth = MathF.Min(width * 0.26f, 460f * scale);
        float barRight = width - left;
        float barLeft = barRight - barWidth;
        float barHeight = 4f * scale;
        float barTop = footer + (14f * scale);
        float progress = Math.Clamp(_warmUp.Progress, 0f, 1f);

        string stage = _warmUp.IsActive ? _warmUp.Stage : "Preparing";
        float stageSize = 11f * scale;
        painter.DrawLabel(type.Mono, stage.ToUpperInvariant(), new UiVec2(barLeft, footer - (8f * scale)), stageSize, CardDim, 0.22f);

        string percent = FormattableString.Invariant($"{(int)(progress * 100f)}%");
        float percentSize = 14f * scale;
        float percentWidth = type.Mono.MeasureWidth(percent, percentSize);
        painter.DrawText(type.Mono, percent, new UiVec2(barRight - percentWidth, footer - (10f * scale)), percentSize, CardWhite);

        painter.FillRect(new UiRect(barLeft, barTop, barWidth, barHeight), UiPalette.White(0.1f));
        painter.FillRect(new UiRect(barLeft, barTop, barWidth * progress, barHeight), CardAccent);

        // A highlight that sweeps the bar, so the card reads as alive even while a stage is long.
        float sweep = (seconds * 0.45f) % 1.35f;
        float glowWidth = barWidth * 0.14f;
        float glowLeft = barLeft + (barWidth * (sweep - 0.14f));
        float glowRight = MathF.Min(glowLeft + glowWidth, barLeft + barWidth);
        glowLeft = MathF.Max(glowLeft, barLeft);
        if (glowRight > glowLeft)
        {
            Span<UI.Drawing.UiGradientStop> glow =
            [
                new(0f, UiPalette.White(0f)),
                new(0.5f, UiPalette.White(0.55f)),
                new(1f, UiPalette.White(0f)),
            ];
            painter.FillGradient(new UiRect(glowLeft, barTop, glowRight - glowLeft, barHeight), glow, UI.Drawing.UiGradientDirection.Horizontal);
        }
    }

    /// <summary>The loading caption with its pulse of dots, made once rather than every frame.</summary>
    private static readonly string[] LoadingCaptions = ["LOADING", "LOADING.", "LOADING..", "LOADING..."];

    /// <summary>Breaks a paragraph into lines that fit a width, greedily, to at most a few lines.</summary>
    private static List<string> WrapLines(UiFont font, string text, float size, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureWidth(candidate, size) <= maxWidth || current.Length == 0)
            {
                current.Clear().Append(candidate);
                continue;
            }

            lines.Add(current.ToString());
            current.Clear().Append(word);
            if (lines.Count == maxLines - 1)
            {
                break;
            }
        }

        if (current.Length > 0 && lines.Count < maxLines)
        {
            string last = current.ToString();
            if (lines.Count == maxLines - 1 && font.MeasureWidth(last + "…", size) > maxWidth)
            {
                while (last.Length > 1 && font.MeasureWidth(last + "…", size) > maxWidth)
                {
                    last = last[..^1];
                }

                last = last.TrimEnd() + "…";
            }

            lines.Add(last);
        }

        return lines;
    }
}
