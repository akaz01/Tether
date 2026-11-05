// 도움말 창: 사용 순서, 동작 규칙, 버튼 기능, 주의할 점.
using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class HelpForm : Form
{
    private const int ContentWidth = 500;

    private static readonly string[][] Sections =
    {
        new[]
        {
            "Tether는 무엇인가요",
            "지정한 네트워크에 연결돼 있으면 노트북 뚜껑을 닫아도 작업이 계속되게 합니다. 연결돼 있지 않으면 평소처럼 뚜껑을 닫을 때 절전합니다."
        },
        new[]
        {
            "사용 순서",
            "1. 네트워크 추가를 눌러 핫스팟이나 와이파이를 고릅니다.\n2. 최대 3개까지 등록할 수 있습니다. ▲▼로 순위를 바꿉니다.\n3. 활성화를 누릅니다.\n\n창을 닫아도 Tether는 뒤에서 계속 동작합니다."
        },
        new[]
        {
            "어떻게 동작하나요",
            "• 등록한 네트워크에 연결됨: 뚜껑을 닫아도 작업이 계속됩니다.\n• 연결이 끊김: 끊김 유지 시간 동안 작업을 유지하며 다음 네트워크를 찾습니다.\n• 등록한 네트워크에 연결 안 됨: 뚜껑을 닫으면 절전합니다.\n• 와이파이가 끊기면 등록한 네트워크에 1순위부터 차례로 연결합니다.\n• 사용 중인 연결은 기본으로 뺏지 않습니다. 설정에서 상위 네트워크 전환을 켜면 등록한 네트워크에 연결 중일 때 더 높은 순위로 옮깁니다. 더 높은 순위가 설정한 시간 동안 충분한 신호로 보여야 옮기고, 주변 검색 주기 때문에 1분쯤 더 걸릴 수 있습니다. 연결이 끊긴 직후나 전환 직후 5분 동안은 옮기지 않습니다.\n• 배터리에서 연결도 입력도 없음: 설정한 시간이 지나면 절전합니다."
        },
        new[]
        {
            "버튼과 메뉴",
            "• 활성화/비활성화: 기능을 켜고 끕니다. 비활성화하면 뚜껑을 닫을 때 절전합니다.\n• 자동 시작: 로그인할 때 Tether를 자동으로 실행합니다.\n• 네트워크 추가: 이 컴퓨터에 저장된 네트워크에서 고릅니다. 새로고침으로 주변을 다시 찾습니다.\n• ✕: 등록한 네트워크를 목록에서 뺍니다.\n• 설정: 시간, 화면 끄기, 배터리 저발열을 조절합니다. 기록 보기도 여기에 있습니다.\n• 시계 옆 아이콘: 눌러서 활성화/비활성화를 바꾸거나 창을 엽니다. 초록 점은 활성화, 회색 점은 비활성화입니다."
        },
        new[]
        {
            "제거와 폴더 옮기기",
            "• 제거: Tether 창을 닫고 Tether.exe가 있는 폴더를 지웁니다. 30초쯤 뒤 뚜껑 설정을 되돌리고 자동 시작과 기록을 정리한 뒤 Tether가 꺼집니다. 등록한 네트워크와 설정은 남아서 다시 받으면 이어서 쓸 수 있습니다.\n• 폴더 옮기기: 먼저 비활성화하고 폴더를 옮긴 뒤, 새 위치의 Tether.exe를 여세요. 활성화한 채 옮기면 제거로 처리됩니다.\n• 비활성화한 채 폴더를 지우면 자동 시작이 켜져 있을 때 다음 로그인에서 정리됩니다."
        },
        new[]
        {
            "꼭 알아 두세요",
            "• 뚜껑을 닫은 뒤 연결이 끊기면 절전되지 않고 켜진 채로 있을 수 있습니다.\n• 무거운 작업 중에 가방에 넣으면 뜨거워질 수 있습니다. 가능하면 환기되는 곳에 두세요.\n• 휴대폰 핫스팟은 노트북이 켤 수 없습니다. 미리 켜 두고, 자동 꺼짐 기능을 꺼 주세요.\n• 네트워크가 목록에 없으면 Windows에서 그 네트워크에 먼저 한 번 연결해 주세요.\n• 보안 프로그램이 자동 시작을 막으면 그 프로그램에서 Tether를 허용해 주세요."
        }
    };

    private readonly Palette palette;

    public HelpForm(Palette palette, Icon icon)
    {
        this.palette = palette;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Tether 도움말";
        Icon = icon;
        Font = new Font(Ui.FontName, 10f);
        BackColor = palette.Background;
        ForeColor = palette.Text;
        ClientSize = new Size(Ui.S(560), Ui.S(600));
        MinimumSize = new Size(Ui.S(560), Ui.S(360));
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(BuildContent());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.StyleTitleBar(this, palette);
    }

    private Control BuildContent()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = Ui.Pad(24, 20, 16, 20)
        };
        foreach (string[] section in Sections)
        {
            flow.Controls.Add(Heading(section[0]));
            flow.Controls.Add(Body(section[1]));
        }
        return flow;
    }

    private Label Heading(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Ui.FontName, 11f, FontStyle.Bold),
            ForeColor = palette.Text,
            Margin = new Padding(0, 0, 0, Ui.S(6))
        };
    }

    private Label Body(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Ui.S(ContentWidth), 0),
            ForeColor = palette.Muted,
            Margin = new Padding(0, 0, 0, Ui.S(20))
        };
    }
}
