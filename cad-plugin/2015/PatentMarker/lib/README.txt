AutoCAD 2015-2024 托管 DLL
==================================

编译前请将以下文件从 AutoCAD 安装目录复制到本目录：

  - acdbmgd.dll
  - acmgd.dll
  - accoremgd.dll

默认安装路径示例：
  C:\Program Files\Autodesk\AutoCAD 2015\
  C:\Program Files\Autodesk\AutoCAD 2024\

注意：
  - 这些 DLL 不随仓库分发（Autodesk 版权）
  - 2015-2024 各版本的 DLL 接口兼容，任选其一即可
  - 内部版本号为 R20.0 (2015) 到 R24.x (2024)
  - 同一份 DLL 可在 2015-2024 全系使用

NuGet 依赖：
  - Newtonsoft.Json 13.0.3（通过 packages.config 还原）
  - 运行 nuget restore 或让 VS 自动还原
