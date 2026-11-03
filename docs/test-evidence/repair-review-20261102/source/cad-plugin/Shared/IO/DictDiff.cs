using System.Collections.Generic;
using PatentMarker.I18n;

namespace PatentMarker.IO
{
    /// <summary>
    /// v2.2：字典对比 — 双向匹配新旧字典，输出每个条目的变化状态。
    ///
    /// 匹配规则（双向）：
    ///  1. 先按 Number 精确匹配
    ///  2. 剩余条目按 Name 精确匹配
    ///  3. 都匹配不上 → Unmatched
    ///
    /// 状态分类：
    ///  - Added      : 新字典有，旧字典没有
    ///  - Removed    : 旧字典有，新字典没有（在旧列表中展示）
    ///  - NumberChanged : 编号变了，名称没变（按名称匹配上）
    ///  - NameChanged   : 名称变了，编号没变（按编号匹配上）
    ///  - BothChanged   : 编号和名称都变了（无法自动匹配）
    ///  - Unchanged     : 完全相同
    /// </summary>
    public class DictDiffEntry
    {
        // 新版条目（Added/Unchanged/*Changed 时非空；Removed 时为 null）
        public DictEntry NewEntry;
        // 旧版条目（Removed/Unchanged/*Changed 时非空；Added 时为 null）
        public DictEntry OldEntry;

        public DiffStatus Status;

        public string Number { get { return NewEntry != null ? NewEntry.Number : (OldEntry != null ? OldEntry.Number : ""); } }
        public string Name { get { return NewEntry != null ? NewEntry.Name : (OldEntry != null ? OldEntry.Name : ""); } }

        // 旧值（用于对照列显示；无变化时为空）
        public string OldNumber { get { return OldEntry != null ? OldEntry.Number : ""; } }
        public string OldName { get { return OldEntry != null ? OldEntry.Name : ""; } }
    }

    public enum DiffStatus
    {
        Unchanged,
        Added,
        Removed,
        NumberChanged,   // 编号变，名称同（按名称匹配）
        NameChanged,     // 名称变，编号同（按编号匹配）
        BothChanged      // 两者都变（无法匹配）
    }

    public static class DictDiff
    {
        /// <summary>
        /// 计算新旧字典的差异。
        /// 返回的列表包含：所有新条目（Added/Unchanged/NumberChanged/NameChanged/BothChanged）
        /// + 旧字典中已被删除的条目（Removed）。
        /// </summary>
        public static List<DictDiffEntry> Compute(DictModel oldDict, DictModel newDict)
        {
            List<DictDiffEntry> result = new List<DictDiffEntry>();

            if (oldDict == null && newDict == null) return result;
            if (newDict == null)
            {
                // 全部删除
                if (oldDict != null)
                    foreach (DictEntry e in oldDict.Entries)
                        result.Add(MakeRemoved(e));
                return result;
            }
            if (oldDict == null)
            {
                // 全部新增
                foreach (DictEntry e in newDict.Entries)
                    result.Add(MakeAdded(e));
                return result;
            }

            // 1. 按 Number 匹配
            List<DictEntry> oldUnmatched = new List<DictEntry>();  // 旧版未按编号匹配上的
            foreach (DictEntry oldE in oldDict.Entries)
            {
                // 找新字典中同编号的条目
                DictEntry matchedNew = null;
                foreach (DictEntry newE in newDict.Entries)
                {
                    if (NumberIdentity.AreEqual(newE.Number, oldE.Number))
                    {
                        matchedNew = newE;
                        break;
                    }
                }

                if (matchedNew != null)
                {
                    // 编号匹配上
                    if (matchedNew.Name == oldE.Name)
                        result.Add(MakePair(oldE, matchedNew, DiffStatus.Unchanged));
                    else
                        result.Add(MakePair(oldE, matchedNew, DiffStatus.NameChanged));
                }
                else
                {
                    oldUnmatched.Add(oldE);  // 留给第二步按名称匹配
                }
            }

            // 2. 旧字典未按编号匹配上的，按 Name 匹配
            foreach (DictEntry oldE in oldUnmatched)
            {
                DictEntry matchedNew = null;
                foreach (DictEntry newE in newDict.Entries)
                {
                    if (newE.Name == oldE.Name && !ContainsEntry(result, newE))
                    {
                        matchedNew = newE;
                        break;
                    }
                }

                if (matchedNew != null)
                {
                    // 名称匹配上，编号变了
                    result.Add(MakePair(oldE, matchedNew, DiffStatus.NumberChanged));
                }
                else
                {
                    // 都匹配不上 → Removed（旧版独有）
                    result.Add(MakeRemoved(oldE));
                }
            }

            // 3. 新字典中未出现在结果里的 → Added
            foreach (DictEntry newE in newDict.Entries)
            {
                if (!ContainsEntry(result, newE))
                    result.Add(MakeAdded(newE));
            }

            return result;
        }

        private static bool ContainsEntry(List<DictDiffEntry> list, DictEntry newE)
        {
            foreach (DictDiffEntry d in list)
            {
                if (d.NewEntry == newE) return true;
            }
            return false;
        }

        private static DictDiffEntry MakePair(DictEntry oldE, DictEntry newE, DiffStatus status)
        {
            DictDiffEntry d = new DictDiffEntry();
            d.OldEntry = oldE;
            d.NewEntry = newE;
            // 如果两者都变（理论上不会到这里，因为能匹配说明至少一项相同），保险起见标 BothChanged
            if (status == DiffStatus.Unchanged && !NumberIdentity.AreEqual(oldE.Number, newE.Number) && oldE.Name != newE.Name)
                status = DiffStatus.BothChanged;
            d.Status = status;
            return d;
        }

        private static DictDiffEntry MakeAdded(DictEntry newE)
        {
            DictDiffEntry d = new DictDiffEntry();
            d.NewEntry = newE;
            d.OldEntry = null;
            d.Status = DiffStatus.Added;
            return d;
        }

        private static DictDiffEntry MakeRemoved(DictEntry oldE)
        {
            DictDiffEntry d = new DictDiffEntry();
            d.NewEntry = null;
            d.OldEntry = oldE;
            d.Status = DiffStatus.Removed;
            return d;
        }

        /// <summary>
        /// 统计差异概要（用于状态栏）。
        /// </summary>
        public static string Summarize(List<DictDiffEntry> diff)
        {
            int added = 0, removed = 0, numChg = 0, nameChg = 0, bothChg = 0;
            foreach (DictDiffEntry d in diff)
            {
                switch (d.Status)
                {
                    case DiffStatus.Added: added++; break;
                    case DiffStatus.Removed: removed++; break;
                    case DiffStatus.NumberChanged: numChg++; break;
                    case DiffStatus.NameChanged: nameChg++; break;
                    case DiffStatus.BothChanged: bothChg++; break;
                }
            }
            return string.Format(Strings.Diff_Summary, added, removed, numChg, nameChg, bothChg);
        }
    }
}
