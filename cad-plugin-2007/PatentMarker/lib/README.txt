============================================================
 PatentMarker 2007 - lib 目录说明
============================================================

本目录用于存放 AutoCAD 2007 托管 SDK 程序集，供编译时
引用（HintPath）。由于 Autodesk 版权限制，这些 DLL 不会
进入 git 仓库，请从本机 AutoCAD 2007 安装目录复制过来：

  需要的文件：
    acdbmgd.dll
    acmgd.dll

  来源路径（典型）：
    C:\Program Files\Autodesk\AutoCAD 2007\acdbmgd.dll
    C:\Program Files\Autodesk\AutoCAD 2007\acmgd.dll

  复制命令示例（PowerShell）：
    $acad = "C:\Program Files\Autodesk\AutoCAD 2007"
    Copy-Item "$acad\acdbmgd.dll" .\lib\
    Copy-Item "$acad\acmgd.dll"  .\lib\

说明：
  - csproj 中 <Private>false</Private>，仅用于编译期引用，
    运行时由 AutoCAD 2007 进程内存加载，无需随 DLL 分发。
  - 若本机 AutoCAD 安装在其他路径，请相应调整上述命令。
  - probe.exe 为环境探测工具，非编译所需，可不放回。
============================================================
