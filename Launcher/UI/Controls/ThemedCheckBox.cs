using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// Check box drawn by the launcher: a rounded box that fills with steam yellow and shows a tick, or, with Switch on,
// a sliding switch (used for on/off settings). OffText, when set, is the text shown while it is off.
internal sealed class ThemedCheckBox : CheckBox
{
    private bool hover;
    private bool isSwitch;

    public ThemedCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Cursor = Cursors.Hand;
        Margin = new Padding(3, 4, 3, 3);
    }

    public bool Switch
    {
        get => isSwitch;
        set
        {
            isSwitch = value;
            Invalidate();
            PerformLayout();
        }
    }

    public string? OffText { get; set; }

    private string Shown => Checked || OffText == null ? Text : OffText;

    private Size GlyphSize => isSwitch
        ? new Size(Dpi.Px(this, 34), Dpi.Px(this, 18))
        : new Size(Dpi.Px(this, 17), Dpi.Px(this, 17));

    public override Size GetPreferredSize(Size proposedSize)
    {
        TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | (UseMnemonic ? 0 : TextFormatFlags.NoPrefix);
        Size on = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        Size off = OffText == null ? on : TextRenderer.MeasureText(OffText, Font, new Size(int.MaxValue, int.MaxValue), flags);
        int textWidth = Math.Max(on.Width, off.Width);
        Size glyph = GlyphSize;
        int gap = textWidth == 0 ? 0 : Dpi.Px(this, 8);
        return new Size(glyph.Width + gap + textWidth + Padding.Horizontal + Dpi.Px(this, 6),Math.Max(glyph.Height + Dpi.Px(this, 6), on.Height + Dpi.Px(this, 4)) + Padding.Vertical);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        Color backdrop = Dpi.BackdropOf(this);
        g.Clear(backdrop);
        Dpi.Smooth(g);

        bool live = Enabled;
        bool on = CheckState != CheckState.Unchecked;
        Size glyph = GlyphSize;
        int top = (Height - glyph.Height) / 2;
        var box = new Rectangle(Padding.Left, top, glyph.Width - 1, glyph.Height - 1);

        if (isSwitch) DrawSwitch(g, box, on, live, backdrop, p);
        else DrawBox(g, box, on, live, backdrop, p);

        if (Focused && ShowFocusCues && live)
        {
            using var ring = new Pen(p.AccentText, 1.2f) { DashStyle = DashStyle.Dot };
            using GraphicsPath path = Dpi.Rounded(Rectangle.Inflate(box, 3, 3), Dpi.Px(this, 6));
            g.DrawPath(ring, path);
        }

        string text = Shown;
        if (text.Length == 0) return;
        int textLeft = box.Right + Dpi.Px(this, 9);
        Color fore = live ? ForeColor : Theme.Blend(backdrop, p.Muted, 0.4f);
        TextRenderer.DrawText(g, text, Font, new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | (UseMnemonic ? 0 : TextFormatFlags.NoPrefix));
    }

    private void DrawBox(Graphics g, Rectangle box, bool on, bool live, Color backdrop, Theme.Palette p)
    {
        Color fill = on ? (live ? p.Accent : Theme.Blend(backdrop, p.Accent, 0.25f)) : (live ? p.Input : Theme.Blend(backdrop, p.Input, 0.25f));
        Color border = on ? fill : !live ? Theme.Blend(backdrop, p.Border, 0.3f) : hover ? p.Accent : p.BorderStrong;
        using (GraphicsPath path = Dpi.Rounded(box, Dpi.Px(this, 4)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (!on) return;

        float s = box.Width;
        using var tick = new Pen(p.AccentInk, Math.Max(1.8f, Dpi.Px(this, 2f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (CheckState == CheckState.Indeterminate)
        {
            g.DrawLine(tick, box.X + s * 0.26f, box.Y + s * 0.5f, box.X + s * 0.74f, box.Y + s * 0.5f);
            return;
        }
        g.DrawLines(tick, new[]
        {
            new PointF(box.X + s * 0.24f, box.Y + s * 0.52f),
            new PointF(box.X + s * 0.43f, box.Y + s * 0.70f),
            new PointF(box.X + s * 0.76f, box.Y + s * 0.30f)
        });
    }

    private void DrawSwitch(Graphics g, Rectangle box, bool on, bool live, Color backdrop, Theme.Palette p)
    {
        Color track = on ? (live ? p.Accent : Theme.Blend(backdrop, p.Accent, 0.25f)) : (live ? p.SurfaceAlt : Theme.Blend(backdrop, p.SurfaceAlt, 0.25f));
        Color border = on ? track : !live ? Theme.Blend(backdrop, p.Border, 0.3f) : hover ? p.Accent : p.BorderStrong;
        using (GraphicsPath path = Dpi.Rounded(box, box.Height / 2))
        using (var brush = new SolidBrush(track))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        int pad = Dpi.Px(this, 3);
        int d = box.Height - pad * 2;
        int x = on ? box.Right - pad - d : box.X + pad;
        Color thumb = on ? p.AccentInk : live ? p.Muted : Theme.Blend(backdrop, p.Muted, 0.4f);
        using var thumbBrush = new SolidBrush(thumb);
        g.FillEllipse(thumbBrush, x, box.Y + pad, d, d);
    }
}
