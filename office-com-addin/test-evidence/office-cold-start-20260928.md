# Office 0.1.2.0 / 0.1.4.0 冷启动验收

- 时间：2026-09-28（Asia/Shanghai）
- 执行身份：本机当前用户（完整执行身份保存在本机原始证据）
- 源码基线：`a1e8886`；工作树有未提交修改
- 宿主：Windows 10 22H2 x64；PowerPoint 与 Visio `16.0.20430.20092`，均为 64 位；.NET Framework 4.8.1
- 输入字典：安装版 Word UI 在 2026-09-24 导出的脱敏两项字典 `word-installed-export-20260924-bb620f8f/visio-word-export-test.dict.json`

## PowerPoint 0.1.2.0

**结果：PASS（安装包冷启动加载 + 真实 PowerPoint COM 对象与保存恢复；未操作面板 UI）**

- 最终发布包：`PatentMarker-WordPowerPoint-0.1.2.0-20260928-153831.zip`，SHA-256 `FE681EA89300BD85D75FB756B568FF0C443E6CDD2B7B3D44BDB4F1671E023CE7`。包内容校验通过，最终 ZIP 已安装至 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint`；最终包安装后的冷启动 run ID 为 `45883eb62ec54421be899783191cc7d0`。
- COM 对象业务回归在两个全新 PowerPoint 进程 `f34bd17208474db7986f96b84db9d9a7`、`91ebf547f9474a8092b9cc0691b3fd60` 上完成；最终包的冷启动日志也记录产品 0.1.2.0、64 位、实际程序集路径，以及 `OnConnection`、`OnStartupComplete` 成功。
- 实际加载文件：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\PowerPoint\PatentOffice.PowerPoint.dll`，SHA-256 `94BDAFC2015C0C01AEAF8D148FAAC73CC027727CE23561C3149C6B6C48DDD17E`。
- 真实 Word 字典绑定到 COM 生成的两页测试稿；普通图片选择被拒绝；直线标注编号 1、2，同号重复标注接受；保存关闭后重开，绑定及编号在同一进程恢复。另一次全新 PowerPoint 进程打开保存稿，仍恢复绑定与编号 1、2。
- 字典文件在同进程与新进程检查前后均逐字节相同。测试演示文稿：`powerpoint-picture-test.pptx`（本机原始产物）。对象模型回归结果：PPT `result.json`（本机原始产物）。
- 范围限制：测试通过 PowerShell/COM 与反射调用产品方法，没有在面板里点击或通过鼠标画线；本结果不能替代面板 UI 的 L3 用户操作验收。原有 0.1.1.0 面板 L3 证据仍只适用于其历史 DLL/ZIP。

## Visio 0.1.4.0

**结果：SKIP（正向断言通过；三个必要负例跳过，整套回归不算 PASS）**

- 最终发布包：`PatentMarker-WordVisio-0.1.4.0-20260928-153831.zip`，SHA-256 `8D79DDE65969122F07FD463963B6BF19743483F73324EB05F1F9BFF9A0E92B38`。包内容校验通过，最终 ZIP 已安装至 `%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio`；最终包安装后的冷启动 run ID 为 `3ef3add143c946f2af85da87f5adb5a3`。
- 正向业务测试进程 run ID：`e4997f9acb36409f9c0e58687aa9bc7b`；保存稿的新进程 run ID：`c2aa747c48aa4ad8a1468f6a190c649b`。它们与最终包加载的 DLL 哈希一致；最终包冷启动日志同样记录产品 0.1.4.0、64 位、实际程序集路径，且 `OnConnection`、`OnStartupComplete` 成功。
- 实际加载文件：`%LOCALAPPDATA%\PatentMarker\OfficeAddin\Visio\PatentOffice.Visio.dll`，SHA-256 `F679ED672CA9AE015CD3A5ACEC23E09507A7D3D3E3991250C76C84769398932A`。
- 在真实 Visio COM 对象中绑定 Word 字典、绘制/选择一维线、创建编号 1 标注、检查扫描结果、保存并关闭重开均通过；新进程打开 VSDX 后，字典绑定和编号 1 仍可识别，损坏标注数为 0。字典文件在两轮测试前后逐字节相同。
- 正向回归：Visio `result.json`（本机原始产物）；阶段记录：`steps.log`（本机原始产物）；产物：`visio-host-test.vsdx`（本机原始产物）。
- 本轮宿主 COM 脚本通过外部反射执行空选择负例时停在 `emptyselection.mark.begin`。当时检测到 Visio `NUIDialog`（标题 `Microsoft Visio`）；未用鼠标、键盘或 Computer Use 处理对话框。为完成正向宿主链，重跑时明确跳过空选择、普通矩形和重复线三个负例。原始脚本错误地给出总体 PASS；2026-10-01 复核后将该总体结论更正为 SKIP，并修正脚本的分类。当天的 20 项代码测试也没有覆盖这些标注入口负例，原记录对此有误；2026-10-01 才补齐针对性测试。不能把这三项说成 2026-09-28 的宿主或代码实测通过。
- 范围限制：使用 PowerShell/COM 与反射调用产品方法，没有在面板里操作控件；本结果不能替代面板 UI 的 L3 用户操作验收。Visio UI 面板在启动时出现，但没有通过面板完成绑定或点击标注。

## 汇总

- 两个当前安装包的清单、版本及包内容校验均通过；安装后的全新宿主进程加载了清单所列产品 DLL。
- PowerPoint 7/7、Visio 20/20 代码测试通过；本页的本机 COM 测试结果单独按上述范围标注。
- 验收断言没有依赖 Word 字典或演示文稿哈希；字典是否改变通过逐字节比较确认。现有字典读取器仍会在其本地诊断日志中写入内容哈希。按本机项目验收约定，交付记录只保留发布 ZIP 与宿主实际加载 DLL 的 SHA-256。
- Windows 7 + Office 2010 x86、鼠标交互、Office 面板点击流程均未由本轮测试覆盖。

原始 COM 结果、测试图稿与日志位于本机忽略的 test-evidence/office-cold-start-20260928/；本文件为可随源码提交的脱敏摘录。
