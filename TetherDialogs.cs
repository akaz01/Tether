// 보조 창: 네트워크 추가, 기록 보기.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class AddNetworkForm : Form
{
    private const int RowHeight = 46;
    private const int RescanWaitMs = 4000;
    private const string RefreshText = "새로고침";

    private readonly Palette palette;
    private readonly string[] selected;
    private readonly ListBox list = new ListBox
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false,
        AccessibleName = "추가할 수 있는 네트워크"
    };
    private readonly Label emptyLabel = new Label { Text = "추가할 네트워크가 없어요.\nWindows에서 핫스팟이나 와이파이에 먼저 한 번 연결해 주세요.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
    private readonly Timer rescanTimer = new Timer { Interval = RescanWaitMs };
    private readonly ActionButton refreshButton;
    private VisibleNetwork[] visible = new VisibleNetwork[0];

    public string ChosenName { get; private set; }

    public AddNetworkForm(Palette palette, Icon icon, string[] selected)
    {
        this.palette = palette;
        this.selected = selected;
        AutoScaleMode = AutoScaleMode.None;
        Text = "네트워크 추가";
        Icon = icon;
        Font = new Font(Ui.FontName, 10f);
        BackColor = palette.Background;
        ForeColor = palette.Text;
        ClientSize = new Size(Ui.S(420), Ui.S(460));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        refreshButton = new ActionButton(palette.Ring, 84, 40) { Text = RefreshText, BackColor = palette.Background, ForeColor = palette.Link };
        var cancel = new ActionButton(palette.Ring, 84, 40) { Text = "닫기", BackColor = palette.Card, ForeColor = palette.Text };
        refreshButton.Click += (sender, e) => StartRescan();
        cancel.Click += (sender, e) => Close();
        CancelButton = cancel;
        rescanTimer.Tick += (sender, e) => FinishRescan();
        FormClosed += (sender, e) => rescanTimer.Dispose();
        list.ItemHeight = Ui.S(RowHeight);
        list.BackColor = palette.Card;
        list.DrawItem += DrawNetwork;
        list.MouseClick += (sender, e) => Choose(list.IndexFromPoint(e.Location));
        list.KeyDown += (sender, e) => { if (e.KeyCode == Keys.Enter) Choose(list.SelectedIndex); };
        Controls.Add(BuildLayout(cancel));
        LoadNetworks();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleTitleBar(this, palette);
    }

    private Control BuildLayout(Control cancel)
    {
        var card = new RoundedCard(palette.Card, palette.Background) { Dock = DockStyle.Fill, Padding = Ui.Pad(14, 8, 10, 8), Margin = Padding.Empty };
        emptyLabel.BackColor = palette.Card;
        emptyLabel.ForeColor = palette.Muted;
        card.Controls.Add(list);
        card.Controls.Add(emptyLabel);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Ui.Pad(0, 12, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(refreshButton, 0, 0);
        buttons.Controls.Add(cancel, 2, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Ui.Pad(20, 16, 20, 14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "이 컴퓨터에 저장된 네트워크입니다. 누르면 추가됩니다.", AutoSize = true, ForeColor = palette.Muted, Margin = Ui.Pad(4, 0, 0, 10) }, 0, 0);
        root.Controls.Add(card, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        return root;
    }

    private void LoadNetworks()
    {
        visible = ReadVisible();
        string[] names = ReadSaved().Where(Networks.IsSupported).Except(selected)
            .OrderByDescending(name => visible.Any(network => network.Name == name))
            .ThenBy(name => name)
            .ToArray();
        list.Items.Clear();
        list.Items.AddRange(names.Cast<object>().ToArray());
        list.Visible = names.Length > 0;
        emptyLabel.Visible = names.Length == 0;
    }

    private static VisibleNetwork[] ReadVisible()
    {
        try { return Wlan.Visible(); }
        catch (InvalidOperationException) { return new VisibleNetwork[0]; }
    }

    private static string[] ReadSaved()
    {
        try { return Wlan.SavedProfiles(); }
        catch (InvalidOperationException) { return new string[0]; }
    }

    private void DrawNetwork(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        string name = (string)list.Items[e.Index];
        VisibleNetwork network = visible.FirstOrDefault(item => item.Name == name);
        string status = network == null ? "안 보임" : network.Connected ? "● 연결됨" : "주변에 있음";
        Color statusColor = network != null && network.Connected ? palette.Good : palette.Muted;
        bool isSelected = (e.State & DrawItemState.Selected) != 0;
        using (var brush = new SolidBrush(isSelected ? palette.Background : palette.Card)) e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, name, Font, e.Bounds, palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, status, Font, e.Bounds, statusColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        using (var pen = new Pen(palette.Divider)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= list.Items.Count) return;
        ChosenName = (string)list.Items[index];
        Close();
    }

    private void StartRescan()
    {
        refreshButton.Enabled = false;
        refreshButton.Text = "찾는 중";
        try { Wlan.Scan(); }
        catch (InvalidOperationException error) { Log.Write("검색 실패: " + error.Message); }
        rescanTimer.Start();
    }

    private void FinishRescan()
    {
        rescanTimer.Stop();
        refreshButton.Text = RefreshText;
        refreshButton.Enabled = true;
        LoadNetworks();
    }
}

internal sealed class LogForm : Form
{
    private const int MaxLines = 200;
    private const int RefreshMs = 2000;

    private readonly Palette palette;
    private readonly Timer timer = new Timer { Interval = RefreshMs };
    private readonly TextBox console = new TextBox
    {
        Multiline = true,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        AccessibleName = "기록"
    };
    private string shownLog = "";

    public LogForm(Palette palette, Icon icon)
    {
        this.palette = palette;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Tether 기록";
        Icon = icon;
        Font = new Font(Ui.FontName, 9.5f);
        BackColor = palette.Background;
        ClientSize = new Size(Ui.S(560), Ui.S(420));
        MinimumSize = new Size(Ui.S(420), Ui.S(300));
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        console.BackColor = palette.Card;
        console.ForeColor = palette.Text;
        var card = new RoundedCard(palette.Card, palette.Background) { Dock = DockStyle.Fill, Padding = Ui.Pad(14, 12, 8, 12) };
        card.Controls.Add(console);
        var root = new Panel { Dock = DockStyle.Fill, Padding = Ui.Pad(20, 16, 20, 16) };
        root.Controls.Add(card);
        Controls.Add(root);

        timer.Tick += (sender, e) => RefreshLog();
        Shown += (sender, e) => { RefreshLog(); timer.Start(); };
        FormClosed += (sender, e) => timer.Dispose();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleTitleBar(this, palette);
    }

    private void RefreshLog()
    {
        string text = ReadLogTail();
        if (text == shownLog) return;
        shownLog = text;
        console.Text = text;
        console.SelectionStart = console.TextLength;
        console.ScrollToCaret();
    }

    private static string ReadLogTail()
    {
        try
        {
            if (!File.Exists(Log.FilePath)) return "아직 기록이 없습니다.";
            using (var stream = new FileStream(Log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string[] lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - MaxLines)));
            }
        }
        catch (IOException error) { return "기록을 읽지 못했습니다: " + error.Message; }
    }
}
