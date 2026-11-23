using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// A compact multiple choice: every option is a small pill that is on (yellow dot with a tick) or off (empty ring).
// The pills wrap onto as many rows as the width needs, so a long list takes little room.
internal sealed class ChipList : Control
{
    internal sealed class Chip
    {
        public Chip(string id, string text, string? tip = null)
        {
            Id = id;
            Text = text;
            Tip = tip;
        }

        public string Id { get; }
        public string Text { get; }
        public string? Tip { get; }
        public bool Checked { get; set; }
    }

    private readonly List<Chip> chips = new List<Chip>();
    private readonly ToolTip tip = new ToolTip { ShowAlways = true };
    private int hot = -1;
    private int focusIndex = -1;

    public event Action? Changed;

    public ChipList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Height = LogicalToDeviceUnits(30);
    }

    public IReadOnlyList<Chip> Chips => chips;

    public int CheckedCount => chips.Count(c => c.Checked);

    public void Add(Chip chip)
    {
        chips.Add(chip);
        Relayout();
    }

    public void SetChecked(string id, bool value)
    {
        Chip? chip = chips.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (chip == null || chip.Checked == value) return;
        chip.Checked = value;
        Invalidate();
    }

    public void SetAll(bool value)
    {
        bool changed = false;
        foreach (Chip chip in chips)
        {
            if (chip.Checked == value) continue;
            chip.Checked = value;
            changed = true;
        }
        if (!changed) return;
        Invalidate();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ layout

    private int ChipHeight => Dpi.Px(this, 28);
    private int Gap => Dpi.Px(this, 6);

    private Rectangle[] Place(int width)
    {
        var rects = new Rectangle[chips.Count];
        int x = 0, y = 0;
        for (int i = 0; i < chips.Count; i++)
        {
            int w = TextRenderer.MeasureText(chips[i].Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + Dpi.Px(this, 40);
            if (x > 0 && x + w > width)
            {
                x = 0;
                y += ChipHeight + Gap;
            }
            rects[i] = new Rectangle(x, y, w, ChipHeight);
            x += w + Gap;
        }
        return rects;
    }

    private int HeightFor(int width)
    {
        if (chips.Count == 0) return ChipHeight;
        Rectangle[] rects = Place(Math.Max(Dpi.Px(this, 80), width));
        return rects[^1].Bottom + 1;
    }

    private void Relayout()
    {
        int height = HeightFor(Width);
        if (Height != height) Height = height;
        Invalidate();
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, width, chips.Count == 0 ? height : HeightFor(width), specified);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new Size(proposedSize.Width > 0 ? proposedSize.Width : Width, HeightFor(proposedSize.Width > 0 ? proposedSize.Width : Width));

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Relayout();
    }

    // ------------------------------------------------------------------ input

    private int ChipAt(Point point)
    {
        Rectangle[] rects = Place(Width);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].Contains(point)) return i;
        }
        return -1;
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= chips.Count) return;
        chips[index].Checked = !chips[index].Checked;
        Invalidate();
        Changed?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = ChipAt(e.Location);
        if (index == hot) return;
        hot = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        tip.SetToolTip(this, index >= 0 ? chips[index].Tip ?? "" : "");
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hot = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = ChipAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            Focus();
            focusIndex = index;
            Toggle(index);
        }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (chips.Count == 0) return;
        if (focusIndex < 0) focusIndex = 0;
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                focusIndex = Math.Max(0, focusIndex - 1);
                e.Handled = true;
                break;
            case Keys.Right or Keys.Down:
                focusIndex = Math.Min(chips.Count - 1, focusIndex + 1);
                e.Handled = true;
                break;
            case Keys.Space or Keys.Enter:
                Toggle(focusIndex);
                e.Handled = true;
                break;
        }
        Invalidate();
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        if (focusIndex < 0 && chips.Count > 0) focusIndex = 0;
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        Color backdrop = Dpi.BackdropOf(this);
        g.Clear(backdrop);
        Dpi.Smooth(g);

        Rectangle[] rects = Place(Width);
        for (int i = 0; i < chips.Count; i++)
        {
            Chip chip = chips[i];
            Rectangle rect = new Rectangle(rects[i].X, rects[i].Y, rects[i].Width - 1, rects[i].Height - 1);
            bool isHot = i == hot;
            Color fill = chip.Checked ? Theme.Blend(backdrop, p.Accent, Theme.Dark ? 0.16f : 0.30f) : p.Input;
            Color border = chip.Checked ? p.Accent : isHot ? p.BorderStrong : p.Border;
            using (GraphicsPath path = Dpi.Rounded(rect, rect.Height / 2))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            int d = Dpi.Px(this, 14);
            var dot = new Rectangle(rect.X + Dpi.Px(this, 9), rect.Y + (rect.Height - d) / 2, d, d);
            if (chip.Checked)
            {
                using var brush = new SolidBrush(p.Accent);
                g.FillEllipse(brush, dot);
                using var tick = new Pen(p.AccentInk, Math.Max(1.6f, Dpi.Px(this, 1.8f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(dot.X + d * 0.26f, dot.Y + d * 0.54f),
                    new PointF(dot.X + d * 0.44f, dot.Y + d * 0.72f),
                    new PointF(dot.X + d * 0.76f, dot.Y + d * 0.32f)
                });
            }
            else
            {
                using var ring = new Pen(p.BorderStrong, 1.4f);
                g.DrawEllipse(ring, dot);
            }

            TextRenderer.DrawText(g, chip.Text, Font, new Rectangle(dot.Right + Dpi.Px(this, 7), rect.Y, rect.Width - d - Dpi.Px(this, 24), rect.Height),
                chip.Checked ? p.Text : p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues && i == focusIndex)
            {
                using var ring = new Pen(p.AccentText) { DashStyle = DashStyle.Dot };
                using GraphicsPath path = Dpi.Rounded(Rectangle.Inflate(rect, 2, 2), rect.Height / 2 + 2);
                g.DrawPath(ring, path);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
