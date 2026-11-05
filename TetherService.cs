// 허용한 네트워크 연결 상태에 맞춰 Windows 덮개 동작과 절전을 제어한다.
// 뚜껑 신호가 오지 않는 기기가 있어, 뚜껑을 감지하는 대신 덮개 동작을 미리 바꿔 둔다.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class VisibleNetwork
{
    public readonly string Name;
    public readonly bool Connected;
    public readonly int Signal;

    public VisibleNetwork(string name, bool connected, int signal)
    {
        Name = name;
        Connected = connected;
        Signal = signal;
    }
}

internal sealed class Decision
{
    public readonly bool KeepWorking;
    public readonly string ConnectedName;
    public readonly string ConnectTo;
    public readonly bool SleepNow;

    public Decision(bool keepWorking, string connectedName, string connectTo, bool sleepNow)
    {
        KeepWorking = keepWorking;
        ConnectedName = connectedName;
        ConnectTo = connectTo;
        SleepNow = sleepNow;
    }
}

// 사용자가 조절하는 시간 값. settings.txt에 이름=값 형식으로 저장한다.
internal sealed class TimingSettings
{
    public const int MaxGraceMinutes = 60;
    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 120;
    public const int SwitchOff = 0;
    public const int MaxSwitchSeconds = 600;
    private const string GraceKey = "disconnect-grace-minutes";
    private const string IdleKey = "idle-sleep-minutes";
    private const string SwitchKey = "switch-after-seconds";

    public static readonly TimingSettings Default = new TimingSettings(2, 10, SwitchOff);
    private static readonly string FilePath = Path.Combine(Log.Dir, "settings.txt");

    public readonly int GraceMinutes;
    public readonly int IdleMinutes;
    public readonly int SwitchSeconds;

    public TimingSettings(int graceMinutes, int idleMinutes, int switchSeconds)
    {
        GraceMinutes = Clamp(graceMinutes, 0, MaxGraceMinutes);
        IdleMinutes = Clamp(idleMinutes, MinIdleMinutes, MaxIdleMinutes);
        SwitchSeconds = Clamp(switchSeconds, SwitchOff, MaxSwitchSeconds);
    }

    private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }

    public static TimingSettings Parse(IEnumerable<string> lines)
    {
        Dictionary<string, string> values = lines
            .Select(line => line.Split(new[] { '=' }, 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0].Trim())
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim());
        return new TimingSettings(
            ReadInt(values, GraceKey, Default.GraceMinutes),
            ReadInt(values, IdleKey, Default.IdleMinutes),
            ReadInt(values, SwitchKey, Default.SwitchSeconds));
    }

    private static int ReadInt(Dictionary<string, string> values, string key, int fallback)
    {
        string text;
        int value;
        return values.TryGetValue(key, out text) && int.TryParse(text, out value) ? value : fallback;
    }

    public static TimingSettings Load()
    {
        if (!File.Exists(FilePath)) return Default;
        return Parse(File.ReadAllLines(FilePath, Encoding.UTF8));
    }

    public void Save()
    {
        SafeFile.WriteLines(FilePath, new[]
        {
            GraceKey + "=" + GraceMinutes,
            IdleKey + "=" + IdleMinutes,
            SwitchKey + "=" + SwitchSeconds
        });
    }
}

// 연결과 절전 판단 규칙. 시간과 입력 상태를 인자로 받아 Windows 없이 시험할 수 있다.
internal sealed class ConnectionPolicy
{
    public static readonly TimeSpan SwitchCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SleepRetry = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan TargetAttempt = TimeSpan.FromSeconds(25);
    public const int MinSwitchSignal = 40;

    private string currentTarget;
    private DateTime targetSince;
    private bool wasConnected;
    private DateTime? lastConnected;
    private string pendingHigher;
    private DateTime pendingSince;
    private DateTime nextSwitch = DateTime.MinValue;
    private DateTime nextSleep = DateTime.MinValue;

    public Decision Decide(string[] names, VisibleNetwork[] visible, DateTime now, TimeSpan idle, bool onBattery, TimingSettings timing)
    {
        VisibleNetwork[] ranked = names
            .Select(name => visible.FirstOrDefault(network => network.Name == name))
            .Where(network => network != null)
            .ToArray();
        VisibleNetwork connected = ranked.FirstOrDefault(network => network.Connected);
        if (connected != null)
        {
            lastConnected = now;
            wasConnected = true;
            currentTarget = null;
            return new Decision(true, connected.Name, PickHigher(ranked, now, timing), false);
        }

        pendingHigher = null;
        // 끊긴 네트워크가 검색 목록에 잠시 남으므로 그쪽으로 되돌아가지 않게 전환을 쉰다.
        if (wasConnected) nextSwitch = now + SwitchCooldown;
        wasConnected = false;
        bool keepWorking = lastConnected.HasValue && now - lastConnected.Value < TimeSpan.FromMinutes(timing.GraceMinutes);
        bool sleepNow = !keepWorking && names.Length > 0 && onBattery && idle >= TimeSpan.FromMinutes(timing.IdleMinutes) && now >= nextSleep;
        if (sleepNow) nextSleep = now + SleepRetry;
        return new Decision(keepWorking, null, PickTarget(ranked, visible, now), sleepNow);
    }

    // 사용자가 쓰는 연결을 뺏지 않도록 와이파이가 아예 끊겼을 때만 연결한다.
    // 꺼진 네트워크가 검색 목록에 잠시 남으므로 붙지 못하면 다음 순위로 넘어간다.
    // 진행 중인 연결을 끊지 않도록 목표마다 연결 명령은 한 번만 보낸다.
    private string PickTarget(VisibleNetwork[] ranked, VisibleNetwork[] visible, DateTime now)
    {
        if (ranked.Length == 0 || visible.Any(network => network.Connected))
        {
            currentTarget = null;
            return null;
        }
        int index = Array.FindIndex(ranked, network => network.Name == currentTarget);
        if (index >= 0 && now - targetSince < TargetAttempt) return null;
        currentTarget = ranked[(index + 1) % ranked.Length].Name;
        targetSince = now;
        return currentTarget;
    }

    // 높은 순위가 충분히 오래, 충분히 강하게 보일 때만 옮겨 연결이 자주 끊기지 않게 한다.
    private string PickHigher(VisibleNetwork[] ranked, DateTime now, TimingSettings timing)
    {
        if (timing.SwitchSeconds == TimingSettings.SwitchOff) return null;
        VisibleNetwork higher = ranked
            .TakeWhile(network => !network.Connected)
            .FirstOrDefault(network => network.Signal >= MinSwitchSignal);
        if (higher == null)
        {
            pendingHigher = null;
            return null;
        }
        if (now < nextSwitch) return null;
        if (higher.Name != pendingHigher)
        {
            pendingHigher = higher.Name;
            pendingSince = now;
            return null;
        }
        if (now - pendingSince < TimeSpan.FromSeconds(timing.SwitchSeconds)) return null;
        pendingHigher = null;
        nextSwitch = now + SwitchCooldown;
        return higher.Name;
    }
}

internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    internal static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tether");
    internal static readonly string FilePath = Path.Combine(Dir, "watcher.log");
    private static readonly object Gate = new object();
    private static string lastMessage;

    // 오래된 절반만 버려 원인 분석에 필요한 최근 기록을 남긴다.
    private static void Trim()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length <= MaxBytes) return;
        string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
        File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2), Encoding.UTF8);
    }

    // 기록 실패 때문에 프로그램이 멈추면 안 되므로 실패는 무시한다.
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (message == lastMessage) return;
                lastMessage = message;
                Trim();
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

// 쓰는 도중에 다른 프로그램이 빈 파일을 읽지 않도록 임시 파일을 거쳐 교체한다.
internal static class SafeFile
{
    public static void WriteLines(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllLines(temporary, lines, Encoding.UTF8);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}

// 허용 네트워크 목록: networks.txt에 한 줄에 하나씩, 위가 높은 순위로 저장한다.
internal static class Networks
{
    public const int MaxCount = 3;
    private static readonly string FilePath = Path.Combine(Log.Dir, "networks.txt");

    public static string[] Load()
    {
        if (!File.Exists(FilePath)) return new string[0];
        return File.ReadAllLines(FilePath, Encoding.UTF8)
            .Select(line => line.Trim())
            .Where(IsSupported)
            .Distinct()
            .Take(MaxCount)
            .ToArray();
    }

    public static void Save(IEnumerable<string> names)
    {
        SafeFile.WriteLines(FilePath, names);
    }

    // netsh 인자를 깨뜨릴 수 있는 이름은 쓰지 않는다.
    public static bool IsSupported(string name)
    {
        return name.Length > 0 && !name.Contains("\"") && !name.EndsWith("\\") && !name.Any(char.IsControl);
    }
}

// 중지 상태 표시. 파일이 있으면 프로그램은 살아 있어도 동작을 멈춘다.
internal static class Paused
{
    private static readonly string FilePath = Path.Combine(Log.Dir, "paused.flag");

    public static bool IsOn { get { return File.Exists(FilePath); } }

    public static void Set(bool paused)
    {
        Directory.CreateDirectory(Log.Dir);
        if (paused) File.WriteAllText(FilePath, "");
        else File.Delete(FilePath);
    }
}

// 사용자가 받은 Tether.exe의 위치. 이 파일이 사라지면 사용자가 제거한 것으로 본다.
internal static class SourceProgram
{
    private static readonly string FilePath = Path.Combine(Log.Dir, "source.txt");

    public static void Remember(string path)
    {
        SafeFile.WriteLines(FilePath, new[] { path });
    }

    // 기록이 없거나 그 파일이 사라졌으면 지금 연 위치로 바꿔도 된다.
    public static bool NeedsUpdate { get { return !File.Exists(FilePath) || IsGone(); } }

    // USB는 뽑을 수 있고, 그 드라이브 문자를 다른 USB가 차지할 수도 있어 고정 드라이브만 판단한다.
    public static bool IsGone(string path, Func<string, bool> fileExists, Func<string, bool> isFixedDrive)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
        return isFixedDrive(Path.GetPathRoot(path)) && !fileExists(path);
    }

    internal static bool IsFixedDrive(string root)
    {
        return !root.StartsWith(@"\\") && new DriveInfo(root).DriveType == DriveType.Fixed;
    }

    // 읽지 못하면 제거하지 않는다. 잘못 지우는 쪽이 더 위험하다.
    public static bool IsGone()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            string path = File.ReadAllLines(FilePath, Encoding.UTF8).FirstOrDefault();
            return IsGone(path, File.Exists, IsFixedDrive);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}

// 압축을 덮어쓰는 동안처럼 잠깐 없는 경우를 걸러 낸다.
internal sealed class SourceWatch
{
    public const int ChecksToConfirm = 6;
    private int missingChecks;

    public bool Observe(bool isGone)
    {
        missingChecks = isGone ? missingChecks + 1 : 0;
        return missingChecks >= ChecksToConfirm;
    }
}

// 종료 중에 뚜껑 설정을 다시 덮어쓰지 않게 막는다. 종료가 취소돼도 감시가 돌아오도록 시간을 정해 둔다.
internal sealed class SessionEndHold
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);
    private DateTime until = DateTime.MinValue;

    public void Start(DateTime now) { until = now + Duration; }
    public bool IsActive(DateTime now) { return now < until; }
}

internal static class Wlan
{
    private const uint ClientVersion = 2;
    private const int ListHeaderSize = 8;
    private const int NetworkEntrySize = 628;
    private const int ProfileEntrySize = 516;
    private const int SignalOffset = 604;
    private const int FlagsOffset = 620;
    private const uint FlagConnected = 1;

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);

    private static T WithInterface<T>(Func<IntPtr, Guid, T> action)
    {
        uint negotiated;
        IntPtr handle;
        uint openResult = WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiated, out handle);
        if (openResult != 0) throw new InvalidOperationException("와이파이 서비스를 열지 못했습니다: " + openResult);
        try
        {
            IntPtr list;
            uint enumResult = WlanEnumInterfaces(handle, IntPtr.Zero, out list);
            if (enumResult != 0) throw new InvalidOperationException("와이파이 장치를 읽지 못했습니다: " + enumResult);
            try
            {
                if (Marshal.ReadInt32(list) == 0) throw new InvalidOperationException("와이파이 장치 없음");
                var guid = (Guid)Marshal.PtrToStructure(IntPtr.Add(list, ListHeaderSize), typeof(Guid));
                return action(handle, guid);
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    public static void Scan()
    {
        WithInterface((handle, guid) =>
        {
            uint result = WlanScan(handle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != 0) Log.Write("검색 요청 실패: " + result);
            return true;
        });
    }

    public static string[] SavedProfiles()
    {
        return WithInterface((handle, guid) =>
        {
            IntPtr list;
            uint result = WlanGetProfileList(handle, ref guid, IntPtr.Zero, out list);
            if (result != 0) throw new InvalidOperationException("저장된 와이파이를 읽지 못했습니다: " + result);
            try
            {
                return Enumerable.Range(0, Marshal.ReadInt32(list))
                    .Select(i => Marshal.PtrToStringUni(IntPtr.Add(list, ListHeaderSize + i * ProfileEntrySize)))
                    .ToArray();
            }
            finally { WlanFreeMemory(list); }
        });
    }

    // 저장된 프로필이 있는 주변 네트워크만 돌려준다.
    public static VisibleNetwork[] Visible()
    {
        return WithInterface((handle, guid) =>
        {
            IntPtr list;
            uint result = WlanGetAvailableNetworkList(handle, ref guid, 0, IntPtr.Zero, out list);
            if (result != 0) throw new InvalidOperationException("주변 와이파이를 읽지 못했습니다: " + result);
            try { return ReadNetworks(list); }
            finally { WlanFreeMemory(list); }
        });
    }

    private static VisibleNetwork[] ReadNetworks(IntPtr list)
    {
        return Enumerable.Range(0, Marshal.ReadInt32(list))
            .Select(i => ReadEntry(IntPtr.Add(list, ListHeaderSize + i * NetworkEntrySize)))
            .Where(network => network.Name.Length > 0)
            .GroupBy(network => network.Name)
            .Select(group => new VisibleNetwork(group.Key, group.Any(n => n.Connected), group.Max(n => n.Signal)))
            .ToArray();
    }

    private static VisibleNetwork ReadEntry(IntPtr entry)
    {
        bool connected = ((uint)Marshal.ReadInt32(entry, FlagsOffset) & FlagConnected) != 0;
        return new VisibleNetwork(Marshal.PtrToStringUni(entry), connected, Marshal.ReadInt32(entry, SignalOffset));
    }
}

internal static class Shell
{
    private const int TimeoutMs = 15000;
    private const int KillWaitMs = 1000;
    private const int Failed = -1;
    internal const int TimedOut = -2;

    // 같은 폴더의 가짜 파일이 실행되지 않도록 시스템 폴더 경로로 실행하고, 멈추면 끊는다.
    public static int Run(string file, string arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file), arguments) { CreateNoWindow = true, UseShellExecute = false };
        try
        {
            using (var process = Process.Start(info))
            {
                if (process.WaitForExit(TimeoutMs)) return process.ExitCode;
                Stop(process);
                return TimedOut;
            }
        }
        catch (Win32Exception error)
        {
            Log.Write(file + " 실행 실패: " + error.Message);
            return Failed;
        }
    }

    // 시간 초과와 프로세스 종료가 겹치면 Kill이 실패할 수 있다.
    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
            process.WaitForExit(KillWaitMs);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    // 실행 실패는 Run이 이미 기록하므로 여기서는 실행된 뒤의 실패만 남긴다.
    public static bool RunLogged(string file, string arguments)
    {
        int result = Run(file, arguments);
        if (result != 0 && result != Failed) Log.Write(file + " " + arguments + " 실패: " + DescribeFailure(result));
        return result == 0;
    }

    internal static string DescribeFailure(int result)
    {
        return result == TimedOut ? "시간 초과" : "종료 코드 0x" + result.ToString("X8");
    }
}

internal sealed class NetworkMonitor
{
    private const int PollMs = 5000;
    private const int PollsPerSearch = 2;
    private const int PollsPerHigherSearch = 12;
    private const int NotApplied = -1;

    private readonly ConnectionPolicy policy = new ConnectionPolicy();
    private readonly SourceWatch sourceWatch = new SourceWatch();
    private readonly SessionEndHold sessionEndHold = new SessionEndHold();
    private readonly object lidGate = new object();
    private volatile bool removalDue;
    private int appliedAction = NotApplied;
    private string[] names = new string[0];
    private TimingSettings timing = TimingSettings.Default;
    private string lastError;
    private string lastState;
    private readonly AutoResetEvent wake = new AutoResetEvent(false);

    public string Status { get { return lastState ?? "시작하는 중"; } }
    public bool RemovalDue { get { return removalDue; } }

    public void Wake() { wake.Set(); }

    public bool RestoreForSessionEnd()
    {
        lock (lidGate)
        {
            sessionEndHold.Start(DateTime.UtcNow);
            return LidAction.Apply(LidAction.Sleep);
        }
    }

    public void Run()
    {
        for (int poll = 0; ; poll++)
        {
            SafeTick(poll);
            wake.WaitOne(PollMs);
        }
    }

    // 예상 못 한 오류로 멈추면 뚜껑 설정이 남으므로 절전으로 되돌리고 계속 돈다.
    private void SafeTick(int poll)
    {
        try { Tick(poll); }
        catch (Exception error)
        {
            Log.Write("오류: " + error.Message);
            appliedAction = NotApplied;
            LidAction.Apply(LidAction.Sleep);
        }
    }

    private void Tick(int poll)
    {
        CheckSource(poll);
        if (removalDue || Paused.IsOn)
        {
            ApplyStopped();
            return;
        }
        ReloadNames();
        Decision decision = policy.Decide(names, ReadVisible(), DateTime.UtcNow, Power.IdleTime, Power.OnBattery, timing);
        ApplyLidAction(decision);
        LogState(decision);
        Reconnect(decision);
        Search(decision, poll);
        if (!decision.SleepNow) return;
        Log.Write("허용 네트워크 없이 입력이 없어 절전 실행");
        Power.Sleep();
    }

    // 비활성화 중에는 폴더를 옮길 수 있게 확인하지 않는다.
    // 시작 직후에만 확인해서, 비활성화한 채 폴더를 지운 경우도 다음 로그인 때 정리한다.
    private void CheckSource(int poll)
    {
        if (removalDue) return;
        bool isChecking = !Paused.IsOn || poll < SourceWatch.ChecksToConfirm;
        removalDue = sourceWatch.Observe(isChecking && SourceProgram.IsGone());
    }

    private void ApplyStopped()
    {
        bool isApplied = appliedAction == LidAction.Sleep && LidAction.IsApplied(LidAction.Sleep);
        if (!isApplied && LidAction.Apply(LidAction.Sleep))
        {
            Power.BlockIdleSleep(false);
            appliedAction = LidAction.Sleep;
        }
        ReportState("비활성화됨, 뚜껑을 닫으면 절전");
    }

    // 제어 창이 저장 중이면 읽기에 실패할 수 있으니 이전 목록을 유지한다.
    private void ReloadNames()
    {
        try
        {
            names = Networks.Load();
            timing = TimingSettings.Load();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // 상태를 읽지 못하면 보이는 네트워크가 없는 것으로 판단한다.
    private VisibleNetwork[] ReadVisible()
    {
        try
        {
            VisibleNetwork[] visible = Wlan.Visible();
            lastError = null;
            return visible;
        }
        catch (InvalidOperationException error)
        {
            if (error.Message != lastError) Log.Write("상태 확인 실패: " + error.Message);
            lastError = error.Message;
            return new VisibleNetwork[0];
        }
    }

    private void ApplyLidAction(Decision decision)
    {
        int action = decision.KeepWorking ? LidAction.DoNothing : LidAction.Sleep;
        lock (lidGate)
        {
            if (sessionEndHold.IsActive(DateTime.UtcNow)) return;
            if (action == appliedAction && LidAction.IsApplied(action)) return;
            if (!LidAction.Apply(action))
            {
                Log.Write("뚜껑 동작 변경 실패");
                return;
            }
            Power.BlockIdleSleep(decision.KeepWorking);
            appliedAction = action;
        }
    }

    private void LogState(Decision decision)
    {
        string state = decision.ConnectedName != null ? decision.ConnectedName + " 연결됨, 뚜껑을 닫아도 작업 계속"
            : decision.KeepWorking ? "연결 끊김, 다음 네트워크를 찾는 동안 작업 유지"
            : "허용 네트워크 없음, 뚜껑을 닫으면 절전";
        ReportState(state);
    }

    private void ReportState(string state)
    {
        if (state == lastState) return;
        lastState = state;
        Log.Write(state);
    }

    private void Reconnect(Decision decision)
    {
        if (decision.ConnectTo == null) return;
        if (decision.ConnectedName != null) Log.Write("더 높은 순위인 " + decision.ConnectTo + "(으)로 전환");
        Shell.RunLogged("netsh.exe", "wlan connect name=\"" + decision.ConnectTo + "\"");
    }

    private void Search(Decision decision, int poll)
    {
        bool isTopRanked = names.Length > 0 && decision.ConnectedName == names[0];
        int interval = decision.ConnectedName == null ? PollsPerSearch : PollsPerHigherSearch;
        if (isTopRanked || poll % interval != 0) return;
        try { Wlan.Scan(); }
        catch (InvalidOperationException error) { Log.Write("검색 실패: " + error.Message); }
    }
}
