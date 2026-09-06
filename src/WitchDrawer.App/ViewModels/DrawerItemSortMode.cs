namespace WitchDrawer.App.ViewModels;

/// <summary>
/// 盒子排序模式。所有收纳盒型（普通/像素/映射/抽屉）统一支持：
/// Free 为自由排序（格位/导入顺序，网格盒可拖拽摆放，布局被记忆）；
/// 其余四种为自动排序，同时作用于盒内网格/封面与抽屉二级弹窗。
/// 持久化按枚举名存储，新增值不影响已有数据。
/// </summary>
public enum DrawerItemSortMode
{
    Free,
    Name,
    Size,
    ItemType,
    ModifiedDate
}
