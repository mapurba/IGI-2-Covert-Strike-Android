using System.Globalization;
using ProjectIGI.Core.Graphics;
using ProjectIGI.Game.Player;
using ProjectIGI.Input;
using ProjectIGI.Rendering;
using ProjectIGI.UI;
using ProjectIGI.UI.Focus;
using ProjectIGI.UI.Kit.Widgets;
using ProjectIGI.UI.Layout;
using ProjectIGI.UI.Widgets;

namespace ProjectIGI.Frontend.Screens;

/// <summary>
/// Authentic Project I.G.I. 1 "Configuration" screen:
/// Stripped of all unnecessary modifications, providing ONLY the requested options:
/// 1. Touch controls customization (drag-and-drop position, pinch-and-zoom resize, size +/-)
/// 2. Controls alpha slider
/// 3. Max frame rate (FPS cap: 30, 60, 90, 120, Uncapped)
/// 4. Invert mouse/look pitch toggle
/// 5. Graphics: resolution, frame rate, sun, smooth motion, FPS counter, texture sharpness and
///    quality, mip blending, muzzle flash, weather and storm — every graphics option that actually
///    changes the picture on the Android (vanilla) path. Impact effects and particles are not here:
///    both are I.G.I. 1-only systems with no I.G.I. 2 input, so on this title they changed nothing.
/// </summary>
public sealed class SettingsScreen : ShellScreen
{
    private static readonly int[] FpsCapValues = [30, 60, 90, 120, 0];
    private static readonly string[] FpsCapLabels = ["30 FPS", "60 FPS", "90 FPS", "120 FPS", "UNCAPPED"];
    private static readonly string[] RenderScaleLabels = ["540P (ULTRA)", "720P (PERFORMANCE)", "900P (BALANCED)", "NATIVE (MAX)"];
    private static readonly int[] DynamicTargetValues = [30, 45, 60];
    private static readonly string[] DynamicTargetLabels = ["30 FPS", "45 FPS", "60 FPS"];
    private static readonly float[] ViewDistanceValues = [1f, 0.1f, 0.025f];
    private static readonly string[] ViewDistanceLabels = ["FAR", "MEDIUM", "NEAR"];
    private static readonly int[] AnisotropyValues = [1, 4, 8, 16];
    private static readonly string[] AnisotropyLabels = ["OFF", "4X", "8X", "16X"];
    private static readonly string[] TextureQualityLabels = ["LOW", "MEDIUM", "HIGH"];

    private readonly float _initialOpacity;
    private readonly float _initialSensitivity;
    private readonly int _initialFpsCap;
    private readonly bool _initialInvert;
    private readonly bool _initialGyro;
    private readonly float _initialGyroSensitivity;
    private readonly float _initialUiScale;
    private readonly float _initialTextScale;
    private readonly int _initialRenderScale;
    private readonly bool _initialDynamicResolution;
    private readonly int _initialDynamicTargetFps;
    private readonly float _initialViewDistance;
    private readonly bool _initialShowFps;
    private readonly float _initialSunBrightness;
    private readonly bool _initialSmooth;
    private readonly int _initialAnisotropy;
    private readonly bool _initialPointMips;
    private readonly int _initialTextureQuality;
    private readonly float _initialMuzzleFlash;
    private readonly bool _initialWeather;
    private readonly bool _initialStorm;
    private readonly float _initialCrosshairScale;
    private readonly int _initialCrosshairColor;
    private readonly bool _initialCrosshairOutline;

    private ScrollView? _scroll;
    private UiVec2? _touchDragStart;
    private float _touchDragStartOffset;
    private bool _isDragging;
    private Control? _pendingTapTarget;

    /// <summary>Initializes a new instance of the <see cref="SettingsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public SettingsScreen(ShellContext context)
        : base(context)
    {
        _initialOpacity = context.Host.TouchOpacity;
        _initialSensitivity = context.Host.TouchSensitivity;
        _initialFpsCap = ReconstructionCVars.FrameLimit.Value;
        _initialInvert = context.Config.Active?.InvertMouse ?? false;
        _initialGyro = context.Config.Active?.GyroEnabled ?? false;
        _initialGyroSensitivity = context.Config.Active?.GyroSensitivity ?? 0.5f;
        _initialUiScale = DisplayCVars.UiScale.Value;
        _initialTextScale = DisplayCVars.TextScale.Value;
        _initialRenderScale = context.Host.RenderScaleMode;
        _initialDynamicResolution = ReconstructionCVars.DynamicResolution.Value;
        _initialDynamicTargetFps = ReconstructionCVars.DynamicTargetFps.Value;
        _initialViewDistance = ReconstructionCVars.ViewDistance.Value;
        _initialShowFps = DisplayCVars.ShowFps.Value;
        _initialSunBrightness = LightingCVars.SunBrightness.Value;
        _initialSmooth = FrameCVars.SmoothPresentation.Value;
        _initialAnisotropy = MaterialCVars.Anisotropy.Value;
        _initialPointMips = MaterialCVars.PointMips.Value;
        _initialTextureQuality = MaterialCVars.TextureQuality.Value;
        _initialMuzzleFlash = CombatEffectCVars.MuzzleFlash.Value;
        _initialWeather = WeatherCVars.WeatherEnabled.Value;
        _initialStorm = WeatherCVars.Storm.Value;
        _initialCrosshairScale = HudCVars.CrosshairScale.Value;
        _initialCrosshairColor = HudCVars.CrosshairColor.Value;
        _initialCrosshairOutline = HudCVars.CrosshairOutline.Value;
    }

    /// <summary>Builds this screen's own theme: the shell's colours, with larger text and rows.</summary>
    private static UiTheme LargeTheme(UiTheme shared)
    {
        return new UiTheme(shared.Body, shared.Heading, shared.Mono, shared.Display)
        {
            BodyTextSize = 22f,
            HeadingTextSize = 52f,
            LabelTextSize = 23f,
            CaptionTextSize = 14f,
            LabelTracking = shared.LabelTracking,
            CaptionTracking = shared.CaptionTracking,
            RowHeight = 56f,
            RowDividers = shared.RowDividers,
            GlassBlur = shared.GlassBlur,
            Text = shared.Text,
            TextBright = shared.TextBright,
            TextLabel = shared.TextLabel,
            TextSecondary = shared.TextSecondary,
            TextMuted = shared.TextMuted,
            TextDim = shared.TextDim,
            TextDisabled = shared.TextDisabled,
            Accent = shared.Accent,
            AccentHover = shared.AccentHover,
            OnAccent = shared.OnAccent,
            Panel = shared.Panel,
            FocusFill = shared.FocusFill,
            Border = shared.Border,
            Track = shared.Track,
            Shadow = shared.Shadow,
            Surface = shared.Surface,
            SurfaceRaised = shared.SurfaceRaised,
            Selection = shared.Selection,
            Info = shared.Info,
            Success = shared.Success,
            Warning = shared.Warning,
            Danger = shared.Danger,
            Destructive = shared.Destructive,
            DestructiveBorder = shared.DestructiveBorder,
        };
    }

    /// <inheritdoc/>
    protected override bool HasChrome => false;

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Settings;

    /// <inheritdoc/>
    public override float BackdropBlur => 0f;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        // This screen's own theme: the shell's colours, with larger text and rows. The shared theme
        // is never mutated -- the other screens are laid out for its sizes. The frame is already as
        // tall as the 1080 reference allows, so the growth is in width, row height and text.
        UiTheme theme = LargeTheme(Context.Theme);

        var root = new Canvas();

        // 1. Logo at Top Center
        var logo = new IgiLogo(theme)
        {
            Margin = new Thickness(0f, 20f, 0f, 0f),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Add(logo, new Anchor(UiVec2.Zero, new UiVec2(1f, 0.20f), Thickness.Zero));

        // 2. Central Green Tactical Frame ("Configuration")
        var frame = new IgiTacticalFrame(theme, "Configuration")
        {
            Width = 1280f,
            Height = 880f,
            Margin = new Thickness(0f, 15f, 0f, 0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The options scroll: there are more of them than the frame holds, so the list lives in
        // a ScrollView and the OK/Cancel bar stays fixed at the bottom of the frame.
        _scroll = new ScrollView(theme)
        {
            Width = 1190f,
            Height = 740f,
            Margin = new Thickness(60f, 42f, 30f, 0f),
            ScrollbarWidth = 14f,
            ScrollbarHitWidth = 56f,
        };

        var configStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 18f,
            Width = 1150f,
        };
        _scroll.SetContent(configStack);

        // ── Option 1: On-Screen Touch Controls Customizer ─────────────────
        var customizeRow = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 16f,
            Width = 1150f,
        };

        var customizeBtn = new Button(theme, "CUSTOMIZE ON-SCREEN CONTROLS")
        {
            Accent = ButtonAccent.Ghost,
            Width = 1150f,
            Height = 80f,
            TextSize = 36f,
        };
        customizeBtn.Activated += _ => Context.Host.CustomizeTouchControls();
        customizeRow.Add(customizeBtn);
        configStack.AddChild(customizeRow);

        // ── Option 2: Controls Alpha Slider (20% to 100%) ─────────────────
        int initialPct = (int)Math.Round(Context.Host.TouchOpacity * 100f);
        var alphaSlider = new Slider(theme, "Controls Alpha", 20f, 100f, initialPct, 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        alphaSlider.ValueChanged += value =>
        {
            float norm = Math.Clamp(value / 100f, 0.20f, 1.0f);
            Context.Host.TouchOpacity = norm;
        };
        configStack.AddChild(alphaSlider);

        // ── Option 3: Touch Sensitivity Slider (20% to 300%) ──────────────
        int initialSensPct = (int)Math.Round(Context.Host.TouchSensitivity * 100f);
        var sensSlider = new Slider(theme, "Touch Sensitivity", 20f, 300f, initialSensPct, 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        sensSlider.ValueChanged += value =>
        {
            float norm = Math.Clamp(value / 100f, 0.20f, 3.0f);
            Context.Host.TouchSensitivity = norm;
        };
        configStack.AddChild(sensSlider);

        // ── Option 4: UI Size Slider (80% to 200%) ─────────────────────────
        // Backed by r_ui_scale, so the size survives a relaunch; the host hands the value to the
        // layout as its user scale, which is what makes the whole shell -- this screen included --
        // draw larger. The old version wrote a static nothing ever saved.
        int initialUiPct = (int)Math.Round(DisplayCVars.UiScale.Value * 100f);
        var uiSizeSlider = new Slider(theme, "UI Size", 80f, 200f, initialUiPct, 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        uiSizeSlider.ValueChanged += value =>
        {
            DisplayCVars.UiScale.Value = Math.Clamp(value / 100f, 0.8f, 2.0f);
        };
        configStack.AddChild(uiSizeSlider);

        // ── Option 4: Text Size Slider (80% to 150%) ───────────────────────
        // Backed by r_text_scale, so it survives a relaunch too; the row widgets multiply the size
        // they resolve by it as they draw, so it takes effect live. It used to write a static no
        // text drawing ever read, so the slider did nothing at all.
        int initialTextPct = (int)Math.Round(DisplayCVars.TextScale.Value * 100f);
        var textSizeSlider = new Slider(theme, "Text Size", 80f, 150f, initialTextPct, 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        textSizeSlider.ValueChanged += value =>
        {
            DisplayCVars.TextScale.Value = Math.Clamp(value / 100f, 0.8f, 1.5f);
        };
        configStack.AddChild(textSizeSlider);

        // ── Graphics ───────────────────────────────────────────────────────
        // One category for every graphics option that actually changes the picture on
        // the Android path (first-run preset Vanilla: the original's forward path
        // straight to the back buffer, no offscreen scene, no Resolve pass). Rows
        // that only run on the deferred/HDR path are deliberately not here — see the
        // PR body for the list and why.
        configStack.AddChild(new GroupHeader(theme, "Graphics") { Margin = new Thickness(0f, 18f, 0f, 4f) });

        // ── Option 5: Max Frame Rate (FPS Cap) ───────────────────────────
        // The default is uncapped (0): the CVar's own default and the first-run value, and the loop
        // holds a picked cap strictly (see BeginFramePass). A value the cycler does not offer shows
        // the nearest offered cap rather than claiming Uncapped; the stored value itself is left alone
        // until a cap is picked.
        int currentFps = ReconstructionCVars.FrameLimit.Value;
        int selectedFpsIndex = Array.IndexOf(FpsCapValues, currentFps);
        if (selectedFpsIndex < 0)
        {
            int nearest = 0;
            for (int i = 1; i < FpsCapValues.Length; i++)
            {
                if (Math.Abs(FpsCapValues[i] - currentFps) < Math.Abs(FpsCapValues[nearest] - currentFps))
                {
                    nearest = i;
                }
            }

            selectedFpsIndex = nearest;
        }

        var fpsCycler = new OptionCycler(theme, "Max Frame Rate", FpsCapLabels, selectedFpsIndex)
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
        };
        fpsCycler.SelectionChanged += index =>
        {
            int cap = FpsCapValues[index];
            ReconstructionCVars.FrameLimit.Value = cap;
        };
        configStack.AddChild(fpsCycler);

        // ── Option 6: Render Resolution Quality (720p / 900p / Native) ───
        int currentRes = Context.Host.RenderScaleMode;
        var resCycler = new OptionCycler(theme, "Resolution Quality", RenderScaleLabels, Math.Clamp(currentRes, 0, 3))
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
        };
        resCycler.SelectionChanged += index =>
        {
            Context.Host.RenderScaleMode = index;
        };
        configStack.AddChild(resCycler);

        // ── Dynamic Resolution: the picture shrinks itself to hold the frame rate ──
        // The PS2 trick, done live: when the frame rate falls below the target below, the game steps
        // the render scale down its own ladder until the rate holds, and steps back up slowly when it
        // holds comfortably, so the picture does not flicker. Your Resolution Quality above is the
        // ceiling -- it never renders above that -- and the archived render scale is never written,
        // so your tier survives a relaunch exactly as picked. Off by default: a capable device
        // renders exactly as before.
        bool dynamicState = ReconstructionCVars.DynamicResolution.Value;
        var dynamicToggle = new Toggle(theme, "Dynamic Resolution", dynamicState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Let the game lower the render resolution itself when the frame rate falls, to hold the target below. Your Resolution Quality is the ceiling -- it never renders above that. Off renders exactly as before.",
        };
        dynamicToggle.ValueChanged += val =>
        {
            ReconstructionCVars.DynamicResolution.Value = val;
        };
        configStack.AddChild(dynamicToggle);

        // ── Dynamic Target FPS: the rate dynamic resolution holds ────────────────
        int currentDynamicTarget = ReconstructionCVars.DynamicTargetFps.Value;
        int selectedDynamicTargetIndex = Array.IndexOf(DynamicTargetValues, currentDynamicTarget);
        if (selectedDynamicTargetIndex < 0)
        {
            selectedDynamicTargetIndex = 0;
        }

        var dynamicTargetCycler = new OptionCycler(theme, "Dynamic Target FPS", DynamicTargetLabels, selectedDynamicTargetIndex)
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
        };
        dynamicTargetCycler.SelectionChanged += index =>
        {
            ReconstructionCVars.DynamicTargetFps.Value = DynamicTargetValues[index];
        };
        configStack.AddChild(dynamicTargetCycler);

        // ── View Distance: how far the world draws ──────────────────────────────
        // Shortens the far plane: Far is the original's 100 km, Medium 10 km, Near 2.5 km. The
        // original's own fog hides everything past about two and a half kilometres anyway, so Near
        // loses almost nothing visible and skips drawing what the fog would have hidden. The sky dome
        // sizes itself to the far plane, so it stays put, and the held weapon sits centimetres away,
        // so a 2.5 km far plane still contains it entirely.
        float currentViewDistance = ReconstructionCVars.ViewDistance.Value;
        int selectedViewDistanceIndex = 0;
        for (int i = 0; i < ViewDistanceValues.Length; i++)
        {
            if (Math.Abs(ViewDistanceValues[i] - currentViewDistance) < Math.Abs(ViewDistanceValues[selectedViewDistanceIndex] - currentViewDistance))
            {
                selectedViewDistanceIndex = i;
            }
        }

        var viewDistanceCycler = new OptionCycler(theme, "View Distance", ViewDistanceLabels, selectedViewDistanceIndex)
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
            Tooltip = "How far the world draws. Near (2.5 km) skips what the original's fog hides anyway; Far is the original's 100 km.",
        };
        viewDistanceCycler.SelectionChanged += index =>
        {
            ReconstructionCVars.ViewDistance.Value = ViewDistanceValues[index];
        };
        configStack.AddChild(viewDistanceCycler);

        // ── Sun: how bright the level sun reads ─────────────────────────────
        // Scales the authored sun that lights the models and the world's bakes.
        // Above 100% dark areas read brighter; 100% is the level as authored.
        // (Display gamma only runs in the Resolve pass, which the vanilla path
        // Android uses never has, so this is the brightness knob that works here.)
        float initialSun = LightingCVars.SunBrightness.Value;
        var sunSlider = new Slider(theme, "Sun", 50f, 300f, (int)Math.Round(initialSun * 100f), 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        sunSlider.ValueChanged += value =>
        {
            LightingCVars.SunBrightness.Value = Math.Clamp(value / 100f, 0.5f, 3f);
        };
        configStack.AddChild(sunSlider);

        // ── Smooth Motion: draw at the display's rate, not the original's 30 Hz ──
        // Interpolates between the original's fixed 30 ticks a second. Off restores
        // the original cadence (and IGI 2's retail mouse filtering and turn-speed cap).
        bool smoothState = FrameCVars.SmoothPresentation.Value;
        var smoothToggle = new Toggle(theme, "Smooth Motion", smoothState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Draw the world at your display's rate instead of the original's fixed 30 steps a second. Off restores the original cadence.",
        };
        smoothToggle.ValueChanged += val =>
        {
            FrameCVars.SmoothPresentation.Value = val;
        };
        configStack.AddChild(smoothToggle);

        // ── Show FPS Counter Overlay (moved up from the end of the list) ───────
        bool showFpsState = DisplayCVars.ShowFps.Value;
        var fpsToggle = new Toggle(theme, "Show FPS Counter", showFpsState)
        {
            Width = 1150f,
            Height = 68f,
        };
        fpsToggle.ValueChanged += val =>
        {
            DisplayCVars.ShowFps.Value = val;
        };
        configStack.AddChild(fpsToggle);

        // ── Graphics quality ───────────────────────────────────────────────
        // Texture sharpness is the knob for Ray's "snow looks too smooth": vanilla
        // pins anisotropy to 1 (off), and without it the ground smears towards the
        // horizon. The rest are the combat/weather picture knobs that are live on
        // this path. The texture rows apply at once now -- changing one re-resolves the
        // samplers of textures already loaded -- instead of waiting for the next level.
        configStack.AddChild(new GroupHeader(theme, "Graphics quality") { Margin = new Thickness(0f, 18f, 0f, 4f) });

        // ── Texture Sharpness: anisotropic filtering (Off / 4x / 8x / 16x) ─────
        int currentAnisotropy = MaterialCVars.Anisotropy.Value;
        int selectedAnisotropyIndex = Array.IndexOf(AnisotropyValues, currentAnisotropy);
        if (selectedAnisotropyIndex < 0)
        {
            selectedAnisotropyIndex = 0;
        }

        var anisotropyCycler = new OptionCycler(theme, "Texture Sharpness", AnisotropyLabels, selectedAnisotropyIndex)
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
            Tooltip = "How sharp the ground stays into the distance. Off is the original's look; higher keeps snow and terrain crisp towards the horizon. Applies immediately.",
        };
        anisotropyCycler.SelectionChanged += index =>
        {
            MaterialCVars.Anisotropy.Value = AnisotropyValues[index];
        };
        configStack.AddChild(anisotropyCycler);

        // ── Blend Mip Levels: linear mip blending instead of the original's nearest ──
        bool mipBlendState = !MaterialCVars.PointMips.Value;
        var mipBlendToggle = new Toggle(theme, "Blend Mip Levels", mipBlendState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Blend between mip levels instead of taking the nearest one, as the original does. Off is the original's slightly crisper, slightly shimmering look. Applies immediately.",
        };
        mipBlendToggle.ValueChanged += val =>
        {
            MaterialCVars.PointMips.Value = !val;
        };
        configStack.AddChild(mipBlendToggle);

        // ── Texture Quality: how sharp textures stay at a distance ───────────
        // A bias on the mip selection, on top of what the render scale asks for: High samples one
        // level sharper, Low one softer. It reaches textures already loaded, so it shows at once.
        // Vulkan only -- WebGPU has no mip-bias sampler state and samples unbiased.
        int currentTextureQuality = Math.Clamp(MaterialCVars.TextureQuality.Value, 0, 2);
        var textureQualityCycler = new OptionCycler(theme, "Texture Quality", TextureQualityLabels, currentTextureQuality)
        {
            Width = 1150f,
            Height = 68f,
            ContentWidth = 320f,
            Tooltip = "How sharp textures stay at a distance. High samples the sharper mip levels; Low softens them. Applies immediately, on Vulkan.",
        };
        textureQualityCycler.SelectionChanged += index =>
        {
            MaterialCVars.TextureQuality.Value = index;
        };
        configStack.AddChild(textureQualityCycler);

        // ── Muzzle Flash: how bright the flame at the barrel is ───────────────
        float initialMuzzleFlash = CombatEffectCVars.MuzzleFlash.Value;
        var muzzleSlider = new Slider(theme, "Muzzle Flash", 0f, 100f, (int)Math.Round(initialMuzzleFlash * 100f), 5f)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "How bright the flame at the barrel is. 100% is the original's; 0 removes it.",
        };
        muzzleSlider.ValueChanged += value =>
        {
            CombatEffectCVars.MuzzleFlash.Value = Math.Clamp(value / 100f, 0f, 1f);
        };
        configStack.AddChild(muzzleSlider);

        // Impact Effects and Impact Particles are deliberately NOT here any more: both are I.G.I. 1
        // systems with no I.G.I. 2 input. The retail impacts view is only registered by the I.G.I. 1
        // presenter, so the toggle gated a null draw; the particle bursts only ever came from the
        // I.G.I. 1 rig's ballistics, which never fires on this title, and no I.G.I. 2 code calls the
        // burst path. Restoring them needs I.G.I. 2 gunfire impact recording plus an I.G.I. 2
        // MissionEffects presenter, as separate work.

        // ── Weather: the rain or snow the level authors gave the mission ──────
        bool weatherState = WeatherCVars.WeatherEnabled.Value;
        var weatherToggle = new Toggle(theme, "Weather", weatherState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Draw the rain or snow the level authors gave each mission.",
        };
        weatherToggle.ValueChanged += val =>
        {
            WeatherCVars.WeatherEnabled.Value = val;
        };
        configStack.AddChild(weatherToggle);

        // ── Storm Effects: the remastered particle storm instead of the original's rain band ──
        bool stormState = WeatherCVars.Storm.Value;
        var stormToggle = new Toggle(theme, "Storm Effects", stormState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Wind-blown storm rain with splashes and lightning, instead of the original's fixed rain band. Only where a level rains.",
        };
        stormToggle.ValueChanged += val =>
        {
            WeatherCVars.Storm.Value = val;
        };
        configStack.AddChild(stormToggle);

        // ── Crosshair: size, colour and outline ────────────────────────────
        // The sight is a hairline at 1px on a dense display; these make it readable.
        // They apply to whichever sight is drawn: the original's lines and the
        // remastered cross alike.
        float initialCrosshair = HudCVars.CrosshairScale.Value;
        var crosshairSlider = new Slider(theme, "Crosshair size", 50f, 300f, (int)Math.Round(initialCrosshair * 100f), 5f)
        {
            Width = 1150f,
            Height = 68f,
        };
        crosshairSlider.ValueChanged += value =>
        {
            HudCVars.CrosshairScale.Value = Math.Clamp(value / 100f, 0.5f, 3f);
        };
        configStack.AddChild(crosshairSlider);

        var crosshairColourBtn = new Button(theme, "Crosshair colour: " + CrosshairPalette.Names[HudCVars.CrosshairColor.Value])
        {
            Width = 1150f,
            Height = 68f,
        };
        crosshairColourBtn.Activated += _ =>
        {
            int next = (HudCVars.CrosshairColor.Value + 1) % CrosshairPalette.Names.Length;
            HudCVars.CrosshairColor.Value = next;
            crosshairColourBtn.Text = "Crosshair colour: " + CrosshairPalette.Names[next];
        };
        configStack.AddChild(crosshairColourBtn);

        bool crosshairOutlineState = HudCVars.CrosshairOutline.Value;
        var crosshairOutlineToggle = new Toggle(theme, "Crosshair outline", crosshairOutlineState)
        {
            Width = 1150f,
            Height = 68f,
        };
        crosshairOutlineToggle.ValueChanged += val =>
        {
            HudCVars.CrosshairOutline.Value = val;
        };
        configStack.AddChild(crosshairOutlineToggle);

        // ── Option 7: Invert Mouse / Look Pitch ───────────────────────────
        bool invertState = Context.Config.Active?.InvertMouse ?? false;
        var invertToggle = new Toggle(theme, "Invert Look / Mouse", invertState)
        {
            Width = 1150f,
            Height = 68f,
        };
        invertToggle.ValueChanged += val =>
        {
            if (Context.Config.Active is { } profile)
            {
                profile.InvertMouse = val;
            }
        };
        configStack.AddChild(invertToggle);

        // ── Gyroscope aim (Android only; tilt the phone to aim) ─────────────
        // This is the live Configuration screen. (An earlier version put this toggle
        // in ControlsSettingsTab, which nothing ever shows, so it never appeared.)
        bool gyroState = Context.Config.Active?.GyroEnabled ?? false;
        var gyroToggle = new Toggle(theme, "Gyroscope aim", gyroState)
        {
            Width = 1150f,
            Height = 68f,
            Tooltip = "Tilt the phone to aim. Only on Android, and only while playing.",
        };
        gyroToggle.ValueChanged += val =>
        {
            if (Context.Config.Active is { } profile)
            {
                profile.GyroEnabled = val;
            }
        };
        configStack.AddChild(gyroToggle);

        // ── Gyroscope sensitivity ────────────────────────────────────────────
        float gyroSensState = Context.Config.Active?.GyroSensitivity ?? 0.5f;
        var gyroSensSlider = new Slider(theme, "Gyroscope sensitivity", 0f, 1f, gyroSensState, 0.05f)
        {
            Width = 1150f,
            Height = 68f,
            Format = v => string.Create(
                CultureInfo.InvariantCulture,
                $"{MouseLookSettings.FromSensitivity(v, invertPitch: false).TurnDistanceCentimetres():0} cm/360°"),
            Tooltip = "How fast the view turns when you tilt the phone.\n\nSame scale as mouse sensitivity — the readout is how far a full turn takes.",
        };
        gyroSensSlider.ValueChanged += value =>
        {
            if (Context.Config.Active is { } profile)
            {
                profile.GyroSensitivity = value;
            }
        };
        configStack.AddChild(gyroSensSlider);


        frame.AddChild(_scroll);

        // ── Bottom Buttons: OK & Cancel ──────────────────────────────────
        var btnBar = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 28f,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0f, 0f, 32f, 16f),
        };

        var okBtn = new IgiMenuButton(theme, "OK")
        {
            TextSize = 36f,
            Height = 72f,
            Width = 220f,
            NormalColor = Bgra32.FromRgb(80, 160, 255),
            HoverColor = Bgra32.FromRgb(228, 238, 252),
        };
        okBtn.Activated += () =>
        {
            Context.Host.SaveSettings();

            // Profiles carry Invert, Gyro and gyro sensitivity, which settings.cfg never wrote: OK
            // used to leave them to the save on exit, so they were lost whenever the app was killed
            // or swiped away after OK instead of quit cleanly.
            Context.Host.SaveProfiles();
            CloseConfig();
        };
        btnBar.Add(okBtn);

        var cancelBtn = new IgiMenuButton(theme, "Cancel")
        {
            TextSize = 36f,
            Height = 72f,
            Width = 220f,
            NormalColor = Bgra32.FromRgb(80, 160, 255),
            HoverColor = Bgra32.FromRgb(228, 238, 252),
        };
        cancelBtn.Activated += () =>
        {
            // Restore initial settings
            Context.Host.TouchOpacity = _initialOpacity;
            Context.Host.TouchSensitivity = _initialSensitivity;
            Context.Host.RenderScaleMode = _initialRenderScale;
            ReconstructionCVars.DynamicResolution.Value = _initialDynamicResolution;
            ReconstructionCVars.DynamicTargetFps.Value = _initialDynamicTargetFps;
            ReconstructionCVars.ViewDistance.Value = _initialViewDistance;
            ReconstructionCVars.FrameLimit.Value = _initialFpsCap;
            DisplayCVars.UiScale.Value = _initialUiScale;
            DisplayCVars.TextScale.Value = _initialTextScale;
            DisplayCVars.ShowFps.Value = _initialShowFps;
            LightingCVars.SunBrightness.Value = _initialSunBrightness;
            FrameCVars.SmoothPresentation.Value = _initialSmooth;
            MaterialCVars.Anisotropy.Value = _initialAnisotropy;
            MaterialCVars.PointMips.Value = _initialPointMips;
            MaterialCVars.TextureQuality.Value = _initialTextureQuality;
            CombatEffectCVars.MuzzleFlash.Value = _initialMuzzleFlash;
            WeatherCVars.WeatherEnabled.Value = _initialWeather;
            WeatherCVars.Storm.Value = _initialStorm;
            HudCVars.CrosshairScale.Value = _initialCrosshairScale;
            HudCVars.CrosshairColor.Value = _initialCrosshairColor;
            HudCVars.CrosshairOutline.Value = _initialCrosshairOutline;
            if (Context.Config.Active is { } profile)
            {
                profile.InvertMouse = _initialInvert;
                profile.GyroEnabled = _initialGyro;
                profile.GyroSensitivity = _initialGyroSensitivity;
            }
            CloseConfig();
        };
        btnBar.Add(cancelBtn);

        frame.AddChild(btnBar);
        root.Add(frame, new Anchor(UiVec2.Zero, UiVec2.One, Thickness.Zero));

        return root;
    }

    private void CloseConfig()
    {
        if (Context.InGame)
        {
            Context.Stack.Reset(new PauseScreen(Context));
        }
        else
        {
            Context.Stack.Reset(new MainMenuScreen(Context));
        }
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        if (nav.Back)
        {
            CloseConfig();
            return false;
        }

        // Drag to scroll the options list, mirroring the mission list: a drag scrolls, a
        // tap still activates the control under the pointer.
        if (_scroll is not null)
        {
            var pointer = new UiVec2(input.Mouse.X, input.Mouse.Y);
            bool insideScroll = _scroll.Bounds.Contains(pointer);

            if (input.Mouse.WasPressed(MouseButton.Left))
            {
                if (insideScroll && !_scroll.IsOverScrollbar(pointer))
                {
                    _touchDragStart = pointer;
                    _touchDragStartOffset = _scroll.ScrollOffset;
                    _isDragging = false;
                    _pendingTapTarget = Focus?.FocusAt(pointer) as Control;
                    return false;
                }
            }
            else if (_touchDragStart is { } start && input.Mouse.IsDown(MouseButton.Left))
            {
                float dy = pointer.Y - start.Y;
                if (!_isDragging && MathF.Abs(dy) > 10f)
                {
                    _isDragging = true;
                }

                if (_isDragging)
                {
                    _scroll.ScrollOffset = _touchDragStartOffset - dy;
                    return false;
                }
            }
            else if (input.Mouse.WasReleased(MouseButton.Left) || !input.Mouse.IsDown(MouseButton.Left))
            {
                if (_touchDragStart is not null)
                {
                    _touchDragStart = null;
                    if (!_isDragging)
                    {
                        _pendingTapTarget?.ActivateAt(pointer);
                    }
                    _isDragging = false;
                    _pendingTapTarget = null;
                    return false;
                }
            }
        }

        return base.HandleInput(nav, input);
    }
}

