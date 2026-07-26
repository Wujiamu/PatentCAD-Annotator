PatentMarker 2013 部署说明
===========================

目标环境：AutoCAD 2013 / 2014 (R19.x)，Windows 7+

文件说明：
  install-2013.vbs    - 安装脚本（写注册表）
  uninstall-2013.vbs  - 卸载脚本（清除注册表）

安装步骤：
  1. 将 PatentMarker.dll 和 Newtonsoft.Json.dll 放到同一固定目录
  2. 将 install-2013.vbs 也放到同一目录
  3. 双击 install-2013.vbs
  4. 重启 AutoCAD
  5. 命令行输入 BZ 验证

卸载步骤：
  1. 双击 uninstall-2013.vbs
  2. 重启 AutoCAD

重要：
  - PatentMarker.dll 和 Newtonsoft.Json.dll 必须在同一目录
  - Newtonsoft.Json.dll 从 NuGet packages 目录获取：
    packages\Newtonsoft.Json.13.0.3\lib\net40\Newtonsoft.Json.dll
  - 脚本会自动检测 R19.0 (2013) 和 R19.1 (2014)
