// 시간 설정 창: 끊김 유지, 입력 없을 때 절전, 상위 전환, 화면 자동 끄기.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal sealed class SettingsForm : Form
{
    private const int MaxScreenMinutes = 240;
    private const int DefaultScreenBattery = 5;
    private const int DefaultScreenPlugged = 10;
    private const int SecondsPerMinute = 60;
    private const int MinCoolPercent = 50;
    private const int MaxCoolPercent = 95;
    private const int DefaultCoolPercent = 70;
    private const int SwitchStep = 5;
    private const int CoolStep = 5;
    private const string MinuteUnit = "분";
    private const string ScreenNeverText = "끄지 않음";

    private readonly Palette palette;
    private readonly Action openLog;
    private readonly RoundCheck coolCheck;
    private readonly Stepper coolInput;
    private readonly Font smallFont = new Font(Ui.FontName, 9f);
    private readonly Stepper graceInput;
    private readonly Stepper idleInput;
    private readonly Stepper switchInput;
    private readonly Stepper screenBatteryInput;
    private readonly Stepper screenPluggedInput;
    private readonly Label error = new Label { AutoSize = true, Visible = false };

    public SettingsForm(Palette palette, Icon icon, Action openLog)
    {
        this.palette = palette;
        this.openLog = openLog;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Tether 설정";
        Icon = icon;
        Font = new Font(Ui.FontName, 10f);
        BackColor = palette.Background;
        ForeColor = palette.Text;
        ClientSize = new Size(Ui.S(500), Ui.S(560));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        graceInput = new Stepper(palette, 0, TimingSettings.MaxGraceMinutes, 1, MinuteUnit, null);
        idleInput = new Stepper(palette, TimingSettings.MinIdleMinutes, TimingSettings.MaxIdleMinutes, 1, MinuteUnit, null);
        switchInput = new Stepper(palette, TimingSettings.SwitchOff, TimingSettings.MaxSwitchSeconds, SwitchStep, "초", "끄기");
        screenBatteryInput = new Stepper(palette, 0, MaxScreenMinutes, 1, MinuteUnit, ScreenNeverText);
        screenPluggedInput = new Stepper(palette, 0, MaxScreenMinutes, 1, MinuteUnit, ScreenNeverText);
        coolCheck = new RoundCheck(palette, "배터리 저발열") { ForeColor = palette.Text };
        coolInput = new Stepper(palette, MinCoolPercent, MaxCoolPercent, CoolStep, "%", null);
        coolCheck.Click += (sender, e) => coolInput.Enabled = coolCheck.Checked;
        Controls.Add(BuildLayout());
        LoadValues();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleTitleBar(this, palette);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = Ui.Pad(20, 20, 20, 16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildCard(), 0, 0);
        error.ForeColor = palette.Bad;
        error.Margin = Ui.Pad(0, 10, 0, 0);
        root.Controls.Add(error, 0, 1);
        root.Controls.Add(BuildLinks(), 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        return root;
    }

    private Control BuildCard()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = palette.Card, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(grid, "끊김 유지", "연결이 끊겨도 작업을 유지", graceInput);
        AddRow(grid, "입력 없을 때 절전", "네트워크 없을 때, 배터리에서만", idleInput);
        AddRow(grid, "상위 네트워크 전환", "켜면 연결 중에도 더 높은 순위로 옮김", switchInput);
        AddRow(grid, "화면 끄기 (배터리)", "입력이 없을 때 화면만 끔", screenBatteryInput);
        AddRow(grid, "화면 끄기 (전원 연결)", "입력이 없을 때 화면만 끔", screenPluggedInput);
        AddCoolRow(grid);

        var card = new RoundedCard(palette.Card, palette.Background) { Dock = DockStyle.Fill, Padding = Ui.Pad(18, 12, 12, 12), Margin = Padding.Empty };
        card.Controls.Add(grid);
        return card;
    }

    private void AddRow(TableLayoutPanel grid, string title, string hint, Stepper input)
    {
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = palette.Card, Margin = Ui.Pad(0, 8, 8, 8) };
        text.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = palette.Text, Margin = Padding.Empty });
        text.Controls.Add(new Label { Text = hint, AutoSize = true, Font = smallFont, ForeColor = palette.Muted, Margin = new Padding(0, Ui.S(2), 0, 0) });
        input.AccessibleName = title;
        int row = grid.RowCount++;
        grid.Controls.Add(text, 0, row);
        grid.Controls.Add(input, 1, row);
    }

    private void AddCoolRow(TableLayoutPanel grid)
    {
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = palette.Card, Margin = Ui.Pad(0, 8, 8, 8) };
        text.Controls.Add(coolCheck);
        text.Controls.Add(new Label { Text = "배터리에서 터보를 끄고 CPU를 제한", AutoSize = true, Font = smallFont, ForeColor = palette.Muted, Margin = new Padding(0, Ui.S(2), 0, 0) });
        coolInput.AccessibleName = "배터리 CPU 최대 성능";
        int row = grid.RowCount++;
        grid.Controls.Add(text, 0, row);
        grid.Controls.Add(coolInput, 1, row);
    }

    private Control BuildLinks()
    {
        var log = new ActionButton(palette.Ring, 96, 40) { Text = "기록 보기", BackColor = palette.Background, ForeColor = palette.Link };
        var removeHint = new Label { Text = "제거: Tether.exe가 있는 폴더를 지우세요", AutoSize = true, Font = smallFont, ForeColor = palette.Muted, Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 0, 0) };
        log.Click += (sender, e) => openLog();
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Ui.Pad(0, 8, 0, 0) };
        row.Controls.Add(log);
        row.Controls.Add(removeHint);
        return row;
    }

    private Control BuildButtons()
    {
        var reset = new ActionButton(palette.Ring, 110, 42) { Text = "기본값으로", BackColor = palette.Background, ForeColor = palette.Link };
        var cancel = new ActionButton(palette.Ring, 84, 42) { Text = "취소", BackColor = palette.Card, ForeColor = palette.Text, Margin = new Padding(0, 0, Ui.S(8), 0) };
        var save = new ActionButton(palette.Ring, 100, 42) { Text = "저장", BackColor = palette.Primary, ForeColor = palette.OnPrimary };
        reset.Click += (sender, e) => ShowValues(TimingSettings.Default, DefaultScreenBattery, DefaultScreenPlugged);
        cancel.Click += (sender, e) => Close();
        save.Click += (sender, e) => SaveValues();
        CancelButton = cancel;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = Ui.Pad(0, 14, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(reset, 0, 0);
        grid.Controls.Add(cancel, 2, 0);
        grid.Controls.Add(save, 3, 0);
        return grid;
    }

    private void LoadValues()
    {
        Tuple<uint, uint> screen = PowerReader.ScreenOffSeconds();
        ShowValues(TimingSettings.Load(), (int)screen.Item2 / SecondsPerMinute, (int)screen.Item1 / SecondsPerMinute);
        bool coolOn = PowerReader.IsCoolModeOn();
        int percent = PowerReader.CoolPercent();
        coolCheck.Checked = coolOn;
        coolInput.Enabled = coolOn;
        coolInput.Value = percent >= MinCoolPercent && percent <= MaxCoolPercent ? percent : DefaultCoolPercent;
    }

    private void ShowValues(TimingSettings timing, int screenBattery, int screenPlugged)
    {
        graceInput.Value = timing.GraceMinutes;
        idleInput.Value = timing.IdleMinutes;
        switchInput.Value = timing.SwitchSeconds;
        screenBatteryInput.Value = Math.Min(screenBattery, MaxScreenMinutes);
        screenPluggedInput.Value = Math.Min(screenPlugged, MaxScreenMinutes);
    }

    private void SaveValues()
    {
        try
        {
            new TimingSettings(graceInput.Value, idleInput.Value, switchInput.Value).Save();
            bool applied = PowerReader.SetScreenOffSeconds(screenPluggedInput.Value * SecondsPerMinute, screenBatteryInput.Value * SecondsPerMinute)
                && PowerReader.SetCoolMode(coolCheck.Checked, coolInput.Value);
            if (applied) Close();
            else ShowError("시간은 저장했지만 전원 설정을 바꾸지 못했어요.");
        }
        catch (IOException failure) { ShowError("설정을 저장하지 못했어요: " + failure.Message); }
    }

    private void ShowError(string message)
    {
        error.Text = "▲ " + message;
        error.Visible = true;
    }
}
