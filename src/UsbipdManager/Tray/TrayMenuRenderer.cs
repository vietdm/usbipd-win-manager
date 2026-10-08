using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UsbipdManager.Tray;

internal sealed record TrayPalette(
    Drawing.Color Surface,
    Drawing.Color Text,
    Drawing.Color TextDisabled,
    Drawing.Color Hover,
    Drawing.Color Border);

/// <summary>Draws the tray ContextMenuStrip with the app's light/dark tokens.</summary>
internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private readonly TrayPalette _palette;

    public TrayMenuRenderer(TrayPalette palette)
        : base(new TrayColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new Drawing.SolidBrush(_palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(_palette.Border);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
        // No icon column.
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var rect = new Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var brush = new Drawing.SolidBrush(_palette.Hover);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _palette.Text : _palette.TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Drawing.Pen(_palette.Border);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private sealed class TrayColorTable(TrayPalette palette) : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => palette.Surface;

        public override Drawing.Color MenuBorder => palette.Border;

        public override Drawing.Color MenuItemBorder => palette.Hover;

        public override Drawing.Color MenuItemSelected => palette.Hover;

        public override Drawing.Color ImageMarginGradientBegin => palette.Surface;

        public override Drawing.Color ImageMarginGradientMiddle => palette.Surface;

        public override Drawing.Color ImageMarginGradientEnd => palette.Surface;

        public override Drawing.Color SeparatorDark => palette.Border;

        public override Drawing.Color SeparatorLight => palette.Border;
    }
}
