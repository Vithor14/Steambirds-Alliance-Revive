using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Number field drawn by the launcher (replaces NumericUpDown, whose spin buttons stay bright in the dark theme).
// Same members the launcher used: Value, Minimum, Maximum, DecimalPlaces, Increment, ThousandsSeparator, TextAlign,
// ValueChanged. A value typed outside the limits is not accepted; the field goes back to the last good value when it
// loses the focus. The mouse wheel only changes the number while the field has the focus (otherwise the page scrolls).
internal sealed class NumberBox : Control
{
    private const int WmMouseWheel = 0x020A;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    // The text box inside: its wheel messages belong to the NumberBox.
    private sealed class EditBox : TextBox
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel && Parent != null)
            {
                SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }
    }

    private readonly EditBox edit;
    private readonly System.Windows.Forms.Timer repeat;
    private decimal value;
    private decimal minimum;
    private decimal maximum = 100m;
    private decimal increment = 1m;
    private int decimals;
    private bool thousands;
    private bool hover;
    private int hotButton;
    private int pressedButton;
    private bool updatingText;

    public event EventHandler? ValueChanged;

    public NumberBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.ContainerControl, true);
        edit = new EditBox { BorderStyle = BorderStyle.None };
        Controls.Add(edit);
        edit.TextChanged += (_, _) => OnTyped();
        edit.GotFocus += (_, _) => Invalidate();
        edit.LostFocus += (_, _) => Invalidate();
        edit.Validating += (_, _) => CommitText();
        edit.Leave += (_, _) => CommitText();
        edit.KeyDown += OnEditKeyDown;
        edit.MouseEnter += (_, _) => UpdateHover();
        edit.MouseLeave += (_, _) => UpdateHover();

        repeat = new System.Windows.Forms.Timer { Interval = 400 };
        repeat.Tick += (_, _) =>
        {
            repeat.Interval = 70;
            if (pressedButton != 0) Step(pressedButton);
        };

        WriteText();
        Size = new Size(LogicalToDeviceUnits(90), FixedHeight());
    }

    public decimal Value
    {
        get => value;
        set => SetValue(value, rewriteText: true);
    }

    public decimal Minimum
    {
        get => minimum;
        set
        {
            minimum = value;
            if (maximum < minimum) maximum = minimum;
            SetValue(this.value, rewriteText: true);
        }
    }

    public decimal Maximum
    {
        get => maximum;
        set
        {
            maximum = value;
            if (minimum > maximum) minimum = maximum;
            SetValue(this.value, rewriteText: true);
        }
    }

    public decimal Increment
    {
        get => increment;
        set => increment = value <= 0 ? 1m : value;
    }

    public int DecimalPlaces
    {
        get => decimals;
        set
        {
            decimals = Math.Clamp(value, 0, 10);
            SetValue(this.value, rewriteText: true);
        }
    }

    public bool ThousandsSeparator
    {
        get => thousands;
        set
        {
            thousands = value;
            WriteText();
        }
    }

    public HorizontalAlignment TextAlign
    {
        get => edit.TextAlign;
        set => edit.TextAlign = value;
    }

    // False (like NumericUpDown): a number typed by hand counts when the field loses the focus or Enter is pressed.
    // True: it counts while it is typed (for screens with a Save button, which wake up as soon as something changes).
    public bool CommitWhileTyping { get; set; }

    public bool ReadOnly
    {
        get => edit.ReadOnly;
        set
        {
            edit.ReadOnly = value;
            Invalidate();
        }
    }

    public override string Text => edit.Text;

    // ------------------------------------------------------------------ value

    private void SetValue(decimal wanted, bool rewriteText)
    {
        decimal next = Math.Clamp(Math.Round(wanted, decimals, MidpointRounding.AwayFromZero), minimum, maximum);
        bool changed = next != value;
        value = next;
        if (rewriteText) WriteText();
        if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private string Format(decimal number) => number.ToString((thousands ? "N" : "F") + decimals, CultureInfo.CurrentCulture);

    private void WriteText()
    {
        string text = Format(value);
        if (edit.Text == text) return;
        updatingText = true;
        try
        {
            edit.Text = text;
        }
        finally
        {
            updatingText = false;
        }
    }

    private bool TryParse(string text, out decimal number) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out number);

    // While typing (CommitWhileTyping): a good number inside the limits counts at once, the text stays as typed.
    private void OnTyped()
    {
        if (updatingText || !CommitWhileTyping) return;
        if (TryParse(edit.Text, out decimal typed) && typed >= minimum && typed <= maximum) SetValue(typed, rewriteText: false);
    }

    private void CommitText()
    {
        if (TryParse(edit.Text, out decimal typed)) SetValue(typed, rewriteText: true);
        else WriteText();
        if (edit.Text != Format(value)) WriteText();
    }

    private void Step(int direction)
    {
        if (ReadOnly || !Enabled) return;
        CommitText();
        SetValue(value + increment * direction, rewriteText: true);
        edit.SelectionStart = edit.TextLength;
    }

    // ------------------------------------------------------------------ layout

    private int FixedHeight() => Math.Max(Dpi.Px(this, 26), edit.PreferredHeight + Dpi.Px(this, 8));

    private int SpinWidth => Dpi.Px(this, 20);

    private Rectangle SpinBounds => new Rectangle(Width - SpinWidth - 2, 2, SpinWidth, Height - 4);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, width, FixedHeight(), specified);
    }

    public override Size GetPreferredSize(Size proposedSize) => new Size(Math.Max(Width, Dpi.Px(this, 60)), FixedHeight());

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int padX = Dpi.Px(this, 8);
        int width = Math.Max(10, Width - padX - SpinWidth - Dpi.Px(this, 6));
        edit.SetBounds(padX, Math.Max(0, (Height - edit.Height) / 2), width, edit.Height);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        edit.Font = Font;
        Height = FixedHeight();
        PerformLayout();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        edit.BackColor = BackColor;
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        edit.ForeColor = ForeColor;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        edit.Enabled = Enabled;
        Invalidate();
    }

    // ------------------------------------------------------------------ input

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                Step(1);
                e.Handled = true;
                break;
            case Keys.Down:
                Step(-1);
                e.Handled = true;
                break;
            case Keys.PageUp:
                SetValue(value + increment * 10, rewriteText: true);
                e.Handled = true;
                break;
            case Keys.PageDown:
                SetValue(value - increment * 10, rewriteText: true);
                e.Handled = true;
                break;
            case Keys.Enter:
                CommitText();
                break;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseWheel)
        {
            if (edit.Focused)
            {
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                Step(delta > 0 ? 1 : -1);
                return;
            }
            if (Parent != null)
            {
                SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
                return;
            }
        }
        base.WndProc(ref m);
    }

    private int ButtonAt(Point point)
    {
        Rectangle spin = SpinBounds;
        if (!spin.Contains(point) || ReadOnly || !Enabled) return 0;
        return point.Y < spin.Y + spin.Height / 2 ? 1 : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int button = ButtonAt(e.Location);
        if (button != hotButton)
        {
            hotButton = button;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int button = ButtonAt(e.Location);
        if (e.Button == MouseButtons.Left && button != 0)
        {
            pressedButton = button;
            Step(button);
            repeat.Interval = 400;
            repeat.Start();
            Invalidate();
        }
        else if (e.Button == MouseButtons.Left)
        {
            edit.Focus();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressedButton = 0;
        repeat.Stop();
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        UpdateHover();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hotButton = 0;
        UpdateHover();
        base.OnMouseLeave(e);
    }

    private void UpdateHover()
    {
        bool now = IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));
        if (now == hover) return;
        hover = now;
        Invalidate();
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        Color backdrop = Dpi.BackdropOf(this);
        g.Clear(backdrop);
        Dpi.Smooth(g);

        bool live = Enabled;
        Color fill = live ? p.Input : Theme.Blend(backdrop, p.Input, 0.25f);
        Color border = !live ? Theme.Blend(backdrop, p.Border, 0.3f) : edit.Focused ? p.Accent : hover ? p.BorderStrong : p.Border;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = Dpi.Rounded(bounds, Dpi.Px(this, 4)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (!live || ReadOnly) return;

        Rectangle spin = SpinBounds;
        int mid = spin.Y + spin.Height / 2;
        using (var divider = new Pen(Theme.Blend(p.Input, p.Border, 0.8f))) g.DrawLine(divider, spin.X, spin.Y + 2, spin.X, spin.Bottom - 2);
        DrawArrow(g, new Rectangle(spin.X + 1, spin.Y, spin.Width - 1, mid - spin.Y), up: true, hotButton == 1, pressedButton == 1, p);
        DrawArrow(g, new Rectangle(spin.X + 1, mid, spin.Width - 1, spin.Bottom - mid), up: false, hotButton == -1, pressedButton == -1, p);
    }

    private void DrawArrow(Graphics g, Rectangle area, bool up, bool hot, bool down, Theme.Palette p)
    {
        if (hot)
        {
            using var back = new SolidBrush(down ? p.Border : p.ButtonHover);
            g.FillRectangle(back, area);
        }
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float w = Dpi.Px(this, 3.2f);
        float h = Dpi.Px(this, 1.8f);
        PointF[] points = up
            ? new[] { new PointF(cx - w, cy + h), new PointF(cx, cy - h), new PointF(cx + w, cy + h) }
            : new[] { new PointF(cx - w, cy - h), new PointF(cx, cy + h), new PointF(cx + w, cy - h) };
        using var pen = new Pen(hot ? p.AccentText : p.Muted, Math.Max(1.4f, Dpi.Px(this, 1.4f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLines(pen, points);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            repeat.Stop();
            repeat.Dispose();
        }
        base.Dispose(disposing);
    }
}
