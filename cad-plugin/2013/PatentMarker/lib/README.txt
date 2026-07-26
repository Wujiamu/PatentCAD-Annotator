AutoCAD 2013/2014 托管 DLL
==================================

编译前请将以下文件从 AutoCAD 安装目录复制到本目录：

  - acdbmgd.dll
  - acmgd.dll
  - accoremgd.dll    (2013 起新增)

默认安装路径示例：
  C:\Program Files\Autodesk\AutoCAD 2013\
  C:\Program Files\Autodesk\AutoCAD 2014\

注意：
  - 这些 DLL 不随仓库分发（Autodesk 版权）
  - 2013 和 2014 的 DLL 接口兼容，任选其一即可
  - 内部版本号为 R19.0 (2013) / R19.1 (2014)
  - accoremgd.dll 是 2013 新增的核心托管程序集

NuGet 依赖：
  - Newtonsoft.Json 13.0.3（通过 packages.config 还原）
  - 运行 nuget restore 或让 VS 自动还原
