# 格式转换工具箱

## 1.0.5 OCR 保留彩色页面

- PDF 转可搜索 PDF 默认保留原页面内容、颜色、尺寸、裁剪范围和旋转，只叠加不可见的 OCR 文字层。
- OCR 设置新增“灰度输出”选项，默认不勾选；主动勾选时将页面转换为灰度图片并增加文字层，图片输入同样支持该选项。
- 修复 PDF OCR 强制灰度渲染的问题；对渲染图片明确设置识别 DPI，避免灰度输出的页面尺寸因缺少图片 DPI 信息而改变。
- 回归验证覆盖彩色 PDF、旋转和裁剪页面、页码范围及图片输入，并检查页面外观和文字搜索。

## 1.0.4 小屏幕适配

- 主窗口启动时按当前屏幕工作区及 DPI 缩放调整尺寸并居中，避开任务栏。
- 主内容支持滚动，输入列表和任务/历史区域保持独立高度，缩小窗口不会将结果区域挤没。
- 小屏幕启动时折叠快速开始区，设置项和操作按钮可换行；开始转换后自动定位到任务队列。
- “打开输出目录”入口固定在窗口右下角，滚动内容时仍可使用。

## 1.0.3 补丁

- 修复已安装 WPS 却无法检测文字转换组件的问题：同时检查 32 位与 64 位注册表视图、版本化组件名称，并统一检测和 Worker 调用的组件标识。
- 组件不可用时保留具体检测原因，提示通过安装程序修复组件注册；组件未注册不再误报为文档损坏或加密。
- 用户反馈 WPS Office 12.1.0.28599 的 DOCX 转 PDF 测试成功；其他 WPS 版本仍需对应环境验证。

## 发布安装包

项目使用 NSIS 3.12 生成免管理员权限的 64 位安装包。将 NSIS 安装到 `.tools\nsis` 后运行：

```powershell
.\build-release.ps1
```

脚本会依次执行测试、自包含发布、可选数字签名和安装包编译。若设置 `SIGN_CERT_PATH`（及可选的 `SIGN_CERT_PASSWORD`），会使用系统中的 `signtool.exe` 对主程序和安装包签名。卸载时会询问是否保留 `%LOCALAPPDATA%\FormatToolbox` 下的转换历史与诊断日志。

性能与长期稳定性测试见 `tests\FormatToolbox.Stress`。默认执行 180 次 PDF 转换并生成 `artifacts\performance\latest.json` 报告。

发布前运行 `.\release-acceptance.ps1 -SmokeInstall`，会以无还原模式执行测试和压力测试，检查离线依赖、引擎、签名与安装包哈希，并在 `artifacts\acceptance` 生成机器可读及 Markdown 验收报告。

Windows 10/11 x64 本地离线格式转换桌面应用。界面使用 WPF，转换核心与 UI 解耦；Microsoft Office、WPS Office 和 AutoCAD 转换运行于独立 Worker 进程。

## 当前能力

- DOC/DOCX/RTF、XLS/XLSX/CSV、PPT/PPTX 优先通过本机 Microsoft Office 转 PDF；对应组件不可用时自动回退到 WPS 文字、表格或演示。
- DWG 通过本机 AutoCAD 将全部可打印布局按 TabOrder 合并为单个多页 PDF，并报告跳过或打印失败的布局。
- PNG/JPEG/BMP/TIFF/WebP 读取，以及 PNG/JPEG/BMP/TIFF 输出。
- PDF 合并、页面范围提取、旋转、文字水印，以及图片生成 PDF（PDFsharp，MIT）。
- PDF 页面离线渲染为 PNG/JPEG（PDFium/PDFtoImage，MIT）。
- 简体中文与英文离线 OCR，并输出保留页面图像的可搜索 PDF（Tesseract，Apache-2.0）。
- 动态转换设置面板：图片质量/渲染 DPI、PDF 页码/旋转/压缩/水印、OCR 语言/页码/DPI。
- “合并为单个 PDF”支持按输入列表顺序合并所选 PDF 与图片。
- 统一输出预检可识别磁盘空间不足、只读/无权限目录和输出文件占用，并返回稳定错误代码。
- PDF 强力压缩可按 DPI 与 JPEG 质量降采样重建扫描型页面；该模式会栅格化页面，不保留原有文字选择、链接或表单。
- PDF 页面工具提供缩略图、拖拽排序、勾选排除、逐页/指定范围/每 N 页拆分和自定义输出名称。
- 拖放文件或文件夹、批量队列、取消、失败重试、自动避让重名文件。
- 转换使用同目录临时文件，成功后移动为最终文件；历史只保存路径和状态。
- “帮助 → 意见反馈”与快速开始区的反馈按钮提供内置微信客服二维码、外部反馈网页、环境信息预览/复制和本地日志目录入口。

## 问题反馈与客服

- 在线反馈：[格式转换工具箱反馈页面](https://format-toolbox-feedback.golden-elm-4905.chatgpt.site/)。
- 软件内入口：打开“帮助 → 意见反馈”，可访问反馈网页、扫描微信客服二维码、复制环境信息或打开本地日志目录。

反馈时请提供软件版本、操作系统、转换类型、复现步骤和错误提示。Office、WPS 或 AutoCAD 转换问题请同时说明相关软件的版本。

“工具 → 复制诊断信息”和反馈窗口中的同名按钮可复制工具箱版本、WPS/Office 已注册组件的可执行文件版本、CLSID、注册表视图、引擎状态及本次运行最近 20 条错误码。未检测到组件或无法读取版本时会明确显示。复制内容不包含文档路径或异常正文；本地日志保留具体错误详情及原始 HRESULT，供进一步排查。任务队列也会直接显示转换错误码。

应用不会自动上传文档、日志或环境信息。如需提供样本或日志，请先检查并移除个人信息和敏感内容。

## 构建

要求：Windows 10/11 x64、.NET 8 SDK。Office 文档转换需要相应 Microsoft Office 或 WPS Office 桌面组件，DWG 转换需要完整版 AutoCAD。

```powershell
dotnet restore FormatToolbox.sln
dotnet build FormatToolbox.sln -c Release
dotnet test FormatToolbox.sln -c Release
dotnet publish src/FormatToolbox.App/FormatToolbox.App.csproj -c Release -r win-x64 --self-contained true
```

Worker 项目会随桌面应用一起构建。发布后运行 `格式转换工具箱.exe`。

## 安全与隐私

文件转换不使用网络，不上传用户文件。意见反馈仅在用户确认后使用系统浏览器打开在线网页，不附加文件、日志、环境信息或查询参数；网页填写内容由网页服务接收。环境信息仅在本机生成，可预览后自行复制；诊断日志可能含路径，需用户检查并手动发送。Office/WPS Worker 会尽可能将宏安全级别设为禁用，并以只读方式打开文档。应用不会尝试绕过密码或文档保护；WPS 软件自身的联网策略由其安装版本与用户配置决定。

## 尚未接入的功能

OCR 首次运行要求系统具备 Microsoft Visual C++ 2019 x64 Runtime。

## 测试语料

`tests/TestCorpus` 包含自动化与条件式验收清单。当前自动化覆盖 PDF 渲染、拆分、排序、合并、压缩、加密/损坏文件、Unicode 长路径，以及中英文 OCR；Office 与 AutoCAD 用例会在相应 COM 引擎安装后执行人工视觉验收。
