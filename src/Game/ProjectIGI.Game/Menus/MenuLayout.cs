using ProjectIGI.Core.Graphics;

namespace ProjectIGI.Game.Menus;

/// <summary>
/// The presentation rules the menu types share: where an item lands on screen, and what colour it draws in.
/// </summary>
/// <remarks>
/// Transcribed from <c>MenuText_Prepare</c> (<c>0x420F40</c>) and <c>MenuText_Render</c>
/// (<c>0x421130</c>).
/// </remarks>
public static class MenuLayout
{
    /// <summary>The width the menus are authored against.</summary>
    public const int AuthoredWidth = 640;

    /// <summary>The height the menus are authored against.</summary>
    public const int AuthoredHeight = 480;

    /// <summary>The value the original's colour scale treats as full intensity.</summary>
    /// <remarks>
    /// The same 128 that <c>Font_Create</c> initialises its colour to. Menu colours are expressed on a
    /// 0–128 scale, not 0–255.
    /// </remarks>
    public const float FullIntensity = 128.0f;

    /// <summary>Which title's menu colour table a menu draws with.</summary>
    /// <remarks>
    /// Project I.G.I.'s table is the one transcribed from its binary; I.G.I. 2's is a first-pass
    /// derivation from a retail screenshot (see <see cref="Igi2Colours"/>) — same indices, different RGB.
    /// </remarks>
    public enum MenuColourTable
    {
        /// <summary>Project I.G.I.'s table: green entries, orange version text.</summary>
        Igi1,

        /// <summary>I.G.I. 2's table: pale entries, pale version text.</summary>
        Igi2,
    }

    /// <summary>
    /// One title's menu presentation: which colour table it draws with, and which of the shared
    /// presentation pieces it keeps.
    /// </summary>
    /// <param name="ColourTable">The colour table the menu's text and boxes draw from.</param>
    /// <param name="DrawScreenPanel">Whether a screen draws its panel and title rule.</param>
    /// <param name="DrawBackgroundFx">Whether a screen draws the turning green overlay behind itself.</param>
    /// <param name="DrawSelectionGlow">Whether a lit entry draws the glow sprite behind itself.</param>
    /// <param name="PanelTint">The panel's colours: the fill tint and the border/title-rule edge.</param>
    /// <remarks>
    /// A single value threaded presenter → host → renderer, so a title's look is one choice, not a
    /// second threading path per gate. I.G.I. 1 keeps everything it has always drawn; I.G.I. 2's
    /// retail menu draws the panel in its own blue and never the overlay, and lights its selection
    /// with the glow sprite instead of the flat bar. (PR #16 gated the panel off for I.G.I. 2 on a
    /// misread of the reference thumbnail; Ray corrected it 2026-11-06: the blue box around the
    /// menu and mission screens IS the panel, I.G.I. 1's green box in blue.)
    /// </remarks>
    public readonly record struct MenuStyle(MenuColourTable ColourTable, bool DrawScreenPanel, bool DrawBackgroundFx, bool DrawSelectionGlow, MenuPanelTint PanelTint)
    {
        /// <summary>Project I.G.I.'s menus, exactly as drawn before this type existed.</summary>
        public static MenuStyle Igi1 { get; } = new(MenuColourTable.Igi1, DrawScreenPanel: true, DrawBackgroundFx: true, DrawSelectionGlow: false, MenuPanelTint.Igi1);

        /// <summary>I.G.I. 2's retail menus: its own colour table, blue panel, no overlay, glow selection.</summary>
        public static MenuStyle Igi2 { get; } = new(MenuColourTable.Igi2, DrawScreenPanel: true, DrawBackgroundFx: false, DrawSelectionGlow: true, MenuPanelTint.Igi2);
    }

    /// <summary>The screen panel's colours: the fill tint and the border/title-rule edge.</summary>
    /// <param name="Fill">The fill colour, drawn as a vertical gradient at the panel's fill alphas (0.2 top, 0.05 bottom).</param>
    /// <param name="Edge">The border and title-rule colour.</param>
    /// <remarks>
    /// Project I.G.I.'s is its green, exactly as drawn before this type existed: full green fill,
    /// edge at <see cref="MenuScreenPanel.EdgeGreen"/> (the original's 0.75). I.G.I. 2's is
    /// <b>FIRST PASS — APPROXIMATE.</b> The exact retail panel blue is not measured — no full-size
    /// retail screenshot is on hand — so it takes the I.G.I. 2 theme blue already in the tree: the
    /// (80, 160, 255) accent the frontend chrome uses, rimmed in the (125, 170, 225) rim the touch
    /// controls use. Refine from a full-size retail screenshot.
    /// </remarks>
    public readonly record struct MenuPanelTint(Bgra32 Fill, Bgra32 Edge)
    {
        /// <summary>Project I.G.I.'s panel, exactly as drawn before this type existed.</summary>
        public static MenuPanelTint Igi1 { get; } = new(Bgra32.FromRgb(0, 255, 0), Bgra32.FromRgb(0, MenuScreenPanel.EdgeGreen, 0));

        /// <summary>I.G.I. 2's panel: theme blue fill, lighter blue rim. FIRST PASS — see remarks.</summary>
        public static MenuPanelTint Igi2 { get; } = new(Bgra32.FromRgb(80, 160, 255), Bgra32.FromRgb(125, 170, 225));
    }

    /// <summary>
    /// The colour table <c>MenuText</c> indexes with its <c>ColourIndex</c> parameter, on the
    /// original's 0–128 scale.
    /// </summary>
    /// <remarks>
    /// Read from the table at <c>0x53B778</c>: four entries of three floats, indexed by
    /// <c>ColourIndex &amp; 0xF</c>. The names come from the constants the <c>MenuText</c> registrar
    /// declares.
    /// </remarks>
    public static IReadOnlyList<(float R, float G, float B)> Colours { get; } =
    [
        (0.0f, 0.0f, 0.0f),        // TEXT_BLACK
        (128.0f, 128.0f, 128.0f),  // TEXT_WHITE
        (0.0f, 128.0f, 0.0f),      // TEXT_GREEN
        (128.0f, 95.0f, 0.0f),     // TEXT_ORANGE
    ];

    /// <summary>
    /// I.G.I. 2's colour table, same indices and 0–128 scale as <see cref="Colours"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FIRST PASS — APPROXIMATE. Refine from a full-size retail screenshot.</b> The exact I.G.I. 2
    /// RGB is not determinable from the 320×180 thumbnail that is the only retail reference on hand:
    /// every text pixel in it is blended with the dark-blue backdrop. These values are derived from the
    /// brightest glyph pixels measured in that thumbnail (see below), not read from the game.
    /// </para>
    /// <para>
    /// Indices 0 and 1 are shared with Project I.G.I. (black; white — the thumbnail's title row
    /// measures pure white, brightest pixel (249, 255, 255)). Index 2, the entries: brightest glyph
    /// pixels in the entry band measure (201, 218, 244), i.e. (101, 109, 122) on this scale at full
    /// coverage, rounded up a little for partial-coverage darkening — pale blue-grey, not green.
    /// Index 3, the version text: its "1.0" glyph is only a few pixels, brightest (109, 114, 120),
    /// heavily blended with the backdrop, so it is set a touch dimmer than index 2 in the same pale
    /// family. Both are pale, as the reference shows; the exact split between them is a guess.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(float R, float G, float B)> Igi2Colours { get; } =
    [
        (0.0f, 0.0f, 0.0f),          // TEXT_BLACK — as I.G.I. 1
        (128.0f, 128.0f, 128.0f),    // TEXT_WHITE — as I.G.I. 1; the title measures pure white
        (104.0f, 112.0f, 124.0f),    // TEXT_GREEN slot: pale blue-grey entries (FIRST PASS, see remarks)
        (96.0f, 104.0f, 116.0f),     // TEXT_ORANGE slot: pale version text (FIRST PASS, see remarks)
    ];

    /// <summary>Gets a title's colour table.</summary>
    /// <param name="table">Which title's table.</param>
    /// <returns>The four entries on the original's 0–128 scale.</returns>
    public static IReadOnlyList<(float R, float G, float B)> ColoursFor(MenuColourTable table) =>
        table switch
        {
            MenuColourTable.Igi2 => Igi2Colours,
            _ => Colours,
        };

    /// <summary>Gets the controls list's key-column colour, on the original's 0–128 scale.</summary>
    /// <param name="table">Which title's table.</param>
    /// <returns>The amber the key column draws in.</returns>
    /// <remarks>
    /// Project I.G.I.'s is its own literal, <c>(128, 90, 0)</c> — <b>not</b> its table's
    /// <c>TEXT_ORANGE</c> <c>(128, 95, 0)</c> — so it is kept exactly rather than read from the table.
    /// I.G.I. 2 has no such literal on hand, so its key column takes its table's index 3, the same
    /// pale entry the version text draws in.
    /// </remarks>
    public static (float R, float G, float B) ControlsKeyColour(MenuColourTable table) =>
        table switch
        {
            MenuColourTable.Igi2 => Igi2Colours[3],
            _ => (128.0f, 90.0f, 0.0f),
        };

    /// <summary>
    /// Maps an authored menu position to a screen position.
    /// </summary>
    /// <param name="x">The authored horizontal position.</param>
    /// <param name="y">The authored vertical position.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <param name="scale">The magnification the menu is drawn at; see <see cref="UiScale"/>.</param>
    /// <returns>The screen position in pixels.</returns>
    /// <remarks>
    /// <para>
    /// The original computes <c>(screenWidth &gt;&gt; 1) + x - 320</c> and
    /// <c>(screenHeight &gt;&gt; 1) + y - 240</c>. So the menu is <b>centred at its authored size, not
    /// scaled</b>: at 1024×768 the text stays 640×480-sized in the middle of the screen rather than
    /// growing.
    /// </para>
    /// <para>
    /// <b>Divergence.</b> On a display several times that size, faithful is unreadable — so the offset
    /// from the centre is magnified by a whole number (<see cref="UiScale"/>). At
    /// <see cref="UiScale.Native"/> this is exactly the original's arithmetic, and the caller has to
    /// magnify the art by the same amount or the layout and the sprites will disagree.
    /// </para>
    /// </remarks>
    public static (int X, int Y) ToScreen(int x, int y, int screenWidth, int screenHeight, int scale = UiScale.Native)
    {
        int s = scale < UiScale.Native ? UiScale.Native : scale;
        return (
            (screenWidth >> 1) + ((x - (AuthoredWidth / 2)) * s),
            (screenHeight >> 1) + ((y - (AuthoredHeight / 2)) * s));
    }

    /// <summary>Gets a colour from Project I.G.I.'s table, on the original's 0–128 scale.</summary>
    /// <param name="colourIndex">The <c>ColourIndex</c> parameter. Only the low four bits are used.</param>
    /// <param name="selected">Whether the item is highlighted.</param>
    /// <returns>The colour components.</returns>
    /// <remarks>
    /// A highlighted item is drawn at <c>c * 0.5 + 64</c> per channel, which brightens the dark
    /// channels toward the middle rather than scaling the whole colour up — <c>TEXT_ORANGE</c>'s blue
    /// goes from 0 to 64 while its red stays at 128.
    /// </remarks>
    public static (float R, float G, float B) GetColour(int colourIndex, bool selected) =>
        GetColour(MenuColourTable.Igi1, colourIndex, selected);

    /// <summary>Gets a colour from a title's table, on the original's 0–128 scale.</summary>
    /// <param name="table">Which title's table.</param>
    /// <param name="colourIndex">The <c>ColourIndex</c> parameter. Only the low four bits are used.</param>
    /// <param name="selected">Whether the item is highlighted.</param>
    /// <returns>The colour components.</returns>
    /// <remarks>The selection brightening is the same <c>c * 0.5 + 64</c> for every title.</remarks>
    public static (float R, float G, float B) GetColour(MenuColourTable table, int colourIndex, bool selected)
    {
        (float r, float g, float b) = ColoursFor(table)[colourIndex & 0xF];
        return selected
            ? ((r * 0.5f) + 64.0f, (g * 0.5f) + 64.0f, (b * 0.5f) + 64.0f)
            : (r, g, b);
    }

    /// <summary>The index of <c>TEXT_GREEN</c> in <see cref="Colours"/>.</summary>
    /// <remarks>
    /// The controls' own drawing does not go through a <c>ColourIndex</c> parameter — a switch's tint
    /// and a window's outline are green in the code, not in the data — but they are the same green and
    /// take the same lightening when selected, so they index the same table.
    /// </remarks>
    public const int GreenIndex = 2;

    /// <summary>The alpha the window box's black backing is filled at, relative to the draw alpha.</summary>
    /// <remarks><c>MenuWindow_DrawBox</c> (<c>0x424980</c>) is called with <c>0.5</c> by every control.</remarks>
    public const float BoxFillAlpha = 0.5f;

    /// <summary>Builds the four one-pixel rectangles that make a control's outline.</summary>
    /// <param name="x">The window's left edge.</param>
    /// <param name="y">The window's top edge.</param>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <returns>Top, right, bottom and left edges, in that order.</returns>
    /// <remarks>
    /// <c>MenuWindow_DrawBox</c> submits a four-vertex loop at <c>(x+1, y+1)</c>, <c>(x+w, y+1)</c>,
    /// <c>(x+w, y+h)</c> and <c>(x+1, y+h)</c> — inset by one on the left and top and flush on the
    /// right and bottom, which is asymmetric and is kept that way. Drawing it as four thin rectangles
    /// rather than a line loop is a rendering choice; the pixels it covers are the same.
    /// </remarks>
    public static (int X, int Y, int Width, int Height)[] GetBoxOutline(int x, int y, int width, int height)
    {
        int left = x + 1;
        int top = y + 1;
        int right = x + width;
        int bottom = y + height;
        int span = Math.Max(0, right - left);
        int drop = Math.Max(0, bottom - top);

        return
        [
            (left, top, span, 1),
            (right, top, 1, drop),
            (left, bottom, span, 1),
            (left, top, 1, drop),
        ];
    }

    /// <summary>Converts a colour on the original's 0–128 scale to a renderable pixel.</summary>
    /// <param name="colour">The colour components.</param>
    /// <returns>The opaque pixel.</returns>
    /// <remarks>128 is full intensity, so the scale factor is <c>255 / 128</c>, saturating above that.</remarks>
    public static Bgra32 ToPixel((float R, float G, float B) colour) =>
        Bgra32.FromRgb(Channel(colour.R), Channel(colour.G), Channel(colour.B));

    /// <summary>Builds the name of one of a frame's eight border sprites.</summary>
    /// <param name="spriteResource">The frame's <c>Sprite Resource</c> parameter.</param>
    /// <param name="piece">The piece number, 1 through 8.</param>
    /// <returns>The sprite name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="piece"/> is outside 1–8.</exception>
    /// <remarks>
    /// <c>MenuFrame_Prepare</c> (<c>0x422640</c>) loads exactly eight, formatted <c>"%s_%d.spr"</c> —
    /// which is why <c>menusystem.res</c> holds <c>win1_1.spr</c> through <c>win1_8.spr</c>. Eight
    /// pieces, not nine: the centre is not drawn.
    /// </remarks>
    public static string GetFramePieceName(string spriteResource, int piece)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteResource);
        ArgumentOutOfRangeException.ThrowIfLessThan(piece, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(piece, FramePieceCount);
        return $"{spriteResource}_{piece}.spr";
    }

    /// <summary>The number of border sprites a frame loads.</summary>
    public const int FramePieceCount = 8;

    private static byte Channel(float value) =>
        (byte)Math.Clamp(MathF.Round(value * 255.0f / FullIntensity), 0.0f, 255.0f);
}
