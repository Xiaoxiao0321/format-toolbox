# 真实文件验收语料

## 自行生成的代表性样本验收

没有业务样本时，执行 `Run-SampleAcceptance.ps1` 生成并转换实体文件。生成样本不是实际业务样本，验收结论与 OCR 准确率仅适用于这批基准。

```powershell
.\tests\TestCorpus\Run-SampleAcceptance.ps1
# 样本已生成后复跑
.\tests\TestCorpus\Run-SampleAcceptance.ps1 -ReuseSamples
```

需要 .NET 8、带 Pillow/numpy/reportlab/pypdf/pdfplumber 的 Python，以及 `@oai/artifact-tool` Node 环境；脚本支持传入运行时路径。COM 验收需在实际用户环境运行，沙箱可能隐藏组件注册。报告生成在 `artifacts/acceptance/generated/report.md` 与 `report.json`，保留输出文件、抽取文字、15 张窗口快照和样本 SHA256。

- 三页 TIFF：原始、LZW、Deflate 压缩，不同尺寸与横竖页面；图片转换、PDF、混合合并及 OCR 均检查完整页数和顺序。
- WebP：透明、部分透明、有损静态图；动态与损坏文件必须明确报错。
- OCR：英文、简中、混排、轻度倾斜 / 模糊 / 噪声、明显拉伸变形；以导出 PDF 的文字独立计算 CER，清晰样本阈值 10%，降质及变形挑战样本阈值 25%。
- 大型扫描 PDF：40 个独立全页 JPEG，约 72 MiB；全部导图、全部 OCR、指定页 OCR、UI 调度间隔、进程峰值工作集、页间取消与高 DPI 渲染中取消。
- 长表格：1200 行、8 列，真实 Office/WPS 调用，独立核对每个行 ID 与最小字体；至少 20 页、全部记录存在、字体至少 7 pt。
- 自有弹窗：关于、反馈、PDF 整理、确认操作，模拟 800×600、1024×600@150%、1366×768@200% 减去任务栏后的工作区，检查按钮边界与禁用横向滚动区域的溢出。

AutoCAD 未安装时显示 Blocked，不能计为布局验收通过。在安装完整版 AutoCAD 的机器先执行 `Generate-AutoCadSample.ps1`：生成 Model 与三个名字顺序和 TabOrder 不同的纸空间布局，其中一个使用缺省打印设备；随后复跑验收。此生成脚本未在没有 AutoCAD 的本机执行，字体 / 外部参照 / 损坏打印配置仍需在 CAD 环境补充。

Windows 文件及文件夹选择器属于系统弹窗，需要真实桌面检查；模拟 WPF 测量不覆盖系统对话框。

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
