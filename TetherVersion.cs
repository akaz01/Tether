// 파일 속성에 표시되는 프로그램 정보.
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Tether")]
[assembly: AssemblyProduct("Tether")]
[assembly: AssemblyDescription("지정한 네트워크에 연결돼 있으면 노트북 뚜껑을 닫아도 작업을 유지합니다.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// 같은 폴더의 가짜 DLL이 불러와지지 않도록 시스템 폴더에서만 찾는다.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
