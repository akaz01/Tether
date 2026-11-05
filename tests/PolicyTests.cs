// ConnectionPolicy 규칙 시험. 실행: .\tests\Run-PolicyTests.ps1
using System;

internal static class PolicyTests
{
    private static readonly DateTime Start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Names = { "office", "hotspot" };
    private static readonly TimeSpan Active = TimeSpan.Zero;
    private static readonly TimingSettings SwitchOn = new TimingSettings(2, 10, 30);
    private static int failures;

    private static void Check(string name, bool passed)
    {
        Console.WriteLine((passed ? "통과  " : "실패  ") + name);
        if (!passed) failures++;
    }

    private static VisibleNetwork On(string name) { return new VisibleNetwork(name, true, 80); }
    private static VisibleNetwork Seen(string name, int signal = 80) { return new VisibleNetwork(name, false, signal); }

    private static Decision Decide(ConnectionPolicy policy, DateTime now, params VisibleNetwork[] visible)
    {
        return policy.Decide(Names, visible, now, Active, true, SwitchOn);
    }

    private static void ConnectsToHighestRankWhenDisconnected()
    {
        Decision decision = Decide(new ConnectionPolicy(), Start, Seen("hotspot"), Seen("office"));
        Check("연결이 없으면 가장 높은 순위에 연결", decision.ConnectTo == "office");
    }

    private static void IgnoresNetworksNotSelected()
    {
        Decision decision = Decide(new ConnectionPolicy(), Start, On("cafe"));
        Check("체크하지 않은 네트워크 연결은 작업 유지로 치지 않음", !decision.KeepWorking && decision.ConnectedName == null);
    }

    private static void KeepsWorkingWhileConnected()
    {
        Decision decision = Decide(new ConnectionPolicy(), Start, On("hotspot"));
        Check("체크한 네트워크에 연결되면 작업 유지", decision.KeepWorking && decision.ConnectedName == "hotspot");
    }

    private static void KeepsWorkingDuringGrace()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("office"));
        Check("끊긴 뒤 2분 안에는 작업 유지", Decide(policy, Start.AddSeconds(119)).KeepWorking);
        Check("끊긴 뒤 2분이 지나면 작업 유지 해제", !Decide(policy, Start.AddSeconds(120)).KeepWorking);
    }

    private static void SwitchesToHigherAfterStableTime()
    {
        var policy = new ConnectionPolicy();
        Check("높은 순위가 처음 보이면 바로 옮기지 않음", Decide(policy, Start, On("hotspot"), Seen("office")).ConnectTo == null);
        Check("30초 전에는 옮기지 않음", Decide(policy, Start.AddSeconds(29), On("hotspot"), Seen("office")).ConnectTo == null);
        Check("30초 계속 보이면 높은 순위로 전환", Decide(policy, Start.AddSeconds(30), On("hotspot"), Seen("office")).ConnectTo == "office");
    }

    private static void ResetsStableTimeWhenHigherDisappears()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("hotspot"), Seen("office"));
        Decide(policy, Start.AddSeconds(20), On("hotspot"));
        Decide(policy, Start.AddSeconds(25), On("hotspot"), Seen("office"));
        Check("높은 순위가 사라지면 30초를 다시 셈", Decide(policy, Start.AddSeconds(35), On("hotspot"), Seen("office")).ConnectTo == null);
    }

    private static void IgnoresWeakHigherNetwork()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("hotspot"), Seen("office", 39));
        Check("신호가 약한 높은 순위로는 옮기지 않음", Decide(policy, Start.AddSeconds(31), On("hotspot"), Seen("office", 39)).ConnectTo == null);
    }

    private static void WaitsAfterFailedSwitch()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("hotspot"), Seen("office"));
        Decide(policy, Start.AddSeconds(30), On("hotspot"), Seen("office"));
        Decide(policy, Start.AddSeconds(31), On("hotspot"), Seen("office"));
        Check("전환 실패 뒤 5분 동안 다시 시도하지 않음", Decide(policy, Start.AddSeconds(65), On("hotspot"), Seen("office")).ConnectTo == null);
    }

    private static void StaysOnTopRank()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("office"), Seen("hotspot"));
        Check("1순위에 연결 중이면 옮기지 않음", Decide(policy, Start.AddMinutes(1), On("office"), Seen("hotspot")).ConnectTo == null);
    }

    private static void SleepsOnlyOnBatteryWhenIdle()
    {
        TimeSpan idle = TimeSpan.FromMinutes(10);
        Check("배터리, 입력 없음, 네트워크 없음이면 절전", new ConnectionPolicy().Decide(Names, new VisibleNetwork[0], Start, idle, true, TimingSettings.Default).SleepNow);
        Check("전원 연결 중이면 절전 안 함", !new ConnectionPolicy().Decide(Names, new VisibleNetwork[0], Start, idle, false, TimingSettings.Default).SleepNow);
        Check("입력이 있으면 절전 안 함", !new ConnectionPolicy().Decide(Names, new VisibleNetwork[0], Start, TimeSpan.FromMinutes(9), true, TimingSettings.Default).SleepNow);
        Check("체크한 네트워크가 없으면 절전 안 함", !new ConnectionPolicy().Decide(new string[0], new VisibleNetwork[0], Start, idle, true, TimingSettings.Default).SleepNow);
    }

    private static void DoesNotSleepDuringGrace()
    {
        var policy = new ConnectionPolicy();
        policy.Decide(Names, new[] { On("office") }, Start, Active, true, TimingSettings.Default);
        Decision decision = policy.Decide(Names, new VisibleNetwork[0], Start.AddMinutes(1), TimeSpan.FromMinutes(20), true, TimingSettings.Default);
        Check("끊긴 뒤 유지 시간 안에는 입력이 없어도 절전 안 함", !decision.SleepNow);
    }

    private static void DoesNotRepeatSleepRequestImmediately()
    {
        var policy = new ConnectionPolicy();
        TimeSpan idle = TimeSpan.FromMinutes(10);
        policy.Decide(Names, new VisibleNetwork[0], Start, idle, true, TimingSettings.Default);
        Decision again = policy.Decide(Names, new VisibleNetwork[0], Start.AddSeconds(5), idle, true, TimingSettings.Default);
        Check("절전 요청을 곧바로 반복하지 않음", !again.SleepNow);
    }

    private static void UsesCustomTiming()
    {
        var timing = new TimingSettings(5, 30, 60);
        var policy = new ConnectionPolicy();
        policy.Decide(Names, new[] { On("office") }, Start, Active, true, timing);
        Check("끊김 유지를 5분으로 바꾸면 4분 뒤에도 유지", policy.Decide(Names, new VisibleNetwork[0], Start.AddMinutes(4), Active, true, timing).KeepWorking);
        Check("입력 없음 절전을 30분으로 바꾸면 10분에는 절전 안 함", !new ConnectionPolicy().Decide(Names, new VisibleNetwork[0], Start, TimeSpan.FromMinutes(10), true, timing).SleepNow);
    }

    private static void ParsesAndLimitsSettings()
    {
        TimingSettings parsed = TimingSettings.Parse(new[] { "disconnect-grace-minutes=999", "idle-sleep-minutes=abc", "switch-after-seconds=45" });
        Check("범위를 넘는 값은 최대값으로 제한", parsed.GraceMinutes == TimingSettings.MaxGraceMinutes);
        Check("잘못된 값은 기본값 사용", parsed.IdleMinutes == TimingSettings.Default.IdleMinutes);
        Check("올바른 값은 그대로 사용", parsed.SwitchSeconds == 45);
    }

    private static void NeverTakesOverAnotherConnection()
    {
        var policy = new ConnectionPolicy();
        Check("다른 와이파이에 연결 중이면 끌어오지 않음", Decide(policy, Start, On("cafe"), Seen("hotspot")).ConnectTo == null);
        Check("시간이 지나도 끌어오지 않음", Decide(policy, Start.AddHours(1), On("cafe"), Seen("hotspot")).ConnectTo == null);
    }

    private static void DoesNotSwitchByDefault()
    {
        var policy = new ConnectionPolicy();
        VisibleNetwork[] visible = { On("hotspot"), Seen("office") };
        policy.Decide(Names, visible, Start, Active, true, TimingSettings.Default);
        Decision later = policy.Decide(Names, visible, Start.AddMinutes(10), Active, true, TimingSettings.Default);
        Check("기본 설정에서는 2순위 연결을 유지", later.ConnectTo == null && later.ConnectedName == "hotspot");
    }

    private static void KeepsTryingWhenNothingConnected()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, Seen("hotspot"));
        Check("아무 연결도 없으면 계속 시도", Decide(policy, Start + ConnectionPolicy.TargetAttempt, Seen("hotspot")).ConnectTo == "hotspot");
    }

    private static void TriesNextRankWhenTopStaysUnreachable()
    {
        var policy = new ConnectionPolicy();
        VisibleNetwork[] both = { Seen("office"), Seen("hotspot") };
        TimeSpan attempt = ConnectionPolicy.TargetAttempt;
        Check("처음에는 1순위를 시도", Decide(policy, Start, both).ConnectTo == "office");
        Check("시도 중에는 연결 명령을 다시 보내지 않음", Decide(policy, Start + attempt - TimeSpan.FromSeconds(1), both).ConnectTo == null);
        Check("붙지 못하면 다음 순위로 넘어감", Decide(policy, Start + attempt, both).ConnectTo == "hotspot");
        Check("끝까지 가면 1순위로 돌아감", Decide(policy, Start + attempt + attempt, both).ConnectTo == "office");
    }

    private static void TriesThirdRank()
    {
        var policy = new ConnectionPolicy();
        string[] three = { "office", "home", "hotspot" };
        VisibleNetwork[] all = { Seen("office"), Seen("home"), Seen("hotspot") };
        TimeSpan attempt = ConnectionPolicy.TargetAttempt;
        policy.Decide(three, all, Start, Active, true, SwitchOn);
        policy.Decide(three, all, Start + attempt, Active, true, SwitchOn);
        Check("3순위까지 차례로 시도", policy.Decide(three, all, Start + attempt + attempt, Active, true, SwitchOn).ConnectTo == "hotspot");
        Check("3순위 다음에는 1순위로 돌아감", policy.Decide(three, all, Start + attempt + attempt + attempt, Active, true, SwitchOn).ConnectTo == "office");
    }

    private static void StopsTryingWhenAnotherNetworkConnects()
    {
        var policy = new ConnectionPolicy();
        VisibleNetwork[] both = { Seen("office"), Seen("hotspot") };
        Decide(policy, Start, both);
        Decide(policy, Start.AddSeconds(5), On("cafe"), Seen("office"));
        Check("다른 연결이 끊기면 1순위부터 바로 시도", Decide(policy, Start.AddSeconds(10), both).ConnectTo == "office");
    }

    private static void IgnoresLingeringNetworkAfterDisconnect()
    {
        var policy = new ConnectionPolicy();
        Decide(policy, Start, On("office"));
        Decide(policy, Start.AddSeconds(5), Seen("office"), Seen("hotspot"));
        Decide(policy, Start.AddSeconds(30), Seen("office"), On("hotspot"));
        Check("끊긴 네트워크가 목록에 남아도 정상 연결을 끊지 않음", Decide(policy, Start.AddSeconds(70), Seen("office"), On("hotspot")).ConnectTo == null);
        DateTime later = Start.AddSeconds(5) + ConnectionPolicy.SwitchCooldown;
        Decide(policy, later, Seen("office"), On("hotspot"));
        Check("대기가 끝나면 돌아온 상위 네트워크로 전환", Decide(policy, later.AddSeconds(31), Seen("office"), On("hotspot")).ConnectTo == "office");
    }

    private static void RestartsFromTopAfterConnecting()
    {
        var policy = new ConnectionPolicy();
        VisibleNetwork[] both = { Seen("office"), Seen("hotspot") };
        TimeSpan attempt = ConnectionPolicy.TargetAttempt;
        Decide(policy, Start, both);
        Decide(policy, Start + attempt, both);
        Decide(policy, Start + attempt + TimeSpan.FromSeconds(5), On("hotspot"));
        Check("연결된 뒤 다시 끊기면 1순위부터 시도", Decide(policy, Start + attempt + TimeSpan.FromSeconds(10), both).ConnectTo == "office");
    }

    private static void RejectsUnsafeNetworkNames()
    {
        Check("일반 이름은 허용", Networks.IsSupported("KT_GiGA 5G"));
        Check("한글 이름은 허용", Networks.IsSupported("회사 와이파이"));
        Check("따옴표가 든 이름은 거부", !Networks.IsSupported("a\" interface=\"b"));
        Check("역슬래시로 끝나는 이름은 거부", !Networks.IsSupported("name\\"));
        Check("제어 문자가 든 이름은 거부", !Networks.IsSupported("a\tb"));
        Check("빈 이름은 거부", !Networks.IsSupported(""));
    }

    private static bool Gone(string path, bool fileExists, bool isFixedDrive)
    {
        return SourceProgram.IsGone(path, file => fileExists, root => isFixedDrive);
    }

    private static void TreatsMissingProgramAsGone()
    {
        Check("고정 드라이브에서 파일이 없으면 지워진 것", Gone(@"C:\Tools\Tether.exe", false, true));
        Check("파일이 있으면 지워지지 않은 것", !Gone(@"C:\Tools\Tether.exe", true, true));
    }

    private static void IgnoresUnpluggedDrive()
    {
        Check("USB에서는 파일이 없어도 지워진 것으로 보지 않음", !Gone(@"E:\Tools\Tether.exe", false, false));
        Check("네트워크 폴더는 고정 드라이브가 아님", !SourceProgram.IsFixedDrive(@"\\server\share"));
        Check("없는 드라이브는 고정 드라이브가 아님", !SourceProgram.IsFixedDrive(@"Q:\"));
    }

    private static void IgnoresUnknownSource()
    {
        Check("위치 기록이 없으면 제거하지 않음", !Gone(null, false, true));
        Check("상대 경로 기록은 믿지 않음", !Gone("Tether.exe", false, true));
    }

    private static void ConfirmsRemovalOverRepeatedChecks()
    {
        var watch = new SourceWatch();
        bool due = false;
        for (int check = 1; check < SourceWatch.ChecksToConfirm; check++) due = watch.Observe(true);
        Check("정해진 횟수 전에는 제거하지 않음", !due);
        Check("정해진 횟수만큼 없으면 제거", watch.Observe(true));
    }

    private static void RestartsCountWhenProgramReturns()
    {
        var watch = new SourceWatch();
        for (int check = 1; check < SourceWatch.ChecksToConfirm; check++) watch.Observe(true);
        watch.Observe(false);
        Check("파일이 돌아오면 횟수를 다시 셈", !watch.Observe(true));
    }

    private static void HoldsLidDuringSessionEnd()
    {
        var hold = new SessionEndHold();
        Check("종료가 시작되기 전에는 뚜껑 변경을 막지 않음", !hold.IsActive(Start));
        hold.Start(Start);
        Check("종료 중에는 뚜껑 변경을 막음", hold.IsActive(Start.AddSeconds(5)));
        Check("종료가 취소되면 정해진 시간 뒤 감시가 돌아옴", !hold.IsActive(Start + SessionEndHold.Duration));
    }

    private static void DescribesCommandFailure()
    {
        Check("시간 초과를 종료 코드와 구분함", Shell.DescribeFailure(Shell.TimedOut) == "시간 초과");
        Check("종료 코드를 기록함", Shell.DescribeFailure(1) == "종료 코드 0x00000001");
        Check("강제 종료 코드도 그대로 기록함", Shell.DescribeFailure(unchecked((int)0xC000013A)) == "종료 코드 0xC000013A");
    }

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ConnectsToHighestRankWhenDisconnected();
        IgnoresNetworksNotSelected();
        KeepsWorkingWhileConnected();
        KeepsWorkingDuringGrace();
        SwitchesToHigherAfterStableTime();
        ResetsStableTimeWhenHigherDisappears();
        IgnoresWeakHigherNetwork();
        WaitsAfterFailedSwitch();
        StaysOnTopRank();
        SleepsOnlyOnBatteryWhenIdle();
        DoesNotSleepDuringGrace();
        DoesNotRepeatSleepRequestImmediately();
        UsesCustomTiming();
        ParsesAndLimitsSettings();
        NeverTakesOverAnotherConnection();
        DoesNotSwitchByDefault();
        KeepsTryingWhenNothingConnected();
        TriesNextRankWhenTopStaysUnreachable();
        TriesThirdRank();
        StopsTryingWhenAnotherNetworkConnects();
        IgnoresLingeringNetworkAfterDisconnect();
        RestartsFromTopAfterConnecting();
        RejectsUnsafeNetworkNames();
        TreatsMissingProgramAsGone();
        IgnoresUnpluggedDrive();
        IgnoresUnknownSource();
        ConfirmsRemovalOverRepeatedChecks();
        RestartsCountWhenProgramReturns();
        HoldsLidDuringSessionEnd();
        DescribesCommandFailure();
        Console.WriteLine(failures == 0 ? "모든 시험 통과" : "실패 " + failures + "건");
        return failures == 0 ? 0 : 1;
    }
}
