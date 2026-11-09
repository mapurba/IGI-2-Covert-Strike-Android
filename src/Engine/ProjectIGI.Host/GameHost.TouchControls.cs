using ProjectIGI.Core.Diagnostics;
using ProjectIGI.Core.Graphics;
using ProjectIGI.Game.Config;
using ProjectIGI.Game.Player;
using ProjectIGI.Input;
using ProjectIGI.Presentation;
using ProjectIGI.Presentation.Hud.Remastered;
using ProjectIGI.Presentation.Hud.Remastered.Touch;
using ProjectIGI.UI.Layout;

namespace ProjectIGI.Host;

/// <summary>A touch screen's controls: the fingers read once a frame and pressed as the keys the player's actions are bound to.</summary>
/// <remarks>
/// Whatever has a touch screen -- the browser build on a phone, a tablet -- reports its fingers in <see cref="InputState.Touch"/>,
/// and nothing is shown or pressed until the first one comes down. The keys and mouse buttons pressed are the ones the player's
/// profile binds to each action, so a rebinding moves the button with it, and the game reads them as it reads a keyboard.
/// </remarks>
public sealed partial class GameHost
{
    private readonly TouchControls _touchControls = new();
    private readonly HashSet<KeyCode> _touchKeys = [];
    private readonly HashSet<KeyCode> _touchKeysWanted = [];
    private readonly HashSet<MouseButton> _touchButtons = [];
    private readonly HashSet<MouseButton> _touchButtonsWanted = [];
    private (bool Up, bool Down, bool Left, bool Right) _touchStickHeld;
    // Crouch is a toggle, not a hold: one tap crouches, the next stands up, so the
    // player can crouch and move with two fingers (stick + look) instead of holding
    // a third finger on Crouch.
    private bool _crouchToggled;
    private bool _crouchHeldPrev;
    // The map computer's zoom buttons step one level per tap, not while held: a wheel
    // notch zooms once, so the press edge is what the computer is fed.
    private bool _computerZoomInHeldPrev;
    private bool _computerZoomOutHeldPrev;
    // The map computer's navigation stick, held per direction like the movement stick's:
    // each direction lets go only back inside the smaller release zone.
    private (bool Up, bool Down, bool Left, bool Right) _computerStickHeld;
    // A held arrow pans a whole step a tick, far too fast to steer by, so the navigation
    // stick only holds the arrows one tick in this many: a third of the keyboard's rate.
    // Control stays held the whole time the stick is off centre.
    private const int ComputerStickStepTicks = 3;
    private int _computerStickPulse;

    /// <summary>The action the crouch button holds: the running title's own crouch where it replaces the shared one (I.G.I. 2), else the shared action. The shared crouch bit is stripped for a replacing title, so sending the shared name would silently do nothing — the old button only worked because the title's alternate row happened to share the same key.</summary>
    private string CrouchAction()
    {
        if (Gameplay?.InputBindings is { } bindings)
        {
            foreach (TitleInputBinding binding in bindings)
            {
                if (binding.Replaces == PlayerAction.Crouch)
                {
                    return binding.Default.Action;
                }
            }
        }

        return "Crouch";
    }

    /// <summary>The action each button holds, as the profile names it.</summary>
    private string? TouchAction(TouchButton button) => button switch
    {
        TouchButton.Fire => "Fire",
        TouchButton.Scope => "AlternateFire",
        TouchButton.Reload => "Reload",
        TouchButton.Jump => "Jump",
        TouchButton.Crouch => CrouchAction(),
        TouchButton.LowerStance => "Title_igi2_LowerStance",
        TouchButton.RaiseStance => "Title_igi2_RaiseStance",
        TouchButton.WalkRun => "Title_igi2_ToggleWalkRun",
        TouchButton.DropWeapon => "Title_igi2_DropWeapon",
        TouchButton.Thermal => "Title_igi2_Thermal",
        TouchButton.Use => "Activate",
        TouchButton.Weapon => "NextWeapon",
        TouchButton.Peek => "Peek",
        TouchButton.Map => "MapComputer",
        TouchButton.Binoculars => "Binoculars",
        TouchButton.ZoomIn => "ZoomIn",
        TouchButton.ZoomOut => "ZoomOut",
        TouchButton.Close => "MapComputer",
        _ => null,
    };

    /// <summary>Whether a title's own map computer is open. The host's flag is only ever set by the tactical computer or I.G.I. 1's mission tick, so a title computer — I.G.I. 2's, opened from its own Map action — would otherwise never switch the controls to their computer layout, and its zoom buttons and navigation stick would never appear.</summary>
    private bool TitleComputerOpen => TitlePlayer is ITitlePlayerComputer { IsComputerShown: true };

    /// <summary>What the game is showing, for the controls: a cutscene, play with the pointer locked, or anything with a pointer.</summary>
    private TouchControlMode CurrentTouchMode()
    {
        if (TouchControls.IsCustomizing)
        {
            return TouchControlMode.Customizing;
        }

        if (!_menuFlow.MenuMode && (_mission.Cutscenes?.IsPlaying == true || TitlePlayer is { IsCutscenePlaying: true }))
        {
            return TouchControlMode.Cutscene;
        }

        if ((_computerOpen || TitleComputerOpen) && !_menuFlow.MenuMode)
        {
            return TouchControlMode.Computer;
        }

        // Play is read from what is on the screen rather than from the cursor lock, which the look gives up and takes back for a
        // frame at a time (a settling frame, the weapon wheel): each change of mode lets every finger go, and the stick with it.
        bool play = _playerMode && !_menuFlow.MenuMode && !_computerOpen && !_session.FailedMenu;
        return play ? TouchControlMode.Play : TouchControlMode.Pointer;
    }

    /// <summary>Reads the frame's fingers and presses what they hold. Called straight after the window's events are taken.</summary>
    private void ApplyTouchControls(FrameLoopState state)
    {
        InputState input = _window.Input;
        if (!input.Touch.InUse || _window.Width <= 0 || _window.Height <= 0)
        {
            return;
        }

        TouchControlMode mode = CurrentTouchMode();
        // I.G.I. 2's own buttons only exist when the running title defines them; under
        // any other title they stay out of the layout instead of sitting there dead.
        // The layout re-runs when this flips, so a title change mid-session is picked up.
        _touchControls.ShowIgi2Buttons = Gameplay?.InputBindings.Any(b => b.Default.Action.StartsWith("Title_igi2_", StringComparison.OrdinalIgnoreCase)) == true;
        _touchControls.Update(input.Touch, mode, new LayoutContext(_window.Width, _window.Height));

        // Leaving play clears the crouch toggle, so the player does not come back
        // from a menu or a computer still crouched.
        if (mode != TouchControlMode.Play)
        {
            _crouchToggled = false;
            _crouchHeldPrev = false;
        }

        // Leaving the computer clears the zoom edges and the stick's held directions, so a
        // zoom button held when it closed does not swallow the first tap when it opens again,
        // and the map does not keep panning from a stick that is no longer there.
        if (mode != TouchControlMode.Computer)
        {
            _computerZoomInHeldPrev = false;
            _computerZoomOutHeldPrev = false;
            _computerStickHeld = default;
            _computerStickPulse = 0;
        }

        // No early return for Customizing: the Play/Computer blocks below are mode-gated, so
        // nothing new is wanted while customizing — but the release section at the end must still
        // run, or touch input held when customization started (e.g. Fire) stays latched and the
        // weapon keeps firing for the whole customization session.
        _touchKeysWanted.Clear();
        _touchButtonsWanted.Clear();
        PlayerProfile? profile = _configuration.Active;

        if (mode == TouchControlMode.Computer)
        {
            foreach (TouchButton button in _touchControls.Held)
            {
                // Pause stays usable while the computer is open: the same guards as in
                // play, so it cannot open over a failed mission, a cutscene or a menu.
                // The computer itself stays open behind the pause menu and is there
                // again when it closes.
                if (button == TouchButton.Pause)
                {
                    if (!_menuFlow.PauseMenu
                        && !_computerOpen
                        && !_session.FailedMenu
                        && _mission.Cutscenes?.IsPlaying != true
                        && _mission.MenusEnabled
                        && state.Shell is not null
                        && _session.Mission is not null)
                    {
                        _menuFlow.MenuMode = true;
                        _menuFlow.PauseMenu = true;
                        state.Shell.OpenPauseMenu();
                        HostedPlayer?.HoldStill();
                        _window.CursorLocked = false;
                        _logger.Information("Menu", "Paused via touch button.");
                    }
                }
                // The zoom buttons feed the computer's wheel, one notch per tap: the map
                // zooms a level per wheel notch and would run to its end on a held key.
                // Anything else held is wanted as its action (Map closes the computer).
                else if (button == TouchButton.ZoomIn)
                {
                    if (!_computerZoomInHeldPrev)
                    {
                        _titleWheel += 1f;
                    }
                }
                else if (button == TouchButton.ZoomOut)
                {
                    if (!_computerZoomOutHeldPrev)
                    {
                        _titleWheel -= 1f;
                    }
                }
                else if (TouchAction(button) is { } action)
                {
                    Want(profile, action);
                }
            }

            _computerZoomInHeldPrev = _touchControls.Held.Contains(TouchButton.ZoomIn);
            _computerZoomOutHeldPrev = _touchControls.Held.Contains(TouchButton.ZoomOut);

            // The navigation stick pans the map the way holding Ctrl with an arrow key does:
            // the map only pans on an arrow while Control is down, so the stick holds both.
            // The keys are the computer's fixed ones, not a bound action, and the release
            // section below lets them go when the stick comes back to centre.
            // The arrows are only held one tick in three while the stick is off centre: a
            // held arrow pans a whole step a tick, which drifts far too fast to steer by,
            // while Control stays held the whole time (lifting it would hand the arrows
            // back to the menu). The dead and release zones are untouched, the first tick
            // off centre pans at once, and letting go still stops the map at once.
            UiVec2 computerStick = _touchControls.Stick;
            static bool HoldComputerDirection(bool held, float along) =>
                along > (held ? TouchControls.StickReleaseZone : TouchControls.StickDeadZone);
            _computerStickHeld = (
                HoldComputerDirection(_computerStickHeld.Up, -computerStick.Y),
                HoldComputerDirection(_computerStickHeld.Down, computerStick.Y),
                HoldComputerDirection(_computerStickHeld.Left, -computerStick.X),
                HoldComputerDirection(_computerStickHeld.Right, computerStick.X));
            bool computerPanning = _computerStickHeld.Up || _computerStickHeld.Down || _computerStickHeld.Left || _computerStickHeld.Right;
            _computerStickPulse = computerPanning ? _computerStickPulse + 1 : 0;
            bool computerStep = computerPanning && _computerStickPulse % ComputerStickStepTicks == 1;
            if (computerPanning)
            {
                _touchKeysWanted.Add(KeyCode.LeftControl);
            }

            if (computerStep && _computerStickHeld.Up)
            {
                _touchKeysWanted.Add(KeyCode.Up);
            }

            if (computerStep && _computerStickHeld.Down)
            {
                _touchKeysWanted.Add(KeyCode.Down);
            }

            if (computerStep && _computerStickHeld.Left)
            {
                _touchKeysWanted.Add(KeyCode.Left);
            }

            if (computerStep && _computerStickHeld.Right)
            {
                _touchKeysWanted.Add(KeyCode.Right);
            }
        }

        if (mode == TouchControlMode.Play)
        {
            // Crouch is a toggle: one tap crouches, the next stands up. Flip it on the
            // press (held -> not held transition), not while the button is down, or a
            // long hold would flip it every frame.
            bool crouchHeld = _touchControls.Held.Contains(TouchButton.Crouch);
            if (crouchHeld && !_crouchHeldPrev)
            {
                _crouchToggled = !_crouchToggled;
            }

            _crouchHeldPrev = crouchHeld;
            if (_crouchToggled)
            {
                Want(profile, CrouchAction());
            }

            foreach (TouchButton button in _touchControls.Held)
            {
                if (button == TouchButton.Pause)
                {
                    if (!_menuFlow.PauseMenu
                        && !_computerOpen
                        && !_session.FailedMenu
                        && _mission.Cutscenes?.IsPlaying != true
                        && _mission.MenusEnabled
                        && state.Shell is not null
                        && _session.Mission is not null)
                    {
                        _menuFlow.MenuMode = true;
                        _menuFlow.PauseMenu = true;
                        state.Shell.OpenPauseMenu();
                        HostedPlayer?.HoldStill();
                        _window.CursorLocked = false;
                        _logger.Information("Menu", "Paused via touch button.");
                    }
                }
                else if (button == TouchButton.Crouch)
                {
                    // Handled by the toggle above; do not hold the key while the button is down.
                    continue;
                }
                else if (TouchAction(button) is { } action)
                {
                    Want(profile, action);
                }
            }

            // Each direction is taken past the dead zone and let go only back inside the smaller release zone.
            UiVec2 stick = _touchControls.Stick;
            static bool Hold(bool held, float along) =>
                along > (held ? TouchControls.StickReleaseZone : TouchControls.StickDeadZone);
            _touchStickHeld = (
                Hold(_touchStickHeld.Up, -stick.Y),
                Hold(_touchStickHeld.Down, stick.Y),
                Hold(_touchStickHeld.Left, -stick.X),
                Hold(_touchStickHeld.Right, stick.X));
            if (_touchStickHeld.Up)
            {
                Want(profile, "MoveUp");
            }

            if (_touchStickHeld.Down)
            {
                Want(profile, "MoveDown");
            }

            if (_touchStickHeld.Left)
            {
                Want(profile, "MoveLeft");
            }

            if (_touchStickHeld.Right)
            {
                Want(profile, "MoveRight");
            }

            if (_touchControls.Look is { } look && (look.X != 0f || look.Y != 0f))
            {
                float aimScale = MouseLookSettings.TouchScopeSensitivityScale(TitlePlayer?.IsWeaponScopeActive == true);
                input.Mouse.AddMotion(look.X * aimScale, look.Y * aimScale);
            }

            // Gyroscope aim: tilt the phone to turn the view. Only in Play mode and
            // only when the profile enables it. The window accumulates the gyro's
            // angle deltas (radians) each frame; here they become mouse counts so the
            // existing look path (sensitivity, inversion, zoom scaling) applies.
            // The gyro has its own sensitivity (profile.GyroSensitivity, set by the
            // slider in Settings), separate from the mouse sensitivity.
            // Axis signs are a best guess for landscape and must be verified on a
            // real device (flip yaw/pitch here if a direction feels backwards).
            // The motion is drained every frame even while disabled: the sensor keeps
            // feeding the window, and a stale pile-up would spin the view the moment
            // the toggle is switched on.
            (float gyroYaw, float gyroPitch) = _window.TakeGyroMotion();
            if (profile is { GyroEnabled: true } && (gyroYaw != 0f || gyroPitch != 0f))
            {
                float radiansPerCount = MouseLookSettings.FromSensitivity(
                    profile.GyroSensitivity, profile.InvertMouse).RadiansPerCount;
                float countsPerRadian = 1f / MathF.Max(0.00001f, radiansPerCount);
                input.Mouse.AddMotion(gyroYaw * countsPerRadian, gyroPitch * countsPerRadian);
            }
        }

        if (_touchControls.Pointer is { } pointer)
        {
            input.Mouse.SetPosition(pointer.At.X, pointer.At.Y);
            if (mode == TouchControlMode.Computer && TacticalComputer is null && _mapComputer?.Pointer is { } computer)
            {
                SteerComputerPointer(input, pointer, computer, profile);
            }

            _touchButtonsWanted.Add(MouseButton.Left);
        }

        // Only what the touches pressed is let go: a key the keyboard holds at the same time stays the keyboard's.
        foreach (KeyCode key in _touchKeys)
        {
            if (!_touchKeysWanted.Contains(key))
            {
                input.Keyboard.SetKey(key, down: false);
            }
        }

        foreach (KeyCode key in _touchKeysWanted)
        {
            if (!_touchKeys.Contains(key))
            {
                input.Keyboard.SetKey(key, down: true);
            }
        }

        foreach (MouseButton button in _touchButtons)
        {
            if (!_touchButtonsWanted.Contains(button))
            {
                input.Mouse.SetButton(button, down: false);
            }
        }

        foreach (MouseButton button in _touchButtonsWanted)
        {
            if (!_touchButtons.Contains(button))
            {
                input.Mouse.SetButton(button, down: true);
            }
        }

        _touchKeys.Clear();
        _touchKeys.UnionWith(_touchKeysWanted);
        _touchButtons.Clear();
        _touchButtons.UnionWith(_touchButtonsWanted);
    }

    /// <summary>Moves the original computer's pointer under the finger.</summary>
    /// <remarks>
    /// The computer moves its pointer by the mouse's counts alone (<c>0x46AC30</c>: half its width for 255 counts at the
    /// profile's sensitivity), so the finger's place is turned into the counts that carry the pointer there, less those already
    /// gathered for the next tick. While the map is dragged the pointer holds still and the counts turn the map, so then the
    /// finger's own travel is passed on, as a mouse's would be.
    /// </remarks>
    private void SteerComputerPointer(InputState input, TouchPointer finger, ComputerPointer computer, PlayerProfile? profile)
    {
        float scale = Math.Max(1, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
        float sensitivity = MathF.Max(0.05f, profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity);
        float countsPerPixel = 2f * 255f / (MathF.Max(1f, computer.Width) * sensitivity);

        if (computer.Dragging)
        {
            input.Mouse.AddMotion(finger.Delta.X / scale * countsPerPixel, finger.Delta.Y / scale * countsPerPixel);
            return;
        }

        float wantX = ((finger.At.X / scale) - computer.X) * countsPerPixel;
        float wantY = ((finger.At.Y / scale) - computer.Y) * countsPerPixel;
        input.Mouse.AddMotion(wantX - _computerCountsX, wantY - _computerCountsY);
    }

    private void Want(PlayerProfile? profile, string action)
    {
        if (PlayerProfileInputBindings.TryBoundControl(profile, action, out KeyCode key, out MouseButton? button))
        {
            if (button is { } mouse)
            {
                _touchButtonsWanted.Add(mouse);
            }
            else
            {
                _touchKeysWanted.Add(key);
            }

            return;
        }

        // A title action whose saved row is unbound still works from the title's own
        // default key. RestoreMissing only adds rows that are missing entirely, so a
        // profile saved before a default key existed keeps the old unbound row — the
        // prone button would be dead on it until a controls reset. This is the same
        // repair the shared bindings get on load (RestoreMissingDefaultBindings), just
        // for title actions, and only for touch: a key the player deliberately unbound
        // cannot happen here, as there is no rebind UI. The proper fix is to repair
        // the row itself in RestoreMissing; until then this keeps new buttons live.
        if (Gameplay?.InputBindings is { } bindings)
        {
            foreach (TitleInputBinding binding in bindings)
            {
                if (!string.Equals(binding.Default.Action, action, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(binding.Default.Device, InputBindingKeyNames.KeyboardDevice, StringComparison.OrdinalIgnoreCase)
                    && InputBindingKeyNames.TryParseKeyboardKey(binding.Default.Key, out KeyCode fallbackKey))
                {
                    _touchKeysWanted.Add(fallbackKey);
                }
                else if (string.Equals(binding.Default.Device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase)
                    && InputBindingKeyNames.TryParseMouseButton(binding.Default.Key, out MouseButton fallbackButton))
                {
                    _touchButtonsWanted.Add(fallbackButton);
                }

                return;
            }
        }
    }

    /// <summary>Draws the controls over play, in the title's remastered look.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch they draw into.</param>
    private void DrawTouchControls(FrameLoopState state)
    {
        if ((!_window.Input.Touch.InUse && !TouchControls.IsCustomizing) || _touchControls.Mode is not (TouchControlMode.Play or TouchControlMode.Computer or TouchControlMode.Customizing))
        {
            return;
        }

        ITouchHudSource? source = _hudOverlay as ITouchHudSource ?? _titleHud as ITouchHudSource;
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        TouchControlsRenderer.Draw(
            state.Batch,
            new LayoutContext(_window.Width, _window.Height),
            _touchControls,
            source?.Theme,
            source?.Icons,
            source?.Model,
            _shellType?.Heading);
        state.Batch.End();
    }
}
