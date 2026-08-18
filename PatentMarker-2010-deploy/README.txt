PatentMarker 2010 部署说明
===========================
版本：1.0.0 (2026-08-18)  首个正式发布


目标环境：AutoCAD 2010 / 2011 / 2012 (R18.x)，Windows 7+

文件说明：
  PatentMarker.dll     - CAD 插件主文件
  install-2010.vbs    - 安装脚本（写注册表 + 部署 LSP）
  uninstall-2010.vbs  - 卸载脚本（清除注册表）
  doctor-2010.vbs     - 诊断脚本（CAD 外排查，见下方"诊断"）
  vba/                - Word VBA 文件（7个文件：6个模块 + 1个面板 UserForm）

安装步骤：
  1. 将 PatentMarker.dll 放到固定目录（如 C:\PatentMarker\）
  2. 将本目录下的 install-2010.vbs 也放到同一目录
  3. 双击 install-2010.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

卸载步骤：
  1. 双击 uninstall-2010.vbs
  2. 重启 AutoCAD

Word 端：
  将 vba/ 下的所有文件导入 Word Normal 模板（包括 PatentDictPanel.frm 和 .frx）
  安装后运行宏 ShowPatentDictPanel 打开"专利标注字典工具"面板

注意事项：
  - 脚本会自动检测 R18.0/R18.1/R18.2（2010/2011/2012）
  - DLL 路径不能含中文（部分环境不兼容）
  - 如果注册表方式不生效，可用 NETLOAD 手动加载

诊断（doctor）：
  插件无法加载或 BZD 命令不可用时，无需进入 AutoCAD 即可排查。
  双击 doctor-2010.vbs，或命令行运行:
      cscript doctor-2010.vbs
  - 离线层：检查 PatentMarker.dll、自动加载注册表及 LOADER 指向、
    所需 .NET Framework 3.5、PatentMarker.log 尾部
  - 在线层：自动以批处理模式启动本版本范围内的 AutoCAD，
    NETLOAD 部署 DLL 并执行 PATDOCTOR 生成 CAD 内诊断报告
  - 仅做离线检查: cscript doctor-2010.vbs offline
  - 报告输出到本目录 PatentMarker-doctor-offline-report.txt
    （以及 CAD 内诊断报告 PatentMarker-doctor-report.txt）
  - 运行在线层前请先关闭已打开的 AutoCAD

命令：
  BZ / PATPALETTE        打开字典面板
  BZM / PATMARK          创建引线标注
  BZC / PATCHECK         漏标检测：报告"字典有 · 图纸未标注"清单
  BZA / PATALIGN         对齐标注文字（先选标注，再选线/框基准）
  BZS / PATSELECTALL     全选 PAT 标注实体
  BZD / PATDOCTOR        插件自检并生成诊断报告
  PATMLSET / PATMLVERIFY MLeader scriptable switches and form diagnostic
  DAGUOHAO / PATBRACE    三点创建矢量大括号
  PATBRACEEDIT           通过控制点或输入高度/宽度调整大括号
  第三点决定中部尖点方向：竖向左/右、横向上/下；外侧肩部位于相反侧
