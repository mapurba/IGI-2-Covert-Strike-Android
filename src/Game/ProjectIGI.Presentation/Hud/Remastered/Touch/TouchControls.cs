using System.Globalization;
using ProjectIGI.Core.Configuration;
using ProjectIGI.Input;
using ProjectIGI.UI;
using ProjectIGI.UI.Layout;

namespace ProjectIGI.Presentation.Hud.Remastered.Touch;

/// <summary>What the game is showing, as a touch screen's controls need to know it.</summary>
public enum TouchControlMode
{
    /// <summary>Nothing to control: a load, say.</summary>
    None,

    /// <summary>A menu, the computer or anything else with a pointer: a finger is the pointer.</summary>
    Pointer,

    /// <summary>Play: the stick, the look and the buttons.</summary>
    Play,

    /// <summary>A cutscene: a finger held anywhere skips it, which the host reads from the touches themselves.</summary>
    Cutscene,

    /// <summary>The map computer: a finger is its pointer, zoom buttons and a navigation stick sit at the top centre, and a button in the corner closes it.</summary>
    Computer,

    /// <summary>Interactive HUD and touch layout customization mode.</summary>
    Customizing,
}

/// <summary>Unique identifier for every on-screen customizable control element.</summary>
public enum TouchElementId
{
    /// <summary>Movement virtual joystick.</summary>
    MovementStick,

    /// <summary>Primary right-hand fire button.</summary>
    FireRight,

    /// <summary>Secondary left-hand fire button.</summary>
    FireLeft,

    /// <summary>Alternate fire / sniper scope button.</summary>
    Scope,

    /// <summary>Reload weapon button.</summary>
    Reload,

    /// <summary>Jump button.</summary>
    Jump,

    /// <summary>Crouch toggle button.</summary>
    Crouch,

    /// <summary>Step the stance down (I.G.I. 2).</summary>
    StanceDown,

    /// <summary>Step the stance up (I.G.I. 2).</summary>
    StanceUp,

    /// <summary>Toggle walk/run (I.G.I. 2).</summary>
    WalkRun,

    /// <summary>Toggle stand/prone (I.G.I. 2).</summary>
    Prone,

    /// <summary>Previous weapon.</summary>
    PrevWeapon,

    /// <summary>Hold the weapon wheel open.</summary>
    WeaponWheel,

    /// <summary>Drop the held weapon (I.G.I. 2).</summary>
    DropWeapon,

    /// <summary>Raise the thermal sight (I.G.I. 2).</summary>
    Thermal,

    /// <summary>Hold to walk (I.G.I. 2).</summary>
    WalkHold,

    /// <summary>Lean / peek button.</summary>
    Peek,

    /// <summary>Optics zoom in button.</summary>
    ZoomIn,

    /// <summary>Optics zoom out button.</summary>
    ZoomOut,

    /// <summary>Equipped weapon card.</summary>
    WeaponCard,

    /// <summary>Contextual action / interact button.</summary>
    Use,

    /// <summary>Pause menu button.</summary>
    Pause,

    /// <summary>Tactical map computer button.</summary>
    Map,

    /// <summary>Binoculars button.</summary>
    Binoculars,
}

/// <summary>A button on the screen in play, and the player's action it holds.</summary>
public enum TouchButton
{
    /// <summary>Fire; dragged, it turns the view, so a thumb aims and shoots at once.</summary>
    Fire,

    /// <summary>The weapon's second use: the Dragunov's and the MP5's sights.</summary>
    Scope,

    /// <summary>Reload.</summary>
    Reload,

    /// <summary>Jump.</summary>
    Jump,

    /// <summary>Crouch.</summary>
    Crouch,

    /// <summary>Step the stance down: stand to crouch, crouch to prone (I.G.I. 2).</summary>
    LowerStance,

    /// <summary>Step the stance up: prone to crouch, crouch to stand (I.G.I. 2).</summary>
    RaiseStance,

    /// <summary>Toggle walk/run (I.G.I. 2).</summary>
    WalkRun,

    /// <summary>Toggle stand/prone (I.G.I. 2).</summary>
    Prone,

    /// <summary>The previous weapon.</summary>
    PrevWeapon,

    /// <summary>Hold the weapon wheel open to pick a weapon.</summary>
    WeaponWheel,

    /// <summary>Drop the held weapon (I.G.I. 2).</summary>
    DropWeapon,

    /// <summary>Raise the thermal sight (I.G.I. 2).</summary>
    Thermal,

    /// <summary>Hold to walk instead of run (I.G.I. 2).</summary>
    WalkHold,

    /// <summary>Use: a door, a ladder, a switch.</summary>
    Use,

    /// <summary>The next weapon.</summary>
    Weapon,

    /// <summary>Lean out.</summary>
    Peek,

    /// <summary>The pause menu.</summary>
    Pause,

    /// <summary>The map computer.</summary>
    Map,

    /// <summary>The binoculars.</summary>
    Binoculars,

    /// <summary>Zoom in, through the binoculars or a sight.</summary>
    ZoomIn,

    /// <summary>Zoom out.</summary>
    ZoomOut,

    /// <summary>Closes the map computer.</summary>
    Close,
}

/// <summary>The finger that is the pointer: where it is, how far it moved this frame, and whether it came down this frame.</summary>
/// <param name="At">Where it is, in window pixels.</param>
/// <param name="Delta">How far it moved this frame, in window pixels.</param>
/// <param name="Began">Whether it came down this frame.</param>
public readonly record struct TouchPointer(UiVec2 At, UiVec2 Delta, bool Began);

/// <summary>Where a button sits and how big it is: a round one by its centre and radius, the weapon's card by its box.</summary>
/// <param name="Button">The button.</param>
/// <param name="Centre">Its centre, in window pixels.</param>
/// <param name="Radius">Its radius, or half the card's height, in window pixels.</param>
/// <param name="ElementId">The unique element identifier.</param>
/// <param name="Card">Whether it is a wide card rather than a round button.</param>
/// <param name="Aims">Whether dragging it also turns the view: the right-hand fire button, so one thumb aims and shoots.</param>
public readonly record struct TouchButtonSpot(
    TouchButton Button,
    UiVec2 Centre,
    float Radius,
    TouchElementId ElementId = TouchElementId.FireRight,
    bool Card = false,
    bool Aims = false)
{
    /// <summary>Gets a card's box.</summary>
    public UiRect Box => new(Centre.X - (Radius * 2.4f), Centre.Y - Radius, Radius * 4.8f, Radius * 2f);

    /// <summary>Whether a finger landing at a point presses it: a little generous, as a thumb is not a pointer.</summary>
    public bool Hit(float x, float y)
    {
        if (Card)
        {
            UiRect box = Box;
            float slack = Radius * 0.25f;
            return x >= box.X - slack && x <= box.Right + slack && y >= box.Y - slack && y <= box.Bottom + slack;
        }

        float dx = x - Centre.X, dy = y - Centre.Y;
        float reach = Radius * 1.15f;
        return (dx * dx) + (dy * dy) <= reach * reach;
    }
}

/// <summary>Persisted positioning and scaling overrides for an on-screen control.</summary>
public sealed class TouchElementConfig
{
    /// <summary>Gets or sets the horizontal position from 0 to 1 across the screen.</summary>
    public float NormalizedX { get; set; }

    /// <summary>Gets or sets the vertical position from 0 to 1 down the screen.</summary>
    public float NormalizedY { get; set; }

    /// <summary>Gets or sets the scale factor multiplier.</summary>
    public float Scale { get; set; } = 1.0f;
}

/// <summary>A touch screen's controls: the fingers on it read as a stick, a look, buttons, or a pointer, frame by frame.</summary>
public sealed class TouchControls
{
    /// <summary>How far a drag turns the view: the mouse's counts per back-buffer pixel dragged.</summary>
    public const float LookCountsPerPixel = 4.5f;

    /// <summary>The share of its reach the stick moves before it holds a direction.</summary>
    public const float StickDeadZone = 0.35f;

    /// <summary>The share of its reach the stick comes back under before it lets a direction go.</summary>
    public const float StickReleaseZone = 0.22f;

    /// <summary>The stick's reach, in design units.</summary>
    public const float StickReach = 120f;

    private const float FireRadius = 80f;
    private const float ButtonRadius = 62f;
    private const float SmallRadius = 50f;
    private const float CornerRadius = 36f;

    private static readonly Dictionary<TouchElementId, TouchElementConfig> s_configs = [];
    private static bool s_loaded;
    private static string LayoutFilePath => SettingsPaths.GetFilePath("touch_layout.json");

    /// <summary>Gets or sets whether the interactive on-screen HUD layout editor is currently open.</summary>
    public static bool IsCustomizing { get; set; }

    /// <summary>Gets or sets the element currently selected in customization mode.</summary>
    public static TouchElementId? SelectedElement { get; set; }

    /// <summary>Gets or sets the overall opacity of on-screen touch controls (from 0.2 to 1.0).</summary>
    public static float GlobalOpacity { get; set; } = 0.75f;

    /// <summary>Gets or sets the general touch sensitivity multiplier for swipe look and stick movement (from 0.2 to 3.0, default 1.0).</summary>
    public static float TouchSensitivity { get; set; } = 1.0f;

    /// <summary>Gets or sets whether the I.G.I. 2-only buttons (stance, prone, walk/run, hold-to-walk, drop, thermal) are laid out at all. The host sets it from the running title; under a title that defines none of them they stay out of the layout entirely instead of sitting there dead.</summary>
    public bool ShowIgi2Buttons { get; set; } = true;

    private readonly Dictionary<int, Role> _roles = [];
    private readonly List<TouchButtonSpot> _layout = [];
    private readonly List<TouchButtonSpot> _computerLayout = [];
    private readonly HashSet<TouchButton> _held = [];
    private (float Width, float Height) _laidOutFor;
    private bool _laidOutIgi2;
    private TouchControlMode _mode;
    private UiVec2 _lastLook;

    private int? _dragFingerId;
    private TouchElementId? _draggedElement;
    private float _lastPinchDistance;

    private enum RoleKind
    {
        Stick,
        Look,
        Button,
        Pointer,
        Ignored,
    }

    /// <summary>Gets the buttons on the screen in the current mode.</summary>
    public IReadOnlyList<TouchButtonSpot> Layout => _mode == TouchControlMode.Computer ? _computerLayout : _layout;

    /// <summary>Gets the buttons held this frame.</summary>
    public IReadOnlySet<TouchButton> Held => _held;

    /// <summary>Gets where the stick points.</summary>
    public UiVec2 Stick { get; private set; }

    /// <summary>Gets where the stick's thumb came down.</summary>
    public UiVec2? StickOrigin { get; private set; }

    /// <summary>Gets where the stick rests when no thumb is on it.</summary>
    public UiVec2 StickRest { get; private set; }

    /// <summary>Gets the stick's reach in back-buffer pixels.</summary>
    public float StickReachPixels { get; private set; }

    /// <summary>Gets where the map computer's navigation stick rests, at the top centre of the screen.</summary>
    public UiVec2 ComputerStickRest { get; private set; }

    /// <summary>Gets the map computer's navigation stick's reach in back-buffer pixels.</summary>
    public float ComputerStickReachPixels { get; private set; }

    /// <summary>Gets how far the view is to turn this frame.</summary>
    public UiVec2 Look { get; private set; }

    /// <summary>Gets the finger that is the pointer.</summary>
    public TouchPointer? Pointer { get; private set; }

    /// <summary>Gets the mode the controls were last read in.</summary>
    public TouchControlMode Mode => _mode;

    /// <summary>Loads layout overrides from the persisted configuration file.</summary>
    public static void EnsureLoaded()
    {
        if (s_loaded)
        {
            return;
        }

        s_loaded = true;
        try
        {
            if (File.Exists(LayoutFilePath))
            {
                string[] lines = File.ReadAllLines(LayoutFilePath);
                foreach (string line in lines)
                {
                    string[] parts = line.Split(':');
                    if (parts.Length == 2 && parts[0] == "Opacity" && float.TryParse(parts[1], CultureInfo.InvariantCulture, out float op))
                    {
                        GlobalOpacity = op;
                    }
                    else if (parts.Length == 2 && parts[0] == "Sensitivity" && float.TryParse(parts[1], CultureInfo.InvariantCulture, out float sens))
                    {
                        TouchSensitivity = Math.Clamp(sens, 0.2f, 3.0f);
                    }
                    else if (parts.Length == 4 && Enum.TryParse<TouchElementId>(parts[0], out var id))
                    {
                        if (float.TryParse(parts[1], CultureInfo.InvariantCulture, out float nx) &&
                            float.TryParse(parts[2], CultureInfo.InvariantCulture, out float ny) &&
                            float.TryParse(parts[3], CultureInfo.InvariantCulture, out float sc))
                        {
                            s_configs[id] = new TouchElementConfig
                            {
                                NormalizedX = nx,
                                NormalizedY = ny,
                                Scale = sc,
                            };
                        }
                    }
                }
            }
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>Saves all active layout positions, scaling, and opacity to persistent storage.</summary>
    public static void SaveLayout()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Opacity:{GlobalOpacity:0.00}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Sensitivity:{TouchSensitivity:0.00}");
            foreach (var kvp in s_configs)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{kvp.Key}:{kvp.Value.NormalizedX:0.0000}:{kvp.Value.NormalizedY:0.0000}:{kvp.Value.Scale:0.00}");
            }
            string path = LayoutFilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>Resets all on-screen control positions, scaling, and opacity to defaults.</summary>
    public static void ResetLayout()
    {
        s_configs.Clear();
        GlobalOpacity = 0.75f;
        TouchSensitivity = 1.0f;
        SelectedElement = null;
        try
        {
            if (File.Exists(LayoutFilePath))
            {
                File.Delete(LayoutFilePath);
            }
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>Reads a frame's fingers.</summary>
    public void Update(TouchState touch, TouchControlMode mode, LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(touch);
        ArgumentNullException.ThrowIfNull(context);

        if ((context.Viewport.X, context.Viewport.Y) != _laidOutFor || _laidOutIgi2 != ShowIgi2Buttons)
        {
            LayOut(context);
        }

        if (IsCustomizing)
        {
            _mode = TouchControlMode.Customizing;
            _held.Clear();
            Stick = default;
            StickOrigin = null;
            Look = default;
            Pointer = null;
            UpdateCustomization(touch, context);
            return;
        }

        if (mode != _mode)
        {
            _mode = mode;
            _lastLook = default;
            _roles.Clear();
            foreach (TouchPoint point in touch.Points)
            {
                _roles[point.Id] = new Role(RoleKind.Ignored, null);
            }
        }

        List<int>? gone = null;
        foreach (int id in _roles.Keys)
        {
            if (!Contains(touch, id))
            {
                (gone ??= []).Add(id);
            }
        }

        if (gone is not null)
        {
            foreach (int id in gone)
            {
                _roles.Remove(id);
            }
        }

        _held.Clear();
        UiVec2 look = default;
        UiVec2 stick = default;
        UiVec2? stickOrigin = null;
        TouchPointer? pointer = null;

        foreach (TouchPoint point in touch.Points)
        {
            if (!_roles.TryGetValue(point.Id, out Role role))
            {
                role = Assign(point, mode, context);
                _roles[point.Id] = role;
            }

            switch (role.Kind)
            {
                case RoleKind.Stick:
                    // The map computer's stick is fixed at the top centre: its thumb starts from there,
                    // not from where the finger came down, so the knob tracks the visible base.
                    bool computerStick = mode == TouchControlMode.Computer;
                    UiVec2 origin = computerStick ? ComputerStickRest : new(point.StartX, point.StartY);
                    float dx = point.X - origin.X, dy = point.Y - origin.Y;
                    float length = MathF.Sqrt((dx * dx) + (dy * dy));
                    float reach = (computerStick ? ComputerStickReachPixels : StickReachPixels) / Math.Clamp(TouchSensitivity, 0.5f, 2.0f);
                    float scale = length > reach ? reach / length : 1f;
                    stick = new UiVec2(dx * scale / reach, dy * scale / reach);
                    stickOrigin = origin;
                    break;
                case RoleKind.Look:
                    look += new UiVec2(point.DeltaX, point.DeltaY);
                    break;
                case RoleKind.Button when role.Button is { } button:
                    _held.Add(button);
                    if (role.Aims)
                    {
                        look += new UiVec2(point.DeltaX, point.DeltaY);
                    }
                    break;
                case RoleKind.Pointer:
                    pointer = new TouchPointer(new UiVec2(point.X, point.Y), new UiVec2(point.DeltaX, point.DeltaY), point.Began);
                    break;
            }
        }

        Stick = stick;
        StickOrigin = stickOrigin;

        UiVec2 smoothed = new((look.X + _lastLook.X) * 0.5f, (look.Y + _lastLook.Y) * 0.5f);
        _lastLook = look;
        Look = new UiVec2(smoothed.X * LookCountsPerPixel * TouchSensitivity, smoothed.Y * LookCountsPerPixel * TouchSensitivity);
        Pointer = pointer;
    }

    private void UpdateCustomization(TouchState touch, LayoutContext context)
    {
        float Px(float d) => context.Px(d);
        UiRect safe = context.SafeRect;

        float topY = safe.Y + Px(10f);
        float btnH = Px(60f);
        UiRect saveBox    = new(safe.Right - Px(230f), topY, Px(220f), btnH);
        UiRect resetBox   = new(safe.Right - Px(430f), topY, Px(190f), btnH);
        UiRect sizePlus   = new(safe.Right - Px(540f), topY, Px(100f), btnH);
        UiRect sizeMinus  = new(safe.Right - Px(650f), topY, Px(100f), btnH);
        UiRect opacityBox = new(safe.Right - Px(840f), topY, Px(180f), btnH);

        // Multi-touch pinch-to-zoom for real-time resizing of controls
        if (touch.Points.Count >= 2)
        {
            TouchPoint p0 = touch.Points[0];
            TouchPoint p1 = touch.Points[1];
            if (!p0.Ended && !p1.Ended)
            {
                float dx = p1.X - p0.X;
                float dy = p1.Y - p0.Y;
                float currentDist = MathF.Sqrt((dx * dx) + (dy * dy));

                if (_lastPinchDistance > 10f && currentDist > 10f)
                {
                    float ratio = currentDist / _lastPinchDistance;
                    if (MathF.Abs(ratio - 1.0f) > 0.005f)
                    {
                        TouchElementId target = SelectedElement ?? _draggedElement ?? TouchElementId.MovementStick;
                        if (!s_configs.TryGetValue(target, out var cfg))
                        {
                            cfg = new TouchElementConfig { Scale = 1.0f };
                            s_configs[target] = cfg;
                        }
                        cfg.Scale = Math.Clamp(cfg.Scale * ratio, 0.4f, 2.5f);
                        LayOut(context);
                    }
                }

                _lastPinchDistance = currentDist;
                _dragFingerId = null; // Suppress single-finger drag while pinching
            }
            else
            {
                _lastPinchDistance = 0f;
            }
        }
        else
        {
            _lastPinchDistance = 0f;
        }

        bool hasDragFinger = false;
        foreach (TouchPoint point in touch.Points)
        {
            if (point.Id == _dragFingerId)
            {
                hasDragFinger = true;
                if (_draggedElement is { } dragged)
                {
                    float nx = Math.Clamp(point.X / context.Viewport.X, 0.05f, 0.95f);
                    float ny = Math.Clamp(point.Y / context.Viewport.Y, 0.05f, 0.95f);
                    if (!s_configs.TryGetValue(dragged, out var cfg))
                    {
                        cfg = new TouchElementConfig { Scale = 1.0f };
                        s_configs[dragged] = cfg;
                    }
                    cfg.NormalizedX = nx;
                    cfg.NormalizedY = ny;
                    LayOut(context);
                }
            }

            if (point.Began)
            {
                if (saveBox.Contains(point.X, point.Y))
                {
                    SaveLayout();
                    IsCustomizing = false;
                    _dragFingerId = null;
                    _draggedElement = null;
                    return;
                }
                if (resetBox.Contains(point.X, point.Y))
                {
                    ResetLayout();
                    LayOut(context);
                    return;
                }
                if (sizePlus.Contains(point.X, point.Y))
                {
                    TouchElementId target = SelectedElement ?? TouchElementId.FireRight;
                    if (!s_configs.TryGetValue(target, out var cfg))
                    {
                        cfg = new TouchElementConfig { Scale = 1.0f };
                        s_configs[target] = cfg;
                    }
                    cfg.Scale = Math.Clamp(cfg.Scale + 0.1f, 0.5f, 2.0f);
                    LayOut(context);
                    return;
                }
                if (sizeMinus.Contains(point.X, point.Y))
                {
                    TouchElementId target = SelectedElement ?? TouchElementId.FireRight;
                    if (!s_configs.TryGetValue(target, out var cfg))
                    {
                        cfg = new TouchElementConfig { Scale = 1.0f };
                        s_configs[target] = cfg;
                    }
                    cfg.Scale = Math.Clamp(cfg.Scale - 0.1f, 0.5f, 2.0f);
                    LayOut(context);
                    return;
                }
                if (opacityBox.Contains(point.X, point.Y))
                {
                    GlobalOpacity = GlobalOpacity switch
                    {
                        >= 0.90f => 0.70f,
                        >= 0.65f => 0.45f,
                        >= 0.40f => 0.25f,
                        _ => 1.0f
                    };
                    return;
                }

                bool hitButton = false;
                foreach (TouchButtonSpot spot in _layout)
                {
                    if (spot.Hit(point.X, point.Y))
                    {
                        SelectedElement = spot.ElementId;
                        _draggedElement = spot.ElementId;
                        _dragFingerId = point.Id;
                        hasDragFinger = true;
                        hitButton = true;
                        break;
                    }
                }

                if (!hitButton)
                {
                    float dx = point.X - StickRest.X, dy = point.Y - StickRest.Y;
                    if ((dx * dx) + (dy * dy) <= StickReachPixels * StickReachPixels * 1.5f)
                    {
                        SelectedElement = TouchElementId.MovementStick;
                        _draggedElement = TouchElementId.MovementStick;
                        _dragFingerId = point.Id;
                        hasDragFinger = true;
                    }
                }
            }
        }

        if (!hasDragFinger)
        {
            _dragFingerId = null;
            _draggedElement = null;
        }
    }

    private Role Assign(in TouchPoint point, TouchControlMode mode, LayoutContext context)
    {
        if (mode == TouchControlMode.Computer)
        {
            // The navigation stick is fixed at the top centre: a finger coming down on it steers
            // the map, and does not become the pointer — the pointer is a second finger.
            float computerDx = point.StartX - ComputerStickRest.X, computerDy = point.StartY - ComputerStickRest.Y;
            if ((computerDx * computerDx) + (computerDy * computerDy) <= (ComputerStickReachPixels * 1.5f) * (ComputerStickReachPixels * 1.5f))
            {
                return new Role(RoleKind.Stick, null);
            }

            if (Nearest(point.StartX, point.StartY, _computerLayout) is { } computerSpot)
            {
                return new Role(RoleKind.Button, computerSpot.Button);
            }
        }

        if (mode is TouchControlMode.Pointer or TouchControlMode.Computer)
        {
            foreach (Role other in _roles.Values)
            {
                if (other.Kind == RoleKind.Pointer)
                {
                    return new Role(RoleKind.Ignored, null);
                }
            }

            return new Role(RoleKind.Pointer, null);
        }

        if (mode != TouchControlMode.Play)
        {
            return new Role(RoleKind.Ignored, null);
        }

        if (Nearest(point.StartX, point.StartY, _layout) is { } spot)
        {
            return new Role(RoleKind.Button, spot.Button, spot.Aims);
        }

        float sdx = point.StartX - StickRest.X, sdy = point.StartY - StickRest.Y;
        bool nearStick = ((sdx * sdx) + (sdy * sdy)) <= (StickReachPixels * 2.8f) * (StickReachPixels * 2.8f);
        bool stickHalf = nearStick || (point.StartX < context.Viewport.X * 0.42f && point.StartY > context.Viewport.Y * 0.3f);
        bool stickFree = true;
        foreach (Role other in _roles.Values)
        {
            stickFree &= other.Kind != RoleKind.Stick;
        }

        return new Role(stickHalf && stickFree ? RoleKind.Stick : RoleKind.Look, null);
    }

    /// <summary>The spot a finger came down on, where several overlap: the nearest centre wins, not the first in the layout.</summary>
    /// <remarks>
    /// The spots' hit circles are generous (radius × 1.15), and a saved custom layout can put two of
    /// them over each other — the weapon-switch button is laid out before previous-weapon, so a first-match
    /// tap on the previous arrow fell through to next-weapon. Nearest-match makes the tap land on the
    /// button the finger is actually on; a tap outside every circle still finds nothing.
    /// </remarks>
    private static TouchButtonSpot? Nearest(float x, float y, IReadOnlyList<TouchButtonSpot> spots)
    {
        TouchButtonSpot? best = null;
        float bestScore = 1f;
        foreach (TouchButtonSpot spot in spots)
        {
            float score;
            if (spot.Card)
            {
                if (!spot.Hit(x, y)) { continue; }
                score = 0.5f;
            }
            else
            {
                float dx = x - spot.Centre.X, dy = y - spot.Centre.Y;
                float reach = spot.Radius * 1.15f;
                score = MathF.Sqrt((dx * dx) + (dy * dy)) / reach;
                if (score > 1f) { continue; }
            }

            if (best is null || score < bestScore)
            {
                best = spot;
                bestScore = score;
            }
        }

        return best;
    }

    private void LayOut(LayoutContext context)
    {
        EnsureLoaded();
        _laidOutFor = (context.Viewport.X, context.Viewport.Y);
        _laidOutIgi2 = ShowIgi2Buttons;
        _layout.Clear();
        UiRect safe = context.SafeRect;
        // Touch buttons keep a fixed physical size: they must NOT grow or shrink with the
        // menu UI-size setting (GlobalUiScale), or Reset in the customiser would restore a
        // different button size for every UI size. Divide the UI-size multiplier back out
        // so the layout uses only the display's reference scale.
        float touchScale = context.Scale / MathF.Max(0.5f, LayoutContext.GlobalUiScale);
        float Px(float design) => design * touchScale;

        UiVec2 ResolvePos(TouchElementId id, UiVec2 defaultPos)
        {
            if (s_configs.TryGetValue(id, out var cfg) && cfg.NormalizedX > 0f && cfg.NormalizedY > 0f)
            {
                return new UiVec2(cfg.NormalizedX * context.Viewport.X, cfg.NormalizedY * context.Viewport.Y);
            }
            s_configs[id] = new TouchElementConfig
            {
                NormalizedX = defaultPos.X / context.Viewport.X,
                NormalizedY = defaultPos.Y / context.Viewport.Y,
                Scale = 1.0f,
            };
            return defaultPos;
        }

        float Scale(TouchElementId id) => s_configs.TryGetValue(id, out var cfg) ? Math.Clamp(cfg.Scale, 0.5f, 2.0f) : 1.0f;

        // Stick
        UiVec2 defaultStick = new(safe.X + Px(250f), safe.Bottom - Px(230f));
        StickRest = ResolvePos(TouchElementId.MovementStick, defaultStick);
        StickReachPixels = Px(StickReach) * Scale(TouchElementId.MovementStick);

        // Buttons
        void AddRound(TouchElementId id, TouchButton button, float right, float bottom, float radius, bool aims = false)
        {
            UiVec2 def = new(safe.Right - Px(right), safe.Bottom - Px(bottom));
            UiVec2 pos = ResolvePos(id, def);
            _layout.Add(new TouchButtonSpot(button, pos, Px(radius) * Scale(id), ElementId: id, Aims: aims));
        }

        AddRound(TouchElementId.FireRight, TouchButton.Fire, 300f, 230f, FireRadius, aims: true);
        AddRound(TouchElementId.Scope, TouchButton.Scope, 470f, 400f, ButtonRadius);
        AddRound(TouchElementId.Reload, TouchButton.Reload, 505f, 190f, ButtonRadius);
        AddRound(TouchElementId.Jump, TouchButton.Jump, 300f, 430f, ButtonRadius);
        AddRound(TouchElementId.Crouch, TouchButton.Crouch, 110f, 170f, SmallRadius);
        AddRound(TouchElementId.Peek, TouchButton.Peek, 110f, 315f, SmallRadius);
        AddRound(TouchElementId.ZoomIn, TouchButton.ZoomIn, 110f, 600f, CornerRadius);
        AddRound(TouchElementId.ZoomOut, TouchButton.ZoomOut, 110f, 495f, CornerRadius);

        // I.G.I. 2 stance buttons: a pair to the right of the movement stick,
        // clear of its reach. Tap to step the stance one level up or down;
        // prone is reached by stepping down from crouch, not by its own button.
        // The pair is I.G.I. 2 only and stays out of the layout under any other title.
        if (ShowIgi2Buttons)
        {
            _layout.Add(new TouchButtonSpot(TouchButton.RaiseStance, ResolvePos(TouchElementId.StanceUp, new UiVec2(safe.X + Px(480f), safe.Bottom - Px(400f))), Px(SmallRadius) * Scale(TouchElementId.StanceUp), ElementId: TouchElementId.StanceUp));
            _layout.Add(new TouchButtonSpot(TouchButton.LowerStance, ResolvePos(TouchElementId.StanceDown, new UiVec2(safe.X + Px(480f), safe.Bottom - Px(280f))), Px(SmallRadius) * Scale(TouchElementId.StanceDown), ElementId: TouchElementId.StanceDown));
        }

        // Weapon (round, not a wide card: the wide rectangle was awkward to tap) and Use.
        // The current weapon and ammo stay visible in the HUD's own weapon widget.
        UiVec2 defWeapon = new(safe.X + (safe.Width * 0.5f), safe.Bottom - Px(90f));
        UiVec2 defUse = new(safe.X + (safe.Width * 0.5f), safe.Bottom - Px(250f));
        _layout.Add(new TouchButtonSpot(TouchButton.Weapon, ResolvePos(TouchElementId.WeaponCard, defWeapon), Px(ButtonRadius) * Scale(TouchElementId.WeaponCard), ElementId: TouchElementId.WeaponCard));
        _layout.Add(new TouchButtonSpot(TouchButton.Use, ResolvePos(TouchElementId.Use, defUse), Px(SmallRadius) * Scale(TouchElementId.Use), ElementId: TouchElementId.Use));

        // Drop sits beside the weapon button. The wheel is not laid out: it only
        // opens and draws under the remastered HUD, which Android runs with off,
        // so its button would sit there dead — it comes back with a standalone
        // wheel draw. Previous weapon has no button (keyboard O still steps back).
        if (ShowIgi2Buttons)
        {
            _layout.Add(new TouchButtonSpot(TouchButton.DropWeapon, ResolvePos(TouchElementId.DropWeapon, new UiVec2(safe.X + (safe.Width * 0.5f) + Px(170f), safe.Bottom - Px(90f))), Px(SmallRadius) * Scale(TouchElementId.DropWeapon), ElementId: TouchElementId.DropWeapon));
        }

        // Thermal sight, on the right above the scope button (I.G.I. 2 only).
        if (ShowIgi2Buttons)
        {
            _layout.Add(new TouchButtonSpot(TouchButton.Thermal, ResolvePos(TouchElementId.Thermal, new UiVec2(safe.Right - Px(470f), safe.Bottom - Px(540f))), Px(SmallRadius) * Scale(TouchElementId.Thermal), ElementId: TouchElementId.Thermal));
        }

        // While the computer is open, only its own controls show: the navigation
        // stick and zoom at the top centre, with Pause and the Map button at the
        // top-right corner. Every other button is hidden, because the layout in
        // Computer mode is this list alone. Map is the computer's own toggle, so
        // it closes the computer; Pause opens the pause menu. Other titles keep
        // their Close button, which does the same job as Map.
        _computerLayout.Clear();
        if (ShowIgi2Buttons)
        {
            _computerLayout.Add(new TouchButtonSpot(TouchButton.Pause, new UiVec2(safe.Right - Px(80f), safe.Y + Px(80f)), Px(CornerRadius), ElementId: TouchElementId.Pause));
            _computerLayout.Add(new TouchButtonSpot(TouchButton.Map, new UiVec2(safe.Right - Px(80f) - Px(CornerRadius * 2.6f), safe.Y + Px(80f)), Px(CornerRadius), ElementId: TouchElementId.Map));
        }
        else
        {
            _computerLayout.Add(new TouchButtonSpot(TouchButton.Close, new UiVec2(safe.Right - Px(90f), safe.Y + Px(90f)), Px(ButtonRadius)));
        }

        // The map computer's own controls, at the very top centre, only while it is open:
        // zoom out and in flanking a fixed navigation stick, which pans the map. The stick is
        // a little smaller than the movement stick; the zoom buttons sit clear of its reach.
        ComputerStickRest = new UiVec2(safe.X + (safe.Width * 0.5f), safe.Y + Px(110f));
        ComputerStickReachPixels = Px(110f);
        _computerLayout.Add(new TouchButtonSpot(TouchButton.ZoomOut, new UiVec2(ComputerStickRest.X - Px(300f), ComputerStickRest.Y), Px(CornerRadius), ElementId: TouchElementId.ZoomOut));
        _computerLayout.Add(new TouchButtonSpot(TouchButton.ZoomIn, new UiVec2(ComputerStickRest.X + Px(300f), ComputerStickRest.Y), Px(CornerRadius), ElementId: TouchElementId.ZoomIn));

        // Both binoculars and thermal are separate tools in I.G.I. 2.
        float y = safe.Y + Px(80f);
        TouchElementId[] cornerIds = ShowIgi2Buttons
            ? [TouchElementId.Pause, TouchElementId.Map, TouchElementId.WalkRun, TouchElementId.Binoculars]
            : [TouchElementId.Pause, TouchElementId.Map, TouchElementId.Binoculars];
        TouchButton[] cornerBtns = ShowIgi2Buttons
            ? [TouchButton.Pause, TouchButton.Map, TouchButton.WalkRun, TouchButton.Binoculars]
            : [TouchButton.Pause, TouchButton.Map, TouchButton.Binoculars];
        for (int i = 0; i < cornerBtns.Length; i++)
        {
            UiVec2 defCorner = new(safe.Right - Px(80f) - (i * Px(CornerRadius * 2.6f)), y);
            _layout.Add(new TouchButtonSpot(cornerBtns[i], ResolvePos(cornerIds[i], defCorner), Px(CornerRadius) * Scale(cornerIds[i]), ElementId: cornerIds[i]));
        }
    }

    private static bool Contains(TouchState touch, int id)
    {
        foreach (TouchPoint point in touch.Points)
        {
            if (point.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Role(RoleKind Kind, TouchButton? Button, bool Aims = false);
}
