// Tether 제어 창의 화면 부품: 캐릭터, 카드, 버튼, 체크 상자, 네트워크 줄.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class Shapes
{
    public static GraphicsPath Rounded(Rectangle area, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(area.Width, area.Height));
        int right = area.Right - diameter;
        int bottom = area.Bottom - diameter;
        var path = new GraphicsPath();
        path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
        path.AddArc(right, area.Top, diameter, diameter, 270, 90);
        path.AddArc(right, bottom, diameter, diameter, 0, 90);
        path.AddArc(area.Left, bottom, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Mix(Color color, Color other, float amount)
    {
        return Color.FromArgb(
            (int)(color.R + (other.R - color.R) * amount),
            (int)(color.G + (other.G - color.G) * amount),
            (int)(color.B + (other.B - color.B) * amount));
    }
}

internal enum MascotMood { Ready, Awake, Searching, Off }

// 앱 아이콘의 캐릭터. 표정으로 현재 상태를 보여 주고, 같은 내용을 글자로도 함께 표시한다.
internal sealed class Mascot : Control
{
    private const float SignalWidth = 5f;
    private MascotMood mood;

    public Mascot(Palette palette)
    {
        DoubleBuffered = true;
        BackColor = palette.Card;
        Size = new Size(Ui.S(92), Ui.S(92));
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Tether 캐릭터";
    }

    public MascotMood Mood
    {
        get { return mood; }
        set
        {
            if (mood == value) return;
            mood = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(Width / 100f, Height / 100f);
        using (var tile = new SolidBrush(Palette.MascotTile))
        using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, 100, 100), 23)) graphics.FillPath(tile, path);
        DrawSignal(graphics);
        using (var brush = new SolidBrush(Palette.Slate))
        using (GraphicsPath path = Shapes.Rounded(new Rectangle(14, 72, 72, 18), 8)) graphics.FillPath(brush, path);
        using (var brush = new SolidBrush(mood == MascotMood.Off ? Palette.Slate : Color.White))
        using (GraphicsPath path = Shapes.Rounded(new Rectangle(14, 32, 72, 50), 10)) graphics.FillPath(brush, path);
        DrawFace(graphics);
    }

    private void DrawSignal(Graphics graphics)
    {
        if (mood == MascotMood.Awake) DrawArc(graphics, Palette.Signal, 22);
        if (mood == MascotMood.Awake || mood == MascotMood.Searching) DrawArc(graphics, mood == MascotMood.Awake ? Palette.Signal : Palette.SignalSearching, 12);
    }

    private static void DrawArc(Graphics graphics, Color color, float radius)
    {
        using (var pen = new Pen(color, SignalWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            graphics.DrawArc(pen, 50 - radius, 31 - radius, radius * 2, radius * 2, 225, 90);
    }

    private void DrawFace(Graphics graphics)
    {
        float lookOffset = mood == MascotMood.Searching ? 4f : 0f;
        bool eyesOpen = mood != MascotMood.Off;
        foreach (float x in new[] { 37f, 63f })
        {
            if (eyesOpen) DrawOpenEye(graphics, x + lookOffset);
            else DrawClosedEye(graphics, x);
        }
        if (mood != MascotMood.Off) DrawCheeks(graphics);
        using (var pen = new Pen(Palette.Ink, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            if (mood == MascotMood.Off) graphics.DrawLine(pen, 45, 66, 55, 66);
            else graphics.DrawArc(pen, 44, 57, 12, 10, 30, 120);
        }
    }

    private static void DrawOpenEye(Graphics graphics, float x)
    {
        using (var ink = new SolidBrush(Palette.Ink)) graphics.FillEllipse(ink, x - 5.5f, 46, 11, 13);
        using (var glint = new SolidBrush(Color.White)) graphics.FillEllipse(glint, x - 3.5f, 47.5f, 4, 4);
    }

    private static void DrawClosedEye(Graphics graphics, float x)
    {
        using (var pen = new Pen(Palette.Ink, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            graphics.DrawArc(pen, x - 6, 46, 12, 10, 20, 140);
    }

    private static void DrawCheeks(Graphics graphics)
    {
        using (var blush = new SolidBrush(Palette.Blush))
        {
            graphics.FillEllipse(blush, 23, 60, 10, 6);
            graphics.FillEllipse(blush, 67, 60, 10, 6);
        }
    }
}

// 켜고 끄는 스위치. 표준 체크 상자라 키보드와 보조 기술 동작은 그대로다.
internal sealed class RoundCheck : CheckBox
{
    private const int TrackWidth = 46;
    private const int TrackHeight = 28;
    private const int TextGap = 12;
    private readonly Palette palette;

    public RoundCheck(Palette palette, string text)
    {
        this.palette = palette;
        Text = text;
        AutoSize = false;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        int textWidth = text.Length == 0 ? 0 : Ui.S(TextGap) + TextRenderer.MeasureText(text, new Font(Ui.FontName, 10f)).Width;
        Size = new Size(Ui.S(TrackWidth) + Ui.S(4) + textWidth, Ui.S(TrackHeight) + Ui.S(6));
    }

    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(Width - Ui.S(TrackWidth) - Ui.S(2), (Height - Ui.S(TrackHeight)) / 2, Ui.S(TrackWidth), Ui.S(TrackHeight));
        using (GraphicsPath path = Shapes.Rounded(track, track.Height / 2))
        {
            using (var brush = new SolidBrush(Checked ? palette.SwitchOn : palette.SwitchOff)) graphics.FillPath(brush, path);
            if (Focused) using (var ring = new Pen(palette.Ring, Ui.S(2))) graphics.DrawPath(ring, path);
        }
        DrawKnob(graphics, track);
        if (Text.Length == 0) return;
        var textArea = new Rectangle(0, 0, track.Left, Height);
        TextRenderer.DrawText(graphics, Text, Font, textArea, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void DrawKnob(Graphics graphics, Rectangle track)
    {
        int inset = Ui.S(3);
        int size = track.Height - inset * 2;
        int left = Checked ? track.Right - inset - size : track.Left + inset;
        using (var brush = new SolidBrush(Color.White)) graphics.FillEllipse(brush, left, track.Top + inset, size, size);
    }
}

// − 값 + 조절기. 누르면 한 칸씩, 꾹 누르면 빠르게 바뀐다.
internal sealed class Stepper : TableLayoutPanel
{
    private const int ButtonSize = 36;
    private const int ValueWidth = 92;
    private const int RepeatDelayMs = 400;
    private const int RepeatMs = 70;

    private readonly Palette palette;
    private readonly int minimum;
    private readonly int maximum;
    private readonly int step;
    private readonly string unit;
    private readonly string zeroText;
    private readonly Label display = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty };
    private readonly ActionButton minus;
    private readonly ActionButton plus;
    private readonly Timer repeat = new Timer();
    private int current;
    private int repeatDirection;

    public Stepper(Palette palette, int minimum, int maximum, int step, string unit, string zeroText)
    {
        this.palette = palette;
        this.minimum = minimum;
        this.maximum = maximum;
        this.step = step;
        this.unit = unit;
        this.zeroText = zeroText;
        AutoSize = true;
        Anchor = AnchorStyles.Right;
        Margin = Padding.Empty;
        ColumnCount = 3;
        RowCount = 1;
        BackColor = palette.Card;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(ValueWidth)));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(ButtonSize)));

        minus = StepButton("−", "줄이기", -1);
        plus = StepButton("+", "늘리기", 1);
        display.Font = new Font(Ui.FontName, 10f, FontStyle.Bold);
        display.ForeColor = palette.Text;
        display.BackColor = palette.Card;
        Controls.Add(minus, 0, 0);
        Controls.Add(display, 1, 0);
        Controls.Add(plus, 2, 0);
        repeat.Tick += (sender, e) =>
        {
            repeat.Interval = RepeatMs;
            Value = current + repeatDirection * step;
        };
        Value = minimum;
    }

    public int Value
    {
        get { return current; }
        set
        {
            current = Math.Max(minimum, Math.Min(maximum, value));
            display.Text = current == 0 && zeroText != null ? zeroText : current + unit;
        }
    }

    private ActionButton StepButton(string text, string accessibleName, int direction)
    {
        var button = new ActionButton(palette.Ring, ButtonSize, ButtonSize)
        {
            Text = text,
            AccessibleName = accessibleName,
            AutoSize = false,
            Size = new Size(Ui.S(ButtonSize), Ui.S(ButtonSize)),
            Font = new Font(Ui.FontName, 12f, FontStyle.Bold),
            BackColor = palette.Background,
            ForeColor = palette.Text
        };
        button.MouseDown += (sender, e) => StartRepeat(direction, e);
        button.MouseUp += (sender, e) => repeat.Stop();
        // 마우스는 누르는 순간 이미 바뀌었으므로 키보드로 누른 경우만 처리한다.
        button.Click += (sender, e) => { if (!(e is MouseEventArgs)) Value = current + direction * step; };
        return button;
    }

    private void StartRepeat(int direction, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Value = current + direction * step;
        repeatDirection = direction;
        repeat.Interval = RepeatDelayMs;
        repeat.Start();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Color color = Enabled ? palette.Text : palette.Muted;
        display.ForeColor = color;
        minus.ForeColor = color;
        plus.ForeColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) repeat.Dispose();
        base.Dispose(disposing);
    }
}

// 둥근 모서리 배경. 자식 컨트롤은 같은 카드 색을 쓴다.
internal sealed class RoundedCard : Panel
{
    private const int Radius = 14;
    private readonly Color fill;

    public RoundedCard(Color fill, Color background)
    {
        this.fill = fill;
        BackColor = background;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Shapes.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(Radius)))
        using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
    }
}

// 알약 모양 버튼. 올렸을 때 색이 바뀌고, 키보드로 이동하면 테두리가 보인다.
internal sealed class ActionButton : Button
{
    private const float HoverMix = 0.12f;
    private readonly Color ring;
    private bool isHovered;

    public ActionButton(Color ring, int minWidth, int minHeight)
    {
        this.ring = ring;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = true;
        MinimumSize = new Size(Ui.S(minWidth), Ui.S(minHeight));
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(1, 1, Width - 3, Height - 3);
        Color fill = isHovered ? Shapes.Mix(BackColor, ForeColor, HoverMix) : BackColor;
        using (GraphicsPath path = Shapes.Rounded(area, area.Height / 2))
        {
            using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
            if (Focused) using (var pen = new Pen(ring, Ui.S(2))) graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// 네트워크 한 줄: 순위, 이름과 상태, 순서 버튼, 빼기 버튼.
internal sealed class NetworkRow : TableLayoutPanel
{
    private const int RankWidth = 58;
    private const int ArrowWidth = 34;
    private const int RowHeight = 60;

    private readonly Palette palette;
    private bool showDivider;
    private readonly Label rank = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
    private readonly Label name = new Label { AutoSize = true, Margin = Padding.Empty };
    private readonly Label status = new Label { AutoSize = true, Margin = Padding.Empty };
    private readonly ActionButton up;
    private readonly ActionButton down;
    private readonly ActionButton remove;

    public string NetworkName { get; private set; }

    public NetworkRow(Palette palette, Action<NetworkRow> onRemove, Action<NetworkRow, int> onMove)
    {
        this.palette = palette;
        Dock = DockStyle.Top;
        Height = Ui.S(RowHeight);
        Margin = Padding.Empty;
        BackColor = palette.Card;
        ColumnCount = 5;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(RankWidth)));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(ArrowWidth)));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(ArrowWidth)));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(ArrowWidth)));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        up = SmallButton("▲", "순위 올리기", palette.Link);
        down = SmallButton("▼", "순위 내리기", palette.Link);
        remove = SmallButton("✕", "목록에서 빼기", palette.Muted);
        rank.Font = new Font(Ui.FontName, 10f, FontStyle.Bold);
        rank.ForeColor = palette.Good;
        name.ForeColor = palette.Text;
        status.Font = new Font(Ui.FontName, 9f);
        status.Margin = new Padding(0, Ui.S(1), 0, 0);
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = palette.Card, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        text.Controls.Add(name);
        text.Controls.Add(status);
        Controls.Add(rank, 0, 0);
        Controls.Add(text, 1, 0);
        Controls.Add(up, 2, 0);
        Controls.Add(down, 3, 0);
        Controls.Add(remove, 4, 0);

        remove.Click += (sender, e) => onRemove(this);
        up.Click += (sender, e) => onMove(this, -1);
        down.Click += (sender, e) => onMove(this, 1);
    }

    private ActionButton SmallButton(string text, string accessibleName, Color color)
    {
        return new ActionButton(palette.Ring, ArrowWidth, 32)
        {
            Text = text,
            AccessibleName = accessibleName,
            Font = new Font(Ui.FontName, 8f),
            BackColor = palette.Card,
            ForeColor = color,
            Anchor = AnchorStyles.None
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!showDivider) return;
        using (var pen = new Pen(palette.Divider)) e.Graphics.DrawLine(pen, Ui.S(RankWidth), Height - 1, Width, Height - 1);
    }

    public void Bind(string networkName, int order, int count, VisibleNetwork network, bool hasRowBelow)
    {
        NetworkName = networkName;
        showDivider = hasRowBelow;
        Invalidate();
        rank.Text = (order + 1) + "순위";
        name.Text = networkName;
        status.Text = network == null ? "안 보임" : network.Connected ? "● 연결됨" : "주변에 있음";
        status.ForeColor = network != null && network.Connected ? palette.Good : palette.Muted;
        up.Visible = order > 0;
        down.Visible = order < count - 1;
    }
}
