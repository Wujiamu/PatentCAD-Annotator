# Visio 真实宿主失败契约

## 部署与环境

- 日期：2026-09-24（本地时间）
- 执行身份：`本机用户`
- Visio：Microsoft Visio 16.0.20430.20092 x64；测试前通过 COM 读取版本 16.0，测试前 `Documents.Count=0`
- 实际安装 DLL：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`
- 测试时部署 DLL SHA-256：`D18D49B1335704BB7FF04D6172A4C280FD2989F98E9925FDDE04EB5037FE0D97`
- Visio 进程冷启动 run ID：`77ebb9fc90444e83b058f01f014fd7a2`；生命周期日志记录 `OnConnection`、`OnAddInsUpdate`、`OnStartupComplete` 均 PASS。
- 实际宿主测试调用 ID：`d370ad5361644b15a41741ea958b43c7`。此测试从外部 PowerShell 连接运行中的真实 Visio COM 宿主，并通过反射调用部署程序集内部生产方法；它没有经过面板按钮，不能作为 L3 用户路径证据。

## 前置文件与操作

- 仅新建脱敏测试材料：`visio-host-com-20260924-193219-d370ad53\word-export-shape-fixture.dict.json` 与同目录 `visio-host-test.vsdx`。
- 字典 fixture SHA-256：`E1DA5334883DFE774CBC836A435181DDF0C61DBE13A593EA196325B233EC9856`。它按产品字典 DTO 结构生成，不是 Word UI 导出文件。
- 最短步骤：Visio COM 创建并保存空白 `.vsdx` → 读取字典 fixture → 调用生产绑定方法并确认路径解析 → 用 `Page.DrawLine(1,3,3,1)` 创建独立一维线 → `Window.Select(line, 2)` 选中 → 反射调用已部署程序集的 `VisioAnnotations.MarkSelectedLine(application, entry)`。
- 预期：独立的一维直线通过组成员检查，终点设置箭头，在起点创建编号框，扫描得到编号 `1` 且损坏数为 0。
- 实际：绑定步骤通过；标注方法在处理 `line.ContainingShape` 时抛出 `InvalidOperationException: 暂不支持组内的线条。请先取消组合，或在页面上绘制独立线条。`。箭头、标签、扫描、保存重开断言未执行。
- 产物：`visio-host-test.vsdx` SHA-256 `56F1D334AFF500934748394908AE8F4DDA9D21E1ED8D473258D7FC1935B7CA9E`；逐字节读取确认测试期间字典 hash 未变。
- 阻断位置：生产 `MarkSelectedLine` 对 `ContainingShape != null` 一概视为组内线条。Microsoft 的 `Shape.ContainingShape` 文档明确：未分组的顶层形状返回 PageSheet；组成员才返回其组。因此当前拒绝条件会误拒绝普通顶层直线。[Shape.ContainingShape 文档](https://learn.microsoft.com/en-us/office/vba/api/visio.shape.containingshape)。PageSheet 的 `Type` 是 `visTypePage=1`，组形状为 `visTypeGroup=2`。[VisShapeTypes 枚举](https://learn.microsoft.com/en-us/office/vba/api/visio.visshapetypes)。

## 修复前判定

- **FAIL**：部署程序集的真实 Visio COM 标注调用拒绝顶层一维线。
- 这不证明用户面板按钮路径失败，因为本次调用绕过了 UI；但直接命中了面板调用的同一生产方法，故需以同一宿主 COM 场景修复后复测。
- 安装 DLL、Visio 加载生命周期和文档字典绑定均有独立 PASS 证据；本次没有修改或关闭任何用户文档。
