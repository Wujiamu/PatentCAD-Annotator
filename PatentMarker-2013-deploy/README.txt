PatentMarker 2013 部署说明
===========================

目标环境：AutoCAD 2013 / 2014 (R19.x)，Windows 7+

文件说明：
  PatentMarker.dll     - CAD 插件主文件（单文件部署，已内置 JSON 解析）
  install-2013.vbs     - 安装脚本（写注册表）
  uninstall-2013.vbs   - 卸载脚本（清除注册表）
  vba/                 - Word VBA 模块（6个文件）

安装步骤：
  1. 将 PatentMarker.dll 放到固定目录（如 C:\PatentMarker\）
  2. 将 install-2013.vbs 也放到同一目录
  3. 双击 install-2013.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

卸载步骤：
  1. 双击 uninstall-2013.vbs
  2. 重启 AutoCAD

注意事项：
  - 单文件部署：无需 Newtonsoft.Json.dll（已合并进 PatentMarker.dll）
  - 脚本会自动检测 R19.0 (2013) 和 R19.1 (2014)
  - DLL 路径不能含中文（部分环境不兼容）
  - 如果注册表方式不生效，可用 NETLOAD 手动加载

Word 端：
  将 vba/ 下的 6 个模块导入 Word Normal 模板

命令：BZ BZM BZC BZA BZS DAGUOHAO PATBRACE PATBRACEEDIT

`PATBRACE` / `DAGUOHAO` 通过顶部、底部和宽度方向三点创建独立矢量大括号；`PATBRACEEDIT` 支持重新点选控制点或输入高度/宽度调整。
第三点决定中部尖点方向：竖向左/右、横向上/下；外侧肩部位于相反侧。
