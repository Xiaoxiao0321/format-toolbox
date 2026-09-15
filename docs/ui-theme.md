# 清透莫兰迪青蓝主题

依据任务「界面优化整体规划」的最终定稿实施，分支为 `codex/界面优化`。

## 实施节奏与结果

1. 建立 `src/FormatToolbox.App/Themes/Light.xaml`，集中管理颜色、字体、圆角、间距与控件状态，应用到主窗口。
2. 同步 PDF 页面工具、确认、反馈、关于窗口，以及线条功能图标和软件标识。各窗口可以独立加载共享资源，兼容现有 STA 测试。
3. 构建、回归、逐窗渲染检查，并修整尺寸：输入文件区从 180 增至 218 DIP，合并按钮从 135 增至 165 DIP，确认弹窗最小高度从 180 增至 260 DIP。

主题实施时保留了当时的布局结构、功能、事件、数据绑定、系统窗口边框和原生文件选择框。界面实施阶段版本号为 1.0.6；后续按用户要求更新至 1.0.7 并制作安装包，发布验收记录见 `artifacts/acceptance/release-acceptance-1.0.7.json`。

## 主窗口重布局

在 `codex/重整布局` 分支实施任务「界面优化整体规划」最新的双栏计划。宽窗口顶部单行保留三个常用入口，其余四项放在“更多功能”菜单；文件列表和常驻操作条占左侧，约 320 DIP 的设置栏在右侧，任务队列和历史记录横跨底部。工作区和结果区的分隔线可调高度，文件区和设置区的分隔线可调宽度，设置栏限制为 270–420 DIP，文件区至少 520 DIP。

当可用宽度低于约 980 DIP 时，同一组控件改为“入口、设置、文件、结果”纵向排列。设置栏和整页各自滚动，窗口过矮时可以看到下方文件及结果。目标格式和输出目录始终可见，常用参数直接显示，高级参数默认折叠；切换入口或目标格式时折叠高级参数并保留已填写值。输入路径列随列表宽度变化，长路径省略显示且提供完整路径提示。

后续根据使用反馈修正了分隔线与列表：拖动手柄直接更新设置宽度及工作区／结果区比例，小幅拖动即时生效，超出范围时停在边界；三个列表使用各自的纵向与横向滚动条。历史记录的四个操作按钮移到列表上方，不再受固定列宽挤压。“更多功能”菜单明确加载共享菜单样式，统一 14 DIP 字体、青蓝悬浮色与 8 DIP 圆角；下拉选项字体和标签选中字体的继承也已修正。

## 样式

窗口背景 `#E7F3F7`，卡片与输入框 `#F9FDFE`，正文 `#304B58`，辅助文字 `#506D7B`。

主按钮使用 `#315D70` / `#C0E2EE`；次级按钮使用 `#456B7C` / `#DEEFF5`；取消和关闭使用浅色描边按钮。悬浮、按下、禁用、键盘焦点各有明确状态；列表选中使用浅青蓝背景和深色正文。

微软雅黑 UI 优先，回退微软雅黑、Segoe UI。正文 14 DIP，分区标题 15 DIP，弹窗标题 22 DIP。常规控件最小高度 34 DIP；双行快速入口最小高度 62 DIP。控件、卡片、提示条圆角分别为 6、10、8 DIP。

`ThemeIcon` 通过附加属性绑定矢量 Geometry 和画刷，避免小图标缩放模糊。深色按钮内图标跟随文字颜色。

## 验证

- Release 构建通过。
- 完整测试 73 项全部通过，包含原有 71 项及 2 项主题检查。
- 主题检查验证正文、辅助文字、警告、错误和按钮三种可读状态的文字对比度至少 4.5:1；最低值为次级按钮悬浮状态约 4.53:1。
- 五个窗口各生成 100%、125%、150% 像素密度预览，共 15 张 PNG；并检查最小窗口尺寸下按钮高度。已人工查看代表性预览。
- 实际打开主窗口，检查初始显示、滚动、功能图标、菜单焦点与菜单/下拉展开效果。
- 真实窗口自动化曾出现菜单元素缓存失效，错误为 `element 117 is not available in cached app state for 格式转换工具箱.exe`。完整的下拉键盘选择、列表多选、弹窗回车/Esc和各窗口逐项交互验收未据此声明通过。
- 预览通过 WPF RenderTargetBitmap 在 96、120、144 DPI 渲染，属于像素密度检查；没有切换 Windows 系统缩放，不能替代三种系统缩放下的完整实机验收。
- NuGet 漏洞审计提示 NU1900（无法访问 nuget.org），本次构建和测试使用已有依赖缓存。此项未被视为漏洞审计通过。
- 安装、卸载图标配置均引用更新后的 ICO；1.0.7 安装包已重新构建，约 75.8 MiB。发布构建的 73 项测试通过；180 次 PDF 压力转换无失败和残留临时文件；安装、启动、离线依赖检查与卸载冒烟全部通过，测试安装目录已移除。验收阶段未重复执行构建阶段已通过的测试。

生成预览：

```powershell
$env:FORMATTOOLBOX_THEME_PREVIEWS = 'E:\codex项目\artifacts\ui-theme'
dotnet test FormatToolbox.sln -c Release --no-restore
```

预览位置：`artifacts/ui-theme/<窗口名称>-100.png`，另有 `-125.png`、`-150.png`。

## 软件标识生成记录

使用内置 imagegen 工具编辑现有 PNG，并用已有 IconBuilder 导出 16、24、32、48、64、128、256 像素 ICO。最终资源：

- `src/FormatToolbox.App/app-icon.png`
- `src/FormatToolbox.App/app-icon.ico`

最终提示词：

> Use case: precise-object-edit. Asset type: Windows desktop application icon PNG on genuinely transparent background. Input image 1 is the edit target. Recolor only the existing two overlapping document sheets and two curved conversion arrows to a luminous soft Morandi cyan-blue palette matching the application theme. Preserve the exact recognizable document silhouettes, folds, arrow shapes, layout, margins and sharp clean edges. Dark outlines #315D70, secondary shades #456B7C and #51869C, pale cyan arrows #C0E2EE with restrained gradients toward #51869C, near-white paper #F9FDFE and fold #CFE9F2. Keep strong edge contrast at 16px to 256px on light and dark backgrounds. Remove saturated electric blue/cyan appearance. No new objects, no text, no watermark, no background or drop shadow outside the silhouette. Square transparent PNG with real alpha.
