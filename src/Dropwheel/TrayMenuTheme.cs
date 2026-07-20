using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

internal readonly record struct TrayMenuItemColors(SD.Color Background, SD.Color Text);

/// <summary>WinForms renderer and palette bridge for the tray menu, separated from App command and
/// lifecycle wiring.</summary>
internal static class TrayMenuTheme
{
    internal static void Apply(WF.ContextMenuStrip menu, Dropwheel.UI.Palette palette) =>
        StyleStrip(menu, CreateRenderer(palette), ToSd(palette.Surface), ToSd(palette.Text));

    internal static WF.ToolStripRenderer CreateRenderer(Dropwheel.UI.Palette palette) =>
        new Renderer(new PaletteColors(palette), palette);

    internal static TrayMenuItemColors ResolveItemColors(
        Dropwheel.UI.Palette palette, bool enabled, bool selected) =>
        new(
            enabled && selected ? Blend(palette.Surface, palette.Accent, 0.30) : ToSd(palette.Surface),
            ToSd(enabled ? palette.Text : palette.TextMuted));

    internal static SD.Color ToDrawingColor(System.Windows.Media.Color color) => ToSd(color);

    private static void StyleStrip(WF.ToolStrip strip, WF.ToolStripRenderer renderer, SD.Color back, SD.Color fore)
    {
        strip.Renderer = renderer;
        strip.BackColor = back;
        strip.ForeColor = fore;
        foreach (WF.ToolStripItem item in strip.Items)
            if (item is WF.ToolStripDropDownItem dropdown)
                StyleStrip(dropdown.DropDown, renderer, back, fore);
    }

    private static SD.Color ToSd(System.Windows.Media.Color color) => SD.Color.FromArgb(color.R, color.G, color.B);

    private static SD.Color Blend(System.Windows.Media.Color a, System.Windows.Media.Color b, double t) =>
        SD.Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

    private sealed class Renderer(WF.ProfessionalColorTable table, Dropwheel.UI.Palette palette)
        : WF.ToolStripProfessionalRenderer(table)
    {
        protected override void OnRenderMenuItemBackground(WF.ToolStripItemRenderEventArgs e)
        {
            // Paint the entire item ourselves. The stock professional renderer can layer a system
            // hot-track/check surface over dark menus, producing the bright rectangles seen behind
            // selected items even when the color table itself is palette-aware.
            var colors = ResolveItemColors(palette, e.Item.Enabled, e.Item.Selected);
            using var brush = new SD.SolidBrush(colors.Background);
            e.Graphics.FillRectangle(brush, new SD.Rectangle(SD.Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ResolveItemColors(palette, e.Item.Enabled, e.Item.Selected).Text;
            if (e.Item.Enabled) { base.OnRenderItemText(e); return; }
            WF.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat);
        }

        protected override void OnRenderItemCheck(WF.ToolStripItemImageRenderEventArgs e)
        {
            // Checked menu items keep their accessibility state, but use the transparent palette
            // glyph supplied by App.Tray instead of the stock high-contrast check background.
            if (e.Item.Image is { } image)
                e.Graphics.DrawImage(image, e.ImageRectangle);
        }
    }

    private sealed class PaletteColors : WF.ProfessionalColorTable
    {
        private readonly SD.Color _background;
        private readonly SD.Color _hover;
        private readonly SD.Color _pressed;
        private readonly SD.Color _line;

        internal PaletteColors(Dropwheel.UI.Palette palette)
        {
            _background = ToSd(palette.Surface);
            _hover = Blend(palette.Surface, palette.Accent, 0.30);
            _pressed = Blend(palette.Surface, palette.Accent, 0.18);
            _line = ToSd(palette.Border);
        }

        public override SD.Color ToolStripDropDownBackground => _background;
        public override SD.Color ImageMarginGradientBegin => _background;
        public override SD.Color ImageMarginGradientMiddle => _background;
        public override SD.Color ImageMarginGradientEnd => _background;
        public override SD.Color MenuBorder => _line;
        public override SD.Color MenuItemBorder => _line;
        public override SD.Color MenuItemSelected => _hover;
        public override SD.Color MenuItemSelectedGradientBegin => _hover;
        public override SD.Color MenuItemSelectedGradientEnd => _hover;
        public override SD.Color MenuItemPressedGradientBegin => _pressed;
        public override SD.Color MenuItemPressedGradientMiddle => _pressed;
        public override SD.Color MenuItemPressedGradientEnd => _pressed;
        public override SD.Color CheckBackground => _hover;
        public override SD.Color CheckSelectedBackground => _hover;
        public override SD.Color SeparatorDark => _line;
        public override SD.Color SeparatorLight => _background;
    }
}
