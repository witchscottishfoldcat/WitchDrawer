# 选中蓝框轻微漂移调查报告

日期：2026-08-22

## 更正后的结论

这次漂移不是自适应网格失效，也不是盒子窗口在 Win+D 修复中发生了物理位移。经用户提供正常版本截图与历史会话记录复核，正确实现是独立的选中描边层，并包含固定的 `-0.30 DIP` 半像素补偿：

```xml
<TranslateTransform X="-0.30" Y="-0.30" />
```

该层与内容根边框位于同一个外层 `Grid`，只负责绘制选中轮廓，不参与图标内容测量；`-0.30 DIP` 用于补偿当前透明窗口、圆角和 DPI 布局组合下的半像素落点。后续透明度提交 `b522e9c` 将这一层合并回 `Root`，从而重新暴露视觉偏移。

目前已从 `b522e9c` 的父版本完整恢复该实现；Win+D 和右键菜单修复保持独立。

## 证据

### 1. 正常版本的模板包含独立选中层

透明度提交 `b522e9c` 的父版本中：

- 外层 `Grid` 承担项目 Margin；
- `Root` 负责背景、内容和 Padding；
- `SelectionOutline` 只负责蓝色选中描边；
- `SelectionOutline` 设置 `SnapsToDevicePixels="False"`；
- 描边使用 `TranslateTransform X/Y="-0.30"`；
- 抽屉项使用同构的 `DrawerSelectionOutline`。

### 2. 后续透明度修改删除了补偿

`b522e9c` 删除独立描边层，把选中边框重新合并到 `Root`，同时删除了 `-0.30 DIP` 补偿。之后即使自适应网格和图标 DPI 请求仍正常，蓝框也会出现轻微视觉偏移。

### 3. Win+D 点击链没有移动整个盒子

点击盒子时，窗口会暂时解除 Progman Owner，鼠标释放后再恢复 Owner。这是为了避免盒子成为 Progman 的 LastActivePopup。

对运行中的盒子做了几何探针，记录结果如下：

| 阶段 | HWND 矩形 |
|---|---|
| 鼠标激活前 | `1322,10,1688,191` |
| 鼠标激活期间 | `1322,10,1688,191` |
| 鼠标释放恢复后 | `1322,10,1688,191` |

Owner 的确发生了临时切换，但窗口矩形没有变化。因此这条链不是当前蓝框相对图标漂移的直接原因。

### 4. 自适应布局代码未被删除

`IconFrameSize`、`IconSize`、`ItemSlotWidth`、`ItemSlotHeight`、DPI 更新和虚拟化网格逻辑仍然存在。当前修复没有改动这些值，也没有改动 `GridViewportWidth/Height` 的计算。

## 修复内容

已恢复：

- 普通图标项的独立 `SelectionOutline`；
- 抽屉按钮的独立 `DrawerSelectionOutline`；
- `1.2 DIP` 描边和 `SnapsToDevicePixels="False"`；
- 固定 `-0.30 DIP` 半像素补偿；
- 拖拽时同步隐藏独立描边。

保留：

- Progman Owner，确保 Win+D 时盒子不隐藏；
- 鼠标激活前临时解除 Owner，避免点击盒子后 Win+D 失效；
- 显示桌面期间不执行会改写 Owner 链 Z 序的沉底操作；
- Progman LastActivePopup 修复。

## 验证计划

模板测试固定检查两个独立描边层、`1.2`、`SnapsToDevicePixels=False` 与 `-0.30`，防止后续主题、透明度或菜单修改再次删除这组布局关系。
