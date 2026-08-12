PatentMarker 2010 部署说明
===========================

目标环境：AutoCAD 2010 / 2011 / 2012 (R18.x)，Windows 7+

文件说明：
  install-2010.vbs    - 安装脚本（写注册表 + 部署 LSP）
  uninstall-2010.vbs  - 卸载脚本（清除注册表）

安装步骤：
  1. 将 PatentMarker.dll 放到固定目录（如 C:\PatentMarker\）
  2. 将本目录下的 install-2010.vbs 也放到同一目录
  3. 双击 install-2010.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

卸载步骤：
  1. 双击 uninstall-2010.vbs
  2. 重启 AutoCAD

注意事项：
  - 脚本会自动检测 R18.0/R18.1/R18.2（2010/2011/2012）
  - DLL 路径不能含中文（部分环境不兼容）
  - 如果注册表方式不生效，可用 NETLOAD 手动加载

命令：
  BZ / PATPALETTE        打开字典面板
  BZM / PATMARK          创建引线标注
  BZC / PATCHECK         校验一致性
  BZA / PATALIGN         对齐引线
  BZS / PATSELECTALL     全选 PAT 标注实体
  DAGUOHAO / PATBRACE    三点创建矢量大括号
  PATBRACEEDIT           通过控制点或输入高度/宽度调整大括号
  第三点决定中部尖点方向：竖向左/右、横向上/下；外侧肩部位于相反侧
