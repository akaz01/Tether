// TetherService의 제어 창: 상태 표시, 네트워크 우선순위, 시작·중지, 기록.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Ui
{
    public const string FontName = "Malgun Gothic";
    private static readonly float Factor = ReadFactor();

    private static float ReadFactor()
    {
        using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
    }

    // 96 DPI 기준 픽셀을 현재 화면 배율로 바꾼다.
    public static int S(int pixels) { return (int)Math.Round(pixels * Factor); }
    public static Padding Pad(int left, int top, int right, int bottom) { return new Padding(S(left), S(top), S(right), S(bottom)); }

    private const int CaptionColorAttribute = 35;
    private const int CornerPreferenceAttribute = 33;
    private const int RoundCornerPreference = 2;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    // Windows 11의 둥근 창 모서리를 쓴다. 지원하지 않는 Windows에서는 그대로 둔다.
    public static void RoundCorners(Control control)
    {
        int preference = RoundCornerPreference;
        DwmSetWindowAttribute(control.Handle, CornerPreferenceAttribute, ref preference, sizeof(int));
    }

    public static void StyleTitleBar(Form form, Palette palette)
    {
        int captionColor = ColorTranslator.ToWin32(palette.Background);
        DwmSetWindowAttribute(form.Handle, CaptionColorAttribute, ref captionColor, sizeof(int));
    }
}

internal sealed class Palette
{
    public Color Background, Card, Divider, Text, Muted, Link, Primary, OnPrimary, Good, Warn, Bad, Ring, SwitchOn, SwitchOff;

    public static readonly Color Ink = Color.FromArgb(0x18, 0x1C, 0x26);
    public static readonly Color Blush = Color.FromArgb(0xFB, 0x92, 0xA0);
    public static readonly Color Slate = Color.FromArgb(0x94, 0xA3, 0xB8);
    public static readonly Color MascotTile = Color.FromArgb(0x1E, 0x24, 0x30);
    public static readonly Color Signal = Color.FromArgb(0x4A, 0xDE, 0x80);
    public static readonly Color SignalSearching = Color.FromArgb(0xFB, 0xBF, 0x24);

    public static readonly Palette Default = new Palette
    {
        Background = Color.FromArgb(0xF2, 0xF3, 0xF7),
        Card = Color.White,
        Divider = Color.FromArgb(0xE5, 0xE5, 0xEA),
        Text = Color.FromArgb(0x1C, 0x1C, 0x1E),
        Muted = Color.FromArgb(0x63, 0x63, 0x6A),
        Link = Color.FromArgb(0x00, 0x66, 0xCC),
        Primary = Color.FromArgb(0x15, 0x80, 0x3D),
        OnPrimary = Color.White,
        Good = Color.FromArgb(0x15, 0x80, 0x3D),
        Warn = Color.FromArgb(0xB4, 0x53, 0x09),
        Bad = Color.FromArgb(0xC8, 0x1E, 0x1E),
        Ring = Color.FromArgb(0x00, 0x66, 0xCC),
        SwitchOn = Color.FromArgb(0x22, 0xA3, 0x4F),
        SwitchOff = Color.FromArgb(0xC7, 0xC7, 0xCC)
    };
}

internal static class Watcher
{
    private const string ProcessName = "TetherService";
    private const string ServiceArgument = "--service";
    private const int KillWaitMs = 5000;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string ServicePath = Path.Combine(Log.Dir, ProcessName + ".exe");
    private static readonly string ControlPath = Path.Combine(Log.Dir, TrayContext.ControlFileName);

    public static bool IsInstalled { get { return File.Exists(ServicePath); } }
    public static bool IsRunning { get { return IsServiceAlive && !Paused.IsOn; } }

    public static bool IsServiceAlive
    {
        get
        {
            System.Threading.Mutex mutex;
            if (!System.Threading.Mutex.TryOpenExisting(Service.MutexName, out mutex)) return false;
            mutex.Dispose();
            return true;
        }
    }

    private static bool IsInstalledCopy
    {
        get { return string.Equals(Application.ExecutablePath, ControlPath, StringComparison.OrdinalIgnoreCase); }
    }

    // ZIP을 풀지 않고 실행하면 임시 폴더에서 돌아가고, 이 폴더는 ZIP 창을 닫으면 사라진다.
    private static bool IsTemporaryCopy
    {
        get
        {
            string localTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") + Path.DirectorySeparatorChar;
            return new[] { Path.GetTempPath(), localTemp }.Any(folder => Application.ExecutablePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
        }
    }

    // 이 파일이 사라지면 Tether를 제거한다. 다른 사본을 잠깐 연 것으로는 바꾸지 않는다.
    private static void RememberSource()
    {
        if (!IsInstalledCopy && !IsTemporaryCopy) SourceProgram.Remember(Application.ExecutablePath);
    }

    // 실행 중인 Tether.exe를 복사해 쓰므로 별도 설치가 필요 없다.
    public static void Start(bool autoStart)
    {
        if (IsTemporaryCopy) throw new InvalidOperationException("ZIP 압축을 푼 뒤 Tether.exe를 실행해 주세요.");
        RememberSource();
        Paused.Set(false);
        if (!IsServiceAlive)
        {
            CopyProgram();
            Launch();
        }
        SetAutoStart(autoStart);
    }

    // 프로그램은 살려 두고 동작만 멈춘다. 그래야 시계 옆 아이콘에서 다시 실행할 수 있다.
    public static void Stop()
    {
        Paused.Set(true);
        if (IsServiceAlive) return;
        if (!LidAction.Apply(LidAction.Sleep)) throw new InvalidOperationException("뚜껑 동작을 절전으로 되돌리지 못했습니다.");
    }

    // 뒤에서 도는 프로그램이 비정상 종료돼 뚜껑 설정이 남아 있으면 되돌린다.
    public static void RestoreIfServiceDead()
    {
        if (!IsInstalled || IsServiceAlive || LidAction.IsApplied(LidAction.Sleep)) return;
        if (LidAction.Apply(LidAction.Sleep)) Log.Write("프로그램이 꺼져 있어 뚜껑 동작을 절전으로 복원");
    }

    // 새 파일을 먼저 준비해 두어 복사가 실패해도 기존 프로그램을 잃지 않는다.
    public static void UpgradeIfNeeded()
    {
        if (!IsInstalled) return;
        if (SourceProgram.NeedsUpdate) RememberSource();
        if (IsSameBuild(ServicePath)) return;
        RememberSource();
        bool wasAlive = IsServiceAlive;
        string staged = ServicePath + ".new";
        File.Copy(Application.ExecutablePath, staged, true);
        KillService();
        try
        {
            File.Copy(staged, ServicePath, true);
            CopyControl();
        }
        finally
        {
            File.Delete(staged);
            if (wasAlive) Launch();
        }
    }

    private static bool IsSameBuild(string path)
    {
        var current = new FileInfo(Application.ExecutablePath);
        var other = new FileInfo(path);
        return other.Exists && other.Length == current.Length && other.LastWriteTimeUtc == current.LastWriteTimeUtc;
    }

    private static void CopyProgram()
    {
        Directory.CreateDirectory(Log.Dir);
        File.Copy(Application.ExecutablePath, ServicePath, true);
        CopyControl();
    }

    private static void CopyControl()
    {
        if (!IsInstalledCopy) File.Copy(Application.ExecutablePath, ControlPath, true);
    }

    // 작업 폴더를 물려받으면 사용자가 받은 폴더가 잠겨 지울 수 없다.
    private static void Launch()
    {
        var start = new ProcessStartInfo(ServicePath, ServiceArgument) { WorkingDirectory = Log.Dir };
        using (Process.Start(start)) { }
    }

    // 다른 사용자 세션의 프로그램은 건드리지 않는다.
    private static void KillService()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (Process process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                if (process.SessionId != session) continue;
                process.Kill();
                process.WaitForExit(KillWaitMs);
            }
        }
    }

    public static bool IsAutoStartEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            return key != null && key.GetValue(ProcessName) != null;
    }

    public static void SetAutoStart(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (enabled) key.SetValue(ProcessName, "\"" + ServicePath + "\" " + ServiceArgument);
            else key.DeleteValue(ProcessName, false);
        }
        // 보안 프로그램이 오류 없이 막는 경우가 있어 실제 값을 다시 확인한다.
        if (IsAutoStartEnabled() != enabled) throw new InvalidOperationException("자동 시작 설정이 막혔어요. 보안 프로그램에서 Tether를 허용해 주세요.");
    }
}

internal sealed class ControlForm : Form
{
    private const int RefreshMs = 2000;
    private const int ContentWidth = 480;
    private const int SublineWidth = 336;
    private const int SublineHeight = 24;
    private const uint DoNothing = 0;
    private const string WarnMark = "▲ ";

    private readonly Palette palette = Palette.Default;
    private readonly Font smallFont = new Font(Ui.FontName, 9f);
    private readonly Font bodyFont = new Font(Ui.FontName, 10f);
    private readonly Font headlineFont = new Font(Ui.FontName, 15f, FontStyle.Bold);

    private readonly Mascot mascot;
    private readonly Label headline = new Label { AutoSize = true, Margin = Padding.Empty };
    private readonly Label subline = new Label { AutoSize = false, AutoEllipsis = true, Margin = Padding.Empty };
    private readonly Label summary = new Label { AutoSize = true, Margin = Padding.Empty };
    private readonly Label notice = new Label { AutoSize = true, Visible = false };
    private readonly RoundCheck autoStartBox;
    private readonly TableLayoutPanel rowHost = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
    private readonly Timer timer = new Timer { Interval = RefreshMs };
    private readonly List<NetworkRow> rows = new List<NetworkRow>();
    private readonly ActionButton toggleButton;
    private readonly ActionButton addButton;
    private readonly ActionButton settingsButton;
    private readonly ActionButton helpButton;

    public ControlForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = ControlProgram.WindowTitle;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = bodyFont;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        mascot = new Mascot(palette);
        autoStartBox = new RoundCheck(palette, "자동 시작") { Anchor = AnchorStyles.Left };
        toggleButton = new ActionButton(palette.Ring, 132, 50);
        addButton = LinkButton("+ 네트워크 추가", palette.Card);
        settingsButton = LinkButton("설정", palette.Background);
        helpButton = LinkButton("도움말", palette.Background);

        Controls.Add(BuildLayout());
        toggleButton.Click += (sender, e) =>
        {
            Guarded(Watcher.IsRunning ? (Action)Watcher.Stop : StartWatcher);
            LoadAutoStart();
        };
        addButton.Click += (sender, e) => OpenAddNetwork();
        settingsButton.Click += (sender, e) => OpenSettings();
        helpButton.Click += (sender, e) => OpenHelp();
        autoStartBox.Click += (sender, e) => ToggleAutoStart();
        timer.Tick += (sender, e) => RefreshStatus();
        Load += (sender, e) => { Guarded(Watcher.UpgradeIfNeeded); Guarded(Watcher.RestoreIfServiceDead); LoadAutoStart(); timer.Start(); };
        Shown += (sender, e) => Activate();
    }

    private ActionButton LinkButton(string text, Color background)
    {
        return new ActionButton(palette.Ring, 72, 40) { Text = text, BackColor = background, ForeColor = palette.Link };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleTitleBar(this, palette);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 6, Padding = Ui.Pad(20, 18, 20, 12), Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(ContentWidth)));
        for (int row = 0; row < 6; row++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHero(), 0, 0);
        root.Controls.Add(BuildSectionHeader(), 0, 1);
        root.Controls.Add(BuildNetworkCard(), 0, 2);
        root.Controls.Add(BuildToggle(), 0, 3);
        root.Controls.Add(BuildFooter(), 0, 4);
        root.Controls.Add(BuildNotice(), 0, 5);
        return root;
    }

    private Control BuildHero()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, BackColor = palette.Card, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mascot.Margin = new Padding(0, 0, Ui.S(16), 0);
        headline.Font = headlineFont;
        headline.ForeColor = palette.Text;
        subline.ForeColor = palette.Text;
        subline.Size = new Size(Ui.S(SublineWidth), Ui.S(SublineHeight));
        subline.Margin = new Padding(0, Ui.S(4), 0, 0);
        summary.Font = smallFont;
        summary.ForeColor = palette.Muted;
        summary.Margin = new Padding(0, Ui.S(8), 0, 0);
        grid.Controls.Add(mascot, 0, 0);
        grid.SetRowSpan(mascot, 3);
        grid.Controls.Add(headline, 1, 0);
        grid.Controls.Add(subline, 1, 1);
        grid.Controls.Add(summary, 1, 2);

        var card = new RoundedCard(palette.Card, palette.Background)
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Ui.Pad(16, 16, 16, 16),
            Margin = Padding.Empty
        };
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildSectionHeader()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Ui.Pad(6, 18, 0, 8) };
        panel.Controls.Add(new Label { Text = "작업을 유지할 네트워크", AutoSize = true, Font = new Font(Ui.FontName, 10f, FontStyle.Bold), ForeColor = palette.Text, Margin = Padding.Empty });
        panel.Controls.Add(new Label { Text = "최대 3개까지 등록하고, 1순위부터 차례로 연결합니다.", AutoSize = true, Font = smallFont, ForeColor = palette.Muted, Margin = new Padding(0, Ui.S(2), 0, 0) });
        return panel;
    }

    private Control BuildNetworkCard()
    {
        rowHost.BackColor = palette.Card;
        rowHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addButton.AutoSize = false;
        addButton.Dock = DockStyle.Top;
        addButton.Height = Ui.S(46);

        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = palette.Card, Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(rowHost, 0, 0);
        content.Controls.Add(addButton, 0, 1);

        var card = new RoundedCard(palette.Card, palette.Background)
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Ui.Pad(14, 2, 10, 2),
            Margin = Padding.Empty
        };
        card.Controls.Add(content);
        return card;
    }

    private Control BuildToggle()
    {
        toggleButton.AutoSize = false;
        toggleButton.Dock = DockStyle.Top;
        toggleButton.Height = Ui.S(50);
        toggleButton.Font = new Font(Ui.FontName, 11f, FontStyle.Bold);
        toggleButton.Margin = Ui.Pad(0, 14, 0, 0);
        return toggleButton;
    }

    private Control BuildFooter()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = Ui.Pad(6, 8, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        autoStartBox.ForeColor = palette.Text;
        grid.Controls.Add(autoStartBox, 0, 0);
        grid.Controls.Add(settingsButton, 2, 0);
        grid.Controls.Add(helpButton, 3, 0);
        return grid;
    }

    private Control BuildNotice()
    {
        notice.ForeColor = palette.Bad;
        notice.MaximumSize = new Size(Ui.S(ContentWidth), 0);
        notice.Margin = Ui.Pad(6, 6, 0, 0);
        return notice;
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
            ShowNotice(null);
        }
        catch (Exception error) { ShowNotice(error.Message); }
        RefreshStatus();
    }

    private void ShowNotice(string message)
    {
        notice.Visible = message != null;
        notice.Text = WarnMark + message;
    }

    private void StartWatcher()
    {
        if (Networks.Load().Length == 0) throw new InvalidOperationException("유지할 네트워크를 하나 이상 추가해 주세요.");
        Watcher.Start(autoStartBox.Checked);
    }

    private void OpenAddNetwork()
    {
        string[] current = Networks.Load();
        using (var dialog = new AddNetworkForm(palette, Icon, current))
        {
            dialog.ShowDialog(this);
            if (dialog.ChosenName != null) SaveNetworks(current.Concat(new[] { dialog.ChosenName }));
        }
    }

    private void OpenHelp()
    {
        using (var dialog = new HelpForm(palette, Icon)) dialog.ShowDialog(this);
    }

    private void OpenLog()
    {
        using (var dialog = new LogForm(palette, Icon)) dialog.ShowDialog(this);
    }

    private void OpenSettings()
    {
        using (var dialog = new SettingsForm(palette, Icon, OpenLog)) dialog.ShowDialog(this);
        RefreshStatus();
    }

    private void LoadAutoStart()
    {
        autoStartBox.Checked = Watcher.IsAutoStartEnabled();
    }

    private void ToggleAutoStart()
    {
        if (!Watcher.IsInstalled) return;
        Guarded(() => Watcher.SetAutoStart(autoStartBox.Checked));
        LoadAutoStart();
    }

    // 눌린 줄이 지워질 수 있어 클릭 처리가 끝난 뒤에 저장한다.
    private void RemoveNetwork(NetworkRow row)
    {
        string name = row.NetworkName;
        BeginInvoke((Action)(() => SaveNetworks(Networks.Load().Where(item => item != name))));
    }

    private void MoveNetwork(NetworkRow row, int offset)
    {
        string[] order = Networks.Load();
        int index = Array.IndexOf(order, row.NetworkName);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= order.Length) return;
        order[index] = order[target];
        order[target] = row.NetworkName;
        SaveNetworks(order);
    }

    private void SaveNetworks(IEnumerable<string> names)
    {
        try
        {
            Networks.Save(names.ToArray());
            ShowNotice(null);
        }
        catch (IOException error) { ShowNotice("네트워크 목록을 저장하지 못했습니다: " + error.Message); }
        RefreshStatus();
    }

    // 일시적인 읽기 실패로 창이 오류를 띄우지 않게 다음 갱신에 맡긴다.
    private void RefreshStatus()
    {
        try { RefreshStatusNow(); }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RefreshStatusNow()
    {
        string[] selected = Networks.Load();
        VisibleNetwork[] visible = ReadVisible();
        RefreshHero(selected, visible);
        RefreshToggle();
        RefreshRows(selected, visible);
        summary.Text = DescribePower();
    }

    private static VisibleNetwork[] ReadVisible()
    {
        try { return Wlan.Visible(); }
        catch (InvalidOperationException) { return new VisibleNetwork[0]; }
    }

    private void RefreshHero(string[] selected, VisibleNetwork[] visible)
    {
        Tuple<uint, uint> lid = PowerReader.LidAction();
        bool keepsRunning = lid.Item1 == DoNothing && lid.Item2 == DoNothing;
        VisibleNetwork connected = visible.FirstOrDefault(network => network.Connected && selected.Contains(network.Name));
        if (!Watcher.IsInstalled) SetHero("시작할 준비가 됐어요", "네트워크를 추가하고 활성화를 눌러 주세요.", MascotMood.Ready);
        else if (!Watcher.IsRunning) SetHero("지금은 쉬는 중이에요", keepsRunning ? "뚜껑을 닫아도 절전하지 않습니다." : "뚜껑을 닫으면 절전합니다.", MascotMood.Off);
        else if (keepsRunning && connected != null) SetHero("깨어서 일하는 중이에요", "뚜껑을 닫아도 작업이 계속됩니다.", MascotMood.Awake);
        else if (keepsRunning) SetHero("연결을 찾고 있어요", TimingSettings.Load().GraceMinutes + "분 안에 연결되지 않으면 절전합니다.", MascotMood.Searching);
        else SetHero("네트워크를 기다리고 있어요", "연결 전에 뚜껑을 닫으면 절전합니다.", MascotMood.Ready);
    }

    private void SetHero(string title, string detail, MascotMood mood)
    {
        if (headline.Text != title) headline.Text = title;
        if (subline.Text != detail) subline.Text = detail;
        mascot.Mood = mood;
    }

    private void RefreshToggle()
    {
        bool running = Watcher.IsRunning;
        toggleButton.Text = running ? "비활성화" : "활성화";
        toggleButton.BackColor = running ? palette.Card : palette.Primary;
        toggleButton.ForeColor = running ? palette.Bad : palette.OnPrimary;
    }

    // 줄을 다시 만들지 않고 내용만 바꿔 깜빡임과 포커스 이동을 막는다.
    private void RefreshRows(string[] selected, VisibleNetwork[] visible)
    {
        bool canAddMore = selected.Length < Networks.MaxCount;
        while (rows.Count < selected.Length) AddRow();
        while (rows.Count > selected.Length) RemoveLastRow();
        for (int i = 0; i < selected.Length; i++)
        {
            VisibleNetwork network = visible.FirstOrDefault(item => item.Name == selected[i]);
            bool hasRowBelow = i < selected.Length - 1 || canAddMore;
            rows[i].Bind(selected[i], i, selected.Length, network, hasRowBelow);
        }
        addButton.Visible = canAddMore;
    }

    private void AddRow()
    {
        var row = new NetworkRow(palette, RemoveNetwork, MoveNetwork);
        rows.Add(row);
        rowHost.RowCount = rows.Count;
        rowHost.Controls.Add(row, 0, rows.Count - 1);
    }

    private void RemoveLastRow()
    {
        NetworkRow row = rows[rows.Count - 1];
        rows.RemoveAt(rows.Count - 1);
        rowHost.Controls.Remove(row);
        row.Dispose();
    }

    private static string DescribePower()
    {
        PowerStatus status = SystemInformation.PowerStatus;
        bool online = status.PowerLineStatus == PowerLineStatus.Online;
        return (online ? "전원 연결 " : "배터리 ") + (int)Math.Round(status.BatteryLifePercent * 100) + "%";
    }
}

internal static class ControlProgram
{
    internal const string WindowTitle = "Tether";
    private const string MutexName = @"Local\Tether";
    private const int ShowRestore = 9;

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    // 이미 열려 있으면 새 창 대신 기존 창을 맨 앞으로 가져온다.
    private static void BringExistingToFront()
    {
        using (Process current = Process.GetCurrentProcess())
        {
            foreach (Process other in Process.GetProcessesByName(current.ProcessName))
            {
                using (other)
                {
                    if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(other.MainWindowHandle, ShowRestore);
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Contains("--service"))
        {
            Service.Run();
            return;
        }

        bool isFirstInstance;
        using (new System.Threading.Mutex(true, MutexName, out isFirstInstance))
        {
            if (!isFirstInstance)
            {
                BringExistingToFront();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ControlForm());
        }
    }
}
