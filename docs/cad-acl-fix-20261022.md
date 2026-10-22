# R10 ACL修复验证记录（进行中）

原R9-ACL-01在GitHub Windows2025上失败，精确SDDL断言不变。通过在隔离临时目录设置旧式继承状态，本机也稳定复现该类变化；三个新测试覆盖精确ACL/ADS/创建时间、ACL恢复被拒绝时回滚、上级撤销权限后的继承传播。

修复：使用有原文件备份的ReplaceFile；保留其对ADS、压缩、创建时间的处理，合并后严格校验Owner/Group/DACL。只有原描述不带AUTO_INHERITED且合并改变它时，使用低层DACL写入避免再次转换；现代继承走标准API；仍不一致则原文件完整回滚。未申请SACL权限，不改全局系统设置。不存在覆盖新数据的Move兜底；MoveFileEx只恢复原备份文件。

本机新用例红136PASS/2FAIL；修复后139/139连续三轮通过，五版SDK编译通过。独立GitHub同一原反例正在重跑，最终候选尚未冻结。R9已封存FAIL报告保留，不能拿这份进行中记录当发布PASS。

原始选择器：`PatentMarker.Tests.DictWriterBoundaryTests.Replace_PreservesNamedStreamAclAndCreationTime`。

新选择器：`LegacyInheritedAcl_WithStreamsAndCreationTime_IsPreservedExactly`、`LegacyAclRepairDenied_RollsBackOriginalBytesSecurityAndStreams`、`LegacyInheritedPermission_RemainsInheritedAfterParentRevocation`（均属于DictWriterBoundaryTests）。

参考：[ReplaceFile](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)、[SetFileSecurity](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-setfilesecurityw)。低层API限定于恢复原有旧式状态，恢复后还要用同一SDDL判定，避免放宽测试。
