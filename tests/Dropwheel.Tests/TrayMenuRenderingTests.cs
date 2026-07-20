using SD = System.Drawing;
using WF = System.Windows.Forms;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class TrayMenuRenderingTests
{
    [Fact]
    public void Renderer_applies_disabled_palette_colors_to_the_actual_drawing_path()
    {
        var palette = Palettes.All["Dark"];
        using var menu = new WF.ContextMenuStrip
        {
            Renderer = TrayMenuTheme.CreateRenderer(palette),
            BackColor = ToDrawingColor(palette.Surface),
            ForeColor = ToDrawingColor(palette.Text),
        };
        var item = new WF.ToolStripMenuItem("Disabled action")
        {
            Enabled = false,
            Size = new SD.Size(220, 24),
        };
        menu.Items.Add(item);
        item.Select();
        Assert.True(item.Selected, "The regression requires WinForms' selected + disabled state.");

        using var bitmap = new SD.Bitmap(item.Width, item.Height);
        using var graphics = SD.Graphics.FromImage(bitmap);
        graphics.Clear(SD.Color.Magenta);

        menu.Renderer.DrawMenuItemBackground(new WF.ToolStripItemRenderEventArgs(graphics, item));
        Assert.Equal(
            ToDrawingColor(palette.Surface),
            bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));

        var textArgs = new WF.ToolStripItemTextRenderEventArgs(
            graphics,
            item,
            item.Text,
            item.ContentRectangle,
            SD.Color.Magenta,
            item.Font,
            SD.ContentAlignment.MiddleLeft);
        menu.Renderer.DrawItemText(textArgs);

        Assert.Equal(ToDrawingColor(palette.TextMuted), textArgs.TextColor);
        var renderedPixels = Enumerable.Range(0, bitmap.Width)
            .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)))
            .ToArray();
        Assert.Contains(renderedPixels, color => color.ToArgb() == ToDrawingColor(palette.TextMuted).ToArgb());
        Assert.DoesNotContain(renderedPixels, color => color.ToArgb() == SD.SystemColors.GrayText.ToArgb());
    }

    [Theory]
    [InlineData("Fluent")]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Neon")]
    public void Disabled_selected_items_keep_the_palette_surface_and_muted_text(string paletteName)
    {
        var palette = Palettes.All[paletteName];

        var idle = TrayMenuTheme.ResolveItemColors(palette, enabled: false, selected: false);
        var selected = TrayMenuTheme.ResolveItemColors(palette, enabled: false, selected: true);

        Assert.Equal(idle, selected);
        Assert.Equal(ToDrawingColor(palette.Surface), selected.Background);
        Assert.Equal(ToDrawingColor(palette.TextMuted), selected.Text);
    }

    [Theory]
    [InlineData("Fluent")]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Neon")]
    public void Enabled_selected_items_keep_the_palette_hover_and_normal_text(string paletteName)
    {
        var palette = Palettes.All[paletteName];

        var idle = TrayMenuTheme.ResolveItemColors(palette, enabled: true, selected: false);
        var selected = TrayMenuTheme.ResolveItemColors(palette, enabled: true, selected: true);

        Assert.NotEqual(idle.Background, selected.Background);
        Assert.Equal(ToDrawingColor(palette.Text), selected.Text);
    }

    [Theory]
    [InlineData("Fluent")]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Neon")]
    public void Renderer_paints_the_exact_opaque_hover_color(string paletteName)
    {
        var palette = Palettes.All[paletteName];
        using var menu = new WF.ContextMenuStrip
        {
            Renderer = TrayMenuTheme.CreateRenderer(palette),
        };
        var item = new WF.ToolStripMenuItem("Selected") { Size = new SD.Size(220, 24) };
        menu.Items.Add(item);
        item.Select();

        using var bitmap = new SD.Bitmap(item.Width, item.Height);
        using var graphics = SD.Graphics.FromImage(bitmap);
        graphics.Clear(SD.Color.Magenta);
        menu.Renderer.DrawMenuItemBackground(new WF.ToolStripItemRenderEventArgs(graphics, item));

        var expected = TrayMenuTheme.ResolveItemColors(palette, enabled: true, selected: true).Background;
        Assert.Equal(expected, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2));
        Assert.Equal(255, bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A);
    }

    [Fact]
    public void Renderer_draws_a_checked_glyph_without_a_check_background()
    {
        var palette = Palettes.All["Dark"];
        var surface = ToDrawingColor(palette.Surface);
        var accent = ToDrawingColor(palette.Accent);
        using var menu = new WF.ContextMenuStrip
        {
            Renderer = TrayMenuTheme.CreateRenderer(palette),
        };
        using var glyph = new SD.Bitmap(16, 16);
        glyph.SetPixel(8, 8, accent);
        var item = new WF.ToolStripMenuItem("Checked")
        {
            Checked = true,
            Image = glyph,
            Size = new SD.Size(220, 24),
        };
        menu.Items.Add(item);

        using var bitmap = new SD.Bitmap(16, 16);
        using var graphics = SD.Graphics.FromImage(bitmap);
        graphics.Clear(surface);
        var args = new WF.ToolStripItemImageRenderEventArgs(
            graphics, item, glyph, new SD.Rectangle(0, 0, 16, 16));
        menu.Renderer.DrawItemCheck(args);

        Assert.Equal(surface, bitmap.GetPixel(0, 0));
        Assert.Equal(accent, bitmap.GetPixel(8, 8));
    }

    private static SD.Color ToDrawingColor(System.Windows.Media.Color color) =>
        SD.Color.FromArgb(color.R, color.G, color.B);
}
