# PatentMarker 项目与共享代码

PatentMarker 是同一套专利附图标注工具，包含三条衍生路线：Word → AutoCAD、Word → PowerPoint、Word → Visio。Word 是共用的字典生产端，各标注宿主读取同一种 `.dict.json`。CAD 可编辑并回写字典，PPT / Visio 只读取字典；CAD 的修改不自动写回 Word 正文。

主仓库沿用 [Wujiamu/PatentCAD-Annotator](https://github.com/Wujiamu/PatentCAD-Annotator) 地址，统一维护 Word、CAD、PPT、Visio。PPT / Visio 在 `office-com-addin/` 维护，各自的 DLL、COM 身份和安装包继续独立；Office 源码整合基线为提交 `bf471b1532768dcd61524773bb33ef8ca4c7be86`。

| 范围 | 唯一源码位置 | 使用端 |
|---|---|---|
| Word 提取、导出、保存事件与面板 | `vba/`；构建规范加载项在 `word-addin/` | 三条路线共用同一 Word 端 |
| 编号规范化、比较与集合哈希 | `cad-plugin/Shared/IO/NumberIdentity.cs` | 五版 CAD、PPT、Visio |
| 字典模型与只读解析 | `office-com-addin/src/PatentOffice.Shared/PatentDictionary.cs`、`DictionaryReader.cs` | PPT、Visio |
| Office COM 生命周期接口与日志 | `office-com-addin/src/PatentOffice.Shared/ComContracts.cs`、`OfficeDiagnostics.cs` | PPT、Visio |
| CAD 业务与宿主适配 | `cad-plugin/Shared/` 及各版本目录 | 五版 CAD，按实际 SDK 差异适配 |
| PPT 标注、文稿绑定与面板 | `office-com-addin/src/PatentOffice.PowerPoint/` | PowerPoint |
| Visio 标注、ShapeSheet 绑定与面板 | `office-com-addin/src/PatentOffice.Visio/` | Visio |

编号比较器保留现有规范路径与 `PatentMarker.IO` 命名空间，减少移动源码造成的变更。Office 项目通过 MSBuild `Compile Include` 链接这个文件，Office 目录不再存一份相同实现。源码没有 Autodesk 依赖，可分别编译到 .NET 2.0、3.5、4.0、4.5 和 .NET 8 宿主 DLL。源码复用不要求安装公共 DLL，也不要求安装 AutoCAD 才能使用 Office 插件。

Word 使用 VBA，与 C# 消费端共用文件协议和验证语料，不能直接链接 C# 文件。协议见[字典契约](../office-com-addin/docs/dictionary-contract.md)。三端的实体创建、文档事务、文件绑定和 UI 差异真实存在，继续保留各自实现。

## 开发与发布

在主仓库根目录运行：

```powershell
./tools/verify-project-family.ps1 # 七个产品项目必须引用唯一编号比较源码
./build.ps1 -Structure
./build.ps1 -Static
./office-com-addin/verify-code.ps1
```

CAD 按年份构建和打包；PPT / Visio 分别运行 `office-com-addin/package-ppt.ps1`、`office-com-addin/package-visio.ps1`。Office 发布 ZIP 内的 README 来自各自 `*-release-readme.md`，必须包含面板打开与恢复步骤。不要让用户安装另一宿主的 DLL。

统一 CI 保留 CAD 门禁，并运行 Office 构建、代码测试和隔离安装门禁。源码整合与说明更新不构成宿主 UI 验收；旧包的实机证据仅适用于原记录的 DLL/ZIP。入口说明见[主 README](../README.md)和[面板指引](../office-com-addin/docs/open-annotation-panel.md)。
