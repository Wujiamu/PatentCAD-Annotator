# R10 ACL修复闭环

R9-ACL-01已按原精确SDDL断言修复。原GitHub Windows2025反例在push、PR各139/139通过；本机139+58+61+61+61=380项唯一测试零跳过，五集合三轮结果一致。实际旧/新DLL使用同一16项NTFS矩阵，旧两项失败，新全部通过，各三轮。

共享写入在替换后核对原Owner/Group/DACL；原描述没有AUTO_INHERITED且被替换API转换时，DACL-only恢复旧式状态；恢复拒绝则移回原备份文件，保留字节、属性、ADS、创建时间和权限。没有SACL或系统权限变更。新增父目录撤权传播检查防止继承变成持久显式权限。

原选择器：`PatentMarker.Tests.DictWriterBoundaryTests.Replace_PreservesNamedStreamAclAndCreationTime`。

新增同类选择器：`LegacyInheritedAcl_WithStreamsAndCreationTime_IsPreservedExactly`、`LegacyAclRepairDenied_RollsBackOriginalBytesSecurityAndStreams`、`LegacyInheritedPermission_RemainsInheritedAfterParentRevocation`，均属于`PatentMarker.Tests.DictWriterBoundaryTests`。前两项有相同夹具红绿，第三项为新增邻界正向检查。

```powershell
dotnet test ./cad-plugin/2025/PatentMarker.Tests/PatentMarker.Tests.csproj -c Release --filter FullyQualifiedName~PatentMarker.Tests.DictWriterBoundaryTests --nologo
```

实际候选、身份、证据与未覆盖边界见[修复后验收](cad-acceptance-20261022-r10.md)；R9封存FAIL保留。GUI工具仍BLOCKED，四旧组仅GitHub外部实机；代码修复通过不冒充全支持范围放行。

参考：[ReplaceFile](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)、[SetFileSecurity](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-setfilesecurityw)。
