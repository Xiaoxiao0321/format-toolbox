# 真实文件验收语料

自动化测试会在临时目录生成 PDF、图片、加密/损坏文件和 Unicode 长路径样本，避免在仓库中保存大型二进制文件。

## 当前机器可自动执行

- 多页 PDF：页面数量、排序、删除、拆分、合并。
- PDFium：页面渲染成功且图片尺寸有效。
- OCR：英文和简体中文模型加载，并生成带文本层的 PDF。
- 异常文件：加密 PDF、截断 PDF、文件占用、Unicode 长路径。
- 压缩：栅格化降采样后生成有效 PDF。

## Office 基准文件（安装桌面 Office 后执行）

- Word：中英文字体、页眉页脚、目录、表格、图片、分页、批注。
- Excel：多工作表、打印区域、横向页面、公式结果、冻结窗格。
- PowerPoint：透明 PNG、主题字体、文本溢出、隐藏页、动画静态结果。

验收：Worker 返回成功；输出 PDF 可打开、页数符合预期；基准渲染图人工确认无明显错位。

安装完整组件并完成首次启动后，可执行：

```powershell
.\tests\TestCorpus\Run-OfficeAcceptance.ps1 -Suite MicrosoftOffice
.\tests\TestCorpus\Run-OfficeAcceptance.ps1 -Suite WPS
```

WPS 验收使用 `kwps.application`、`ket.application` 和 `kwpp.application`，结果中的 Engine 必须明确标识 WPS，不能伪装成 Microsoft Office。

在安装 Word、Excel、PowerPoint 的电脑上运行 `Run-OfficeAcceptance.ps1`，可自动生成包含上述关键特征的基准文件并调用发布版 Worker。输出与报告位于 `artifacts\acceptance\office`；最终视觉效果仍需人工打开三个 PDF 确认。

## AutoCAD 基准文件（安装完整版 AutoCAD 后执行）

- Model 加三个布局，不同 TabOrder。
- 一个无打印设备布局、一个冻结图层布局、一个带外部参照布局。
- 缺失 SHX/TTF 字体和外部参照的副本。

验收：跳过 Model，成功布局按 TabOrder 合并；失败布局进入 warnings；临时 PDF 全部清理。
