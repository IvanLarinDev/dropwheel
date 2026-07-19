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
            Renderer = App.CreateTrayMenuRenderer(palette),
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

        var idle = App.ResolveTrayMenuItemColors(palette, enabled: false, selected: false);
        var selected = App.ResolveTrayMenuItemColors(palette, enabled: false, selected: true);

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

        var idle = App.ResolveTrayMenuItemColors(palette, enabled: true, selected: false);
        var selected = App.ResolveTrayMenuItemColors(palette, enabled: true, selected: true);

        Assert.NotEqual(idle.Background, selected.Background);
        Assert.Equal(ToDrawingColor(palette.Text), selected.Text);
    }

    private static SD.Color ToDrawingColor(System.Windows.Media.Color color) =>
        SD.Color.FromArgb(color.R, color.G, color.B);
}
