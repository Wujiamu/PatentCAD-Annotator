PatentMarker 2015 部署说明
===========================

目标环境：AutoCAD 2015-2024 (R20.x-R24.x)，Windows 7 SP1+

文件说明：
  PatentMarker.dll     - CAD 插件主文件
  Newtonsoft.Json.dll  - JSON 解析库（必须与 PatentMarker.dll 同目录）
  install-2015.vbs     - 安装脚本
  vba/                 - Word VBA 模块（6个文件）

安装步骤：
  1. 将 PatentMarker.dll 和 Newtonsoft.Json.dll 放到同一固定目录
  2. 将 install-2015.vbs 也放到同一目录
  3. 双击 install-2015.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

Word 端：
  将 vba/ 下的 6 个模块导入 Word Normal 模板

命令：BZ BZM BZC BZA BZS
