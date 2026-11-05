// 시계 옆 알림 영역 아이콘과 백그라운드 실행 진입점.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal enum TrayGlyph { Open, Run, Stop }

// 흰 배경, 칸을 나누는 구분선, 직접 그린 아이콘. 마우스를 올리면 둥근 회색으로 강조한다.
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private const int HighlightRadius = 6;
    private const int GlyphSize = 16;
    private const int GlyphGap = 10;
    private const int LabelWidth = 46;
    private readonly Palette palette;

    public TrayMenuRenderer(Palette palette) { this.palette = palette; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Color.White);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area = new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1);
        using (var path = Shapes.Rounded(area, Ui.S(HighlightRadius)))
        using (var brush = new SolidBrush(palette.Background)) e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // 단축키 표시용으로 빈 글자 호출이 한 번 더 온다.
        if (string.IsNullOrEmpty(e.Text)) return;
        int size = Ui.S(GlyphSize);
        int gap = Ui.S(GlyphGap);
        // 줄마다 재지 않고 같은 폭으로 계산해야 위아래 줄의 시작 위치가 일치한다.
        int left = (e.Item.Width - size - gap - Ui.S(LabelWidth)) / 2;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        DrawGlyph(e.Graphics, (TrayGlyph)e.Item.Tag, new Rectangle(left, (e.Item.Height - size) / 2, size, size));
        int textLeft = left + size + gap;
        var area = new Rectangle(textLeft, 0, e.Item.Width - textLeft, e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, area, palette.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawGlyph(Graphics graphics, TrayGlyph glyph, Rectangle area)
    {
        if (glyph == TrayGlyph.Open) DrawWindowGlyph(graphics, area);
        else if (glyph == TrayGlyph.Run) DrawRunGlyph(graphics, area);
        else DrawStopGlyph(graphics, area);
    }

    private void DrawWindowGlyph(Graphics graphics, Rectangle area)
    {
        // 선 두께의 절반만큼 안쪽에 그려야 채운 아이콘과 바깥 크기가 같아진다.
        area.Inflate(-Ui.S(1), -Ui.S(1));
        using (var pen = new Pen(palette.Link, Ui.S(2)))
        using (var path = Shapes.Rounded(area, Ui.S(3)))
        {
            graphics.DrawPath(pen, path);
            int bar = area.Top + area.Height / 3;
            graphics.DrawLine(pen, area.Left, bar, area.Right, bar);
        }
    }

    private void DrawRunGlyph(Graphics graphics, Rectangle area)
    {
        using (var brush = new SolidBrush(palette.Primary))
        {
            graphics.FillPolygon(brush, new[]
            {
                new Point(area.Left, area.Top),
                new Point(area.Right, area.Top + area.Height / 2),
                new Point(area.Left, area.Bottom)
            });
        }
    }

    private void DrawStopGlyph(Graphics graphics, Rectangle area)
    {
        using (var brush = new SolidBrush(palette.Bad))
        using (var path = Shapes.Rounded(area, Ui.S(3))) graphics.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int middle = e.Item.Height / 2;
        using (var pen = new Pen(palette.Divider)) e.Graphics.DrawLine(pen, 0, middle, e.Item.Width, middle);
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private const int RefreshMs = 2000;
    private const int MaxTipLength = 63;
    private const int ItemWidth = 104;
    private const int ItemHeight = 38;
    private const int MenuExtraHeight = 5;
    internal const string ControlFileName = "Tether.exe";

    private readonly NetworkMonitor monitor;
    private readonly NotifyIcon notifyIcon = new NotifyIcon();
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem toggleItem = new ToolStripMenuItem();
    private readonly ToolStripMenuItem openItem = new ToolStripMenuItem("열기") { Tag = TrayGlyph.Open };
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = RefreshMs };
    private readonly Icon runningIcon;
    private readonly Icon stoppedIcon;

    public TrayContext(NetworkMonitor monitor)
    {
        this.monitor = monitor;
        runningIcon = WithBadge(Color.FromArgb(0x4A, 0xDE, 0x80));
        stoppedIcon = WithBadge(Palette.Slate);
        menu = CreateMenu(openItem, toggleItem);
        toggleItem.Click += (sender, e) => Toggle();
        openItem.Click += (sender, e) => OpenControl();
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) menu.Show(Cursor.Position); };
        notifyIcon.DoubleClick += (sender, e) => OpenControl();
        timer.Tick += (sender, e) => RefreshTray();
        RefreshTray();
        notifyIcon.Visible = true;
        timer.Start();
    }

    internal static ContextMenuStrip CreateMenu(ToolStripMenuItem open, ToolStripMenuItem toggle)
    {
        var strip = new ContextMenuStrip
        {
            Font = new Font(Ui.FontName, 9.5f),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = Ui.Pad(4, 4, 4, 4),
            Renderer = new TrayMenuRenderer(Palette.Default),
            BackColor = Color.White
        };
        strip.Items.AddRange(new ToolStripItem[] { open, new ToolStripSeparator(), toggle });
        foreach (ToolStripMenuItem item in new[] { open, toggle })
        {
            item.AutoSize = false;
            item.Size = new Size(Ui.S(ItemWidth), Ui.S(ItemHeight));
        }
        strip.AutoSize = false;
        strip.Size = new Size(Ui.S(ItemWidth), Ui.S(ItemHeight) * 2 + Ui.S(MenuExtraHeight));
        strip.HandleCreated += (sender, e) => Ui.RoundCorners(strip);
        return strip;
    }

    // 아이콘 오른쪽 아래에 상태 점을 찍어 실행과 중지를 구분한다.
    private static Icon WithBadge(Color color)
    {
        using (Icon source = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
        using (Bitmap bitmap = source.ToBitmap())
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(color))
        using (var outline = new SolidBrush(Palette.Ink))
        {
            int size = bitmap.Width / 2;
            int left = bitmap.Width - size;
            int top = bitmap.Height - size;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillEllipse(outline, left - 2, top - 2, size + 2, size + 2);
            graphics.FillEllipse(brush, left, top, size - 2, size - 2);
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }

    private void RefreshTray()
    {
        if (monitor.RemovalDue)
        {
            FinishRemoval();
            return;
        }
        bool stopped = Paused.IsOn;
        string tip = "Tether: " + monitor.Status;
        toggleItem.Text = stopped ? "활성화" : "비활성화";
        toggleItem.Tag = stopped ? TrayGlyph.Run : TrayGlyph.Stop;
        notifyIcon.Icon = stopped ? stoppedIcon : runningIcon;
        notifyIcon.Text = tip.Length > MaxTipLength ? tip.Substring(0, MaxTipLength) : tip;
    }

    // 뚜껑 설정을 되돌리지 못하면 다음 갱신 때 다시 시도한다.
    private void FinishRemoval()
    {
        if (!SelfRemoval.Run()) return;
        timer.Stop();
        notifyIcon.Visible = false;
        ExitThread();
    }

    private void Toggle()
    {
        try
        {
            Paused.Set(!Paused.IsOn);
            monitor.Wake();
        }
        catch (IOException error) { Log.Write("상태 변경 실패: " + error.Message); }
        catch (UnauthorizedAccessException error) { Log.Write("상태 변경 실패: " + error.Message); }
        RefreshTray();
    }

    private static void OpenControl()
    {
        string path = Path.Combine(Log.Dir, ControlFileName);
        if (!File.Exists(path))
        {
            Log.Write("제어 창 파일을 찾지 못했습니다");
            return;
        }
        using (Process.Start(path)) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Tether.exe를 받은 폴더가 지워지면 뚜껑 설정과 자동 시작을 되돌리고 프로그램을 지운다.
// 백신 격리도 삭제와 똑같이 보이므로, 등록 네트워크와 설정은 남겨 다시 받으면 이어서 쓰게 한다.
internal static class SelfRemoval
{
    private const string ServiceFileName = "TetherService.exe";
    private const int KillWaitMs = 5000;
    private const int ExitWaitPings = 6;
    private static readonly string[] DataFiles =
    {
        "paused.flag", "source.txt", "watcher.log",
        "networks.txt.tmp", "settings.txt.tmp", "source.txt.tmp", ServiceFileName + ".new"
    };

    // 복원을 가장 먼저 하고, 다른 단계가 실패해도 나머지를 계속 진행한다.
    public static bool Run()
    {
        if (!LidAction.Apply(LidAction.Sleep))
        {
            Log.Write("제거 보류: 뚜껑 동작을 절전으로 되돌리지 못했습니다");
            return false;
        }
        Attempt(() => Watcher.SetAutoStart(false));
        Attempt(CloseControl);
        Attempt(ScheduleFileDelete);
        return true;
    }

    private static void Attempt(Action step)
    {
        try { step(); }
        catch (Exception error) { Log.Write("제거 중 오류: " + error.Message); }
    }

    private static void CloseControl()
    {
        string control = Path.Combine(Log.Dir, TrayContext.ControlFileName);
        int session = Process.GetCurrentProcess().SessionId;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(control)))
        {
            using (process)
            {
                if (process.SessionId != session) continue;
                if (!string.Equals(process.MainModule.FileName, control, StringComparison.OrdinalIgnoreCase)) continue;
                process.Kill();
                process.WaitForExit(KillWaitMs);
            }
        }
    }

    // 실행 중인 파일은 스스로 지울 수 없고 종료 직전까지 기록이 쓰일 수 있어, 모두 종료 뒤에 지운다.
    // 정해진 파일만 지우므로 사용자가 둔 다른 파일이 있으면 폴더는 남는다.
    private static void ScheduleFileDelete()
    {
        string folder = Path.GetFileName(Log.Dir);
        string ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        var names = DataFiles.Concat(new[] { ServiceFileName, TrayContext.ControlFileName });
        string files = string.Join(" ", names.Select(name => "\"" + folder + "\\" + name + "\""));
        string command = string.Format("/c \"\"{0}\" -n {1} 127.0.0.1 >nul & del /f /q {2} & rmdir \"{3}\"\"", ping, ExitWaitPings, files, folder);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command)
        {
            WorkingDirectory = Path.GetDirectoryName(Log.Dir),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (Process.Start(start)) { }
    }
}

internal static class Service
{
    internal const string MutexName = @"Local\TetherService";

    // Tether가 없는 동안에는 뚜껑을 닫으면 절전하도록 되돌려 둔다.
    private static void RestoreLid(string reason)
    {
        Log.Write(reason + ", 뚜껑 동작을 절전으로 복원");
        LidAction.Apply(LidAction.Sleep);
    }

    private static void RestoreForSessionEnd(NetworkMonitor monitor)
    {
        bool isRestored = monitor.RestoreForSessionEnd();
        Log.Write("로그아웃 또는 종료, " + (isRestored ? "뚜껑 동작을 절전으로 복원" : "뚜껑 동작 복원 실패"));
    }

    public static void Run()
    {
        bool isFirstInstance;
        using (new Mutex(true, MutexName, out isFirstInstance))
        {
            if (!isFirstInstance) return;
            Log.Write("Tether 실행");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => RestoreLid("오류로 종료");
            var monitor = new NetworkMonitor();
            SystemEvents.SessionEnding += (sender, e) => RestoreForSessionEnd(monitor);
            new Thread(monitor.Run) { IsBackground = true }.Start();
            Application.Run(new TrayContext(monitor));
        }
    }
}
