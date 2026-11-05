// 전원 구성표 읽기·쓰기, 뚜껑 동작, 절전 요청.
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class PowerReader
{
    private static Guid buttonsGroup = new Guid("4F971E89-EEBD-4455-A8DE-9E59040E7347");
    private static Guid lidActionSetting = new Guid("5CA83367-6E45-459F-A27B-476B1D01C936");
    private static Guid videoGroup = new Guid("7516B95F-F776-4464-8C53-06167F40CC99");
    private static Guid videoIdleSetting = new Guid("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");
    private static Guid processorGroup = new Guid("54533251-82BE-4824-96C1-47B60B740D00");
    private static Guid maxPerformanceSetting = new Guid("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
    private static Guid turboSetting = new Guid("BE337238-0D82-4146-A960-4F3749D470C7");
    private const uint TurboOff = 0;
    private const uint TurboAggressive = 2;
    private const int FullPerformance = 100;

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static Tuple<uint, uint> Read(ref Guid group, ref Guid setting)
    {
        IntPtr schemePointer;
        uint result = PowerGetActiveScheme(IntPtr.Zero, out schemePointer);
        if (result != 0) throw new InvalidOperationException("전원 구성표 읽기 실패: " + result);
        try
        {
            var scheme = (Guid)Marshal.PtrToStructure(schemePointer, typeof(Guid));
            uint ac, dc;
            uint acResult = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out ac);
            uint dcResult = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out dc);
            if (acResult != 0 || dcResult != 0) throw new InvalidOperationException("전원 설정 읽기 실패");
            return Tuple.Create(ac, dc);
        }
        finally { LocalFree(schemePointer); }
    }

    public static Tuple<uint, uint> LidAction() { return Read(ref buttonsGroup, ref lidActionSetting); }

    // 시스템 종료 중에도 동작하도록 외부 프로그램 없이 쓰고, 한 값이 실패해도 나머지는 바꿔 둔다.
    public static bool SetLidAction(uint action)
    {
        IntPtr schemePointer;
        uint schemeResult = PowerGetActiveScheme(IntPtr.Zero, out schemePointer);
        if (schemeResult != 0)
        {
            Log.Write("전원 구성표 읽기 실패: " + schemeResult);
            return false;
        }
        try
        {
            var scheme = (Guid)Marshal.PtrToStructure(schemePointer, typeof(Guid));
            uint ac = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref buttonsGroup, ref lidActionSetting, action);
            uint dc = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref buttonsGroup, ref lidActionSetting, action);
            uint active = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (ac == 0 && dc == 0 && active == 0) return true;
            Log.Write("뚜껑 동작 쓰기 실패: AC " + ac + ", DC " + dc + ", 적용 " + active);
            return false;
        }
        finally { LocalFree(schemePointer); }
    }
    public static Tuple<uint, uint> ScreenOffSeconds() { return Read(ref videoGroup, ref videoIdleSetting); }

    public static bool IsCoolModeOn()
    {
        return Read(ref processorGroup, ref turboSetting).Item2 == TurboOff && CoolPercent() < FullPerformance;
    }

    public static int CoolPercent() { return (int)Read(ref processorGroup, ref maxPerformanceSetting).Item2; }

    // 배터리 값만 바꾼다. 전원 연결 때의 성능에는 영향이 없다.
    public static bool SetCoolMode(bool enabled, int percent)
    {
        uint turbo = enabled ? TurboOff : TurboAggressive;
        int maxPerformance = enabled ? percent : FullPerformance;
        return Shell.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE " + turbo) == 0
            && Shell.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX " + maxPerformance) == 0
            && Shell.Run("powercfg.exe", "/setactive SCHEME_CURRENT") == 0;
    }

    public static bool SetScreenOffSeconds(int pluggedIn, int onBattery)
    {
        return Shell.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_VIDEO VIDEOIDLE " + pluggedIn) == 0
            && Shell.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT SUB_VIDEO VIDEOIDLE " + onBattery) == 0
            && Shell.Run("powercfg.exe", "/setactive SCHEME_CURRENT") == 0;
    }
}

internal static class LidAction
{
    public const int DoNothing = 0;
    public const int Sleep = 1;

    // 전원 구성표가 바뀌거나 사용자가 직접 고친 경우를 찾기 위해 실제 값을 읽는다.
    public static bool IsApplied(int action)
    {
        try
        {
            Tuple<uint, uint> current = PowerReader.LidAction();
            return current.Item1 == action && current.Item2 == action;
        }
        catch (InvalidOperationException) { return false; }
    }

    public static bool Apply(int action)
    {
        return PowerReader.SetLidAction((uint)action);
    }
}

internal static class Power
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;
    private const uint WmSysCommand = 0x0112;
    private static readonly IntPtr Broadcast = new IntPtr(0xFFFF);
    private static readonly IntPtr ScMonitorPower = new IntPtr(0xF170);
    private static readonly IntPtr MonitorOff = new IntPtr(2);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("powrprof.dll", SetLastError = true)] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvents);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static bool OnBattery { get { return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; } }

    public static TimeSpan IdleTime
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf(typeof(LastInputInfo)) };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }
    }

    // 호출한 스레드에 적용되므로 NetworkMonitor 스레드에서 불러야 한다.
    public static void BlockIdleSleep(bool blocked)
    {
        SetThreadExecutionState(blocked ? Continuous | SystemRequired : Continuous);
    }

    public static void Sleep()
    {
        if (SetSuspendState(false, false, false)) return;
        Log.Write("절전 요청 실패: " + Marshal.GetLastWin32Error() + ", 화면을 꺼서 대기 모드 진입 시도");
        PostMessage(Broadcast, WmSysCommand, ScMonitorPower, MonitorOff);
    }
}
