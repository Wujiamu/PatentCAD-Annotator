# PatentCAD-Annotator 2015 — AutoCAD 2015~2024 适配版

> **状态：已完成**
> 源码从 `cad-plugin/2013/` 派生；标注引擎为 MLeader（F 方案三点顶点链），Newtonsoft.Json 经 ILRepack 合并进单 DLL 部署。
>
> **兼容性：本版本仅适用于 AutoCAD 2015 ~ 2024，不可用于其他版本的 AutoCAD。**

## 目标环境

| 项目 | 值 |
|------|-----|
| 操作系统 | Windows 7 SP1 — Windows 11 |
| AutoCAD | 2015, 2016, ..., 2024 (R20.0—R24.x) |
| .NET | 4.5 (CLR 4.0，兼容 4.5—4.8) |
| 编译器 | VS2015+ |
| 托管程序集 | acdbmgd.dll + acmgd.dll + accoremgd.dll |
| JSON | Newtonsoft.Json 13.0.3 |

## 与 2013 版的差异

| 特性 | 2013 版 | 2015 版 |
|------|---------|---------|
| .NET | 4.0 | 4.5 |
| 标注方式 | MLeader（F 方案） | MLeader（F 方案，相同） |
| JSON | Newtonsoft.Json | Newtonsoft.Json（相同） |
| 覆盖范围 | 2013-2014 | 2015-2024（覆盖最广） |
| 部署 | HKCU 注册表 | HKCU 注册表 |

## 编译步骤

1. 从 AutoCAD 2015-2024 任一版本安装目录复制 `acdbmgd.dll`、`acmgd.dll`、`accoremgd.dll` 到 `PatentMarker/lib/`
2. 还原 NuGet 包：`nuget restore` 或 VS 自动还原
3. 用 VS2015+ 打开 `PatentMarker/PatentMarker.csproj`
4. 编译 Release|AnyCPU
5. 输出：`PatentMarker/bin/Release/PatentMarker.dll`（发布前需用 ILRepack 把 Newtonsoft.Json 合并进单 DLL，见根目录 README"构建与验证"）

## 部署

1. 将 `PatentMarker-2015-deploy/` 整个文件夹复制到目标机器固定目录
2. 双击运行 `PatentMarker-2015-deploy/install-2015.vbs`
3. 重启 AutoCAD
4. 命令行输入 `BZ` 验证

## 命令清单

| 命令 | 拼音别名 | 功能 |
|------|---------|------|
| `PATPALETTE` | `BIAOZHU` / `BZ` | 打开字典面板 |
| `PATMARK` | `BZM` | 创建 MLeader 引线标注（F 方案三点顶点链） |
| `PATCHECK` | `BZC` | 漏标检测：报告"字典有 · 图纸未标注"清单并在面板高亮 |
| `PATALIGN` | `BZA` | 对齐标注文字（先选标注，再选线/框基准；空间不足时自动延伸排列） |
| `PATSELECTALL` | `BZS` | 全选 PAT 标注实体 |
| `PATMLSET` | — | MLeader 脚本化开关 |
| `PATMLVERIFY` | — | MLeader 形态诊断报告（对照记录点链校验） |
| `PATBRACE` | `DAGUOHAO` | 三点创建独立参数化矢量大括号 |
| `PATBRACEEDIT` | — | 通过控制点或输入高度/宽度调整大括号 |
| `PATDOCTOR` | `BZD` | 插件自检并生成诊断报告 |

## 面板交互

- Word 附图标记识别支持 123A、123A1、123A2 等字母后继续数字的编号；编号在逗号、顿号、分号或句号等既有标点处分隔。
- 默认点数模式为三点；点击“点数”按钮后切换为无限点。
- 三点或无限点标注过程中，按 ESC 或右键菜单中的“确认/取消”都可以退出当前标注命令；无限点采集到一半时也可以直接取消。
- 单击条目只选择，双击条目直接进入 `PATMARK` 标注。
- 右键条目选择“编辑条目”，或选中后按 `F2` 修改编号/名称；编辑窗口只负责保存、删除或取消。
- 标注用 **MLeader（F 方案）**：单个多重引线实体自持 MText，顶点链为 `附着点 → 拐点… → 缩进端点`（末顶点沿最后一段方向缩进 0.4×字高，不直接触及文字），文字仍锚定在 `TextLocation`；并禁用全部自动几何（dogleg/landing/extend），无鱼钩、无额外附着点。
- 用户点链同时写入扩展字典 `PATENTMARKER_MLEADER`（Xrecord），供 `PATSELECTALL` 识别与 `PATMLVERIFY` 形态诊断对照。
- `PATMLVERIFY` 会 Explode 全部 PAT MLeader 并对照记录点链输出报告，是回归测试工具；对齐（`PATALIGN`）移动文字时末顶点自动跟随并同步重写点链，因此对齐后仍能通过校验。
- `ExtendLeaderToText` 为 2014+ SDK 属性，代码中以反射访问以保持单一源码兼容。
- 面板新增"检测/对齐"按钮，分别触发 `PATCHECK`（漏标检测，完成后高亮未标注条目）与 `PATALIGN`（选择集先行的线/框对齐）。
- 新增条目仍通过面板"新增条目"按钮进入编辑窗口。
- 面板"Brace/大括号"按钮启动 `PATBRACE`；`PATBRACEEDIT` 可重新点选控制点，也可直接输入高度和宽度。大括号是独立 Polyline，不参与 Leader/MText 关联。
- 第三点决定中部尖点方向：竖向可向左/向右，横向可向上/向下；外侧肩部自动位于相反侧。

详见 [docs/version-plan.md](../../docs/version-plan.md)。
