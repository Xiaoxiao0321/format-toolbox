param(
    [ValidateSet("MicrosoftOffice", "WPS")][string]$Suite = "MicrosoftOffice",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$defaultFolder = if ($Suite -eq "WPS") { "artifacts\acceptance\wps" } else { "artifacts\acceptance\office" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root $defaultFolder }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$worker = Join-Path $root "artifacts\win-x64\FormatToolbox.Worker.exe"
if (-not (Test-Path -LiteralPath $worker)) { throw "找不到发布版 Worker：$worker" }

function Release-ComObject($value) {
    if ($null -ne $value -and [Runtime.InteropServices.Marshal]::IsComObject($value)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($value) }
}

function Wait-ProcessExit([string]$processName, [int]$timeoutSeconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    while ($null -ne (Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw "等待 WPS 后台进程退出超时：$processName.exe" }
        Start-Sleep -Milliseconds 250
    }
}

function New-WordSample([string]$path, [string]$progId) {
    $app = $null; $document = $null; $table = $null; $range = $null; $header = $null
    try {
        $app = New-Object -ComObject $progId
        $app.Visible = $false; $app.DisplayAlerts = 0; $app.AutomationSecurity = 3
        $document = $app.Documents.Add()
        $range = $document.Range(0, 0)
        $range.Text = "格式转换工具箱 Office 验收`r`nFormatToolbox Office Acceptance`r`n`r`n中文字体、页眉页脚、表格与分页测试。"
        $range.Font.NameFarEast = "微软雅黑"; $range.Font.Name = "Arial"; $range.Font.Size = 16
        $range.Collapse(0)
        $table = $document.Tables.Add($range, 4, 3)
        $table.Cell(1,1).Range.Text = "项目"; $table.Cell(1,2).Range.Text = "数值"; $table.Cell(1,3).Range.Text = "备注"
        for ($row = 2; $row -le 4; $row++) { $table.Cell($row,1).Range.Text = "行 $row"; $table.Cell($row,2).Range.Text = "$($row * 100)"; $table.Cell($row,3).Range.Text = "中英混排 ABC" }
        $header = $document.Sections.Item(1).Headers.Item(1).Range; $header.Text = "格式转换工具箱 · Word 基准样本"
        $document.SaveAs2($path, 16)
    } finally {
        if ($null -ne $document) { try { $document.Close(0) } catch { } }
        if ($null -ne $app) { try { $app.Quit(0) } catch { } }
        Release-ComObject $header; Release-ComObject $table; Release-ComObject $range; Release-ComObject $document; Release-ComObject $app
    }
}

function New-ExcelSample([string]$path, [string]$progId) {
    $app = $null; $book = $null; $sheet = $null; $second = $null
    try {
        $app = New-Object -ComObject $progId
        $app.Visible = $false; $app.DisplayAlerts = $false; $app.AutomationSecurity = 3
        $book = $app.Workbooks.Add()
        $sheet = $book.Worksheets.Item(1); $sheet.Name = "打印区域"
        $sheet.Cells.Item(1,1) = "产品"; $sheet.Cells.Item(1,2) = "数量"; $sheet.Cells.Item(1,3) = "单价"; $sheet.Cells.Item(1,4) = "总额"
        for ($row = 2; $row -le 20; $row++) { $sheet.Cells.Item($row,1) = "项目 $row"; $sheet.Cells.Item($row,2) = $row; $sheet.Cells.Item($row,3) = 12.5; $sheet.Cells.Item($row,4).Formula = "=B$row*C$row" }
        $sheet.Range("A1:D20").Columns.AutoFit() | Out-Null
        $sheet.PageSetup.PrintArea = '$A$1:$D$20'; $sheet.PageSetup.Orientation = 2; $sheet.PageSetup.Zoom = $false; $sheet.PageSetup.FitToPagesWide = 1; $sheet.PageSetup.FitToPagesTall = 1
        $second = $book.Worksheets.Add(); $second.Name = "无打印区域"; $second.Cells.Item(1,1) = "该工作表验证自动适页设置"; $second.Cells.Item(2,1) = "中文 English 123"
        $book.SaveAs($path, 51)
    } finally {
        if ($null -ne $book) { try { $book.Close($false) } catch { } }
        if ($null -ne $app) { try { $app.Quit() } catch { } }
        Release-ComObject $second; Release-ComObject $sheet; Release-ComObject $book; Release-ComObject $app
    }
}

function New-PowerPointSample([string]$path, [string]$progId) {
    $app = $null; $presentation = $null; $slide1 = $null; $slide2 = $null; $shape = $null
    try {
        $app = New-Object -ComObject $progId
        $presentation = $app.Presentations.Add()
        $slide1 = $presentation.Slides.Add(1, 1)
        $slide1.Shapes.Title.TextFrame.TextRange.Text = "格式转换工具箱"
        $slide1.Shapes.Item(2).TextFrame.TextRange.Text = "PowerPoint 主题字体与中英混排验收"
        $slide2 = $presentation.Slides.Add(2, 12)
        $shape = $slide2.Shapes.AddShape(1, 80, 80, 500, 220)
        $shape.Fill.ForeColor.RGB = 16744448; $shape.Fill.Transparency = 0.35
        $shape.TextFrame.TextRange.Text = "透明图层`r`nTransparency 35%"
        $presentation.SaveAs($path, 24)
    } finally {
        if ($null -ne $presentation) { try { $presentation.Close() } catch { } }
        if ($null -ne $app) { try { $app.Quit() } catch { } }
        Release-ComObject $shape; Release-ComObject $slide2; Release-ComObject $slide1; Release-ComObject $presentation; Release-ComObject $app
    }
}

$cases = if ($Suite -eq "WPS") { @(
    [ordered]@{ name = "WPS 文字"; progId = "kwps.application"; engine = "wps.writer"; input = Join-Path $OutputDirectory "wps-writer-baseline.docx"; output = Join-Path $OutputDirectory "wps-writer-baseline.pdf" },
    [ordered]@{ name = "WPS 表格"; progId = "ket.application"; engine = "wps.spreadsheet"; input = Join-Path $OutputDirectory "wps-spreadsheet-baseline.xlsx"; output = Join-Path $OutputDirectory "wps-spreadsheet-baseline.pdf" },
    [ordered]@{ name = "WPS 演示"; progId = "kwpp.application"; engine = "wps.presentation"; input = Join-Path $OutputDirectory "wps-presentation-baseline.pptx"; output = Join-Path $OutputDirectory "wps-presentation-baseline.pdf" }
) } else { @(
    [ordered]@{ name = "Word"; progId = "Word.Application"; engine = "msoffice.word"; input = Join-Path $OutputDirectory "word-baseline.docx"; output = Join-Path $OutputDirectory "word-baseline.pdf" },
    [ordered]@{ name = "Excel"; progId = "Excel.Application"; engine = "msoffice.excel"; input = Join-Path $OutputDirectory "excel-baseline.xlsx"; output = Join-Path $OutputDirectory "excel-baseline.pdf" },
    [ordered]@{ name = "PowerPoint"; progId = "PowerPoint.Application"; engine = "msoffice.powerpoint"; input = Join-Path $OutputDirectory "powerpoint-baseline.pptx"; output = Join-Path $OutputDirectory "powerpoint-baseline.pdf" }
) }

foreach ($case in $cases) {
    if ($null -eq [Type]::GetTypeFromProgID($case.progId, $false)) { throw "未检测到 $($case.name) COM 组件：$($case.progId)" }
}
New-WordSample $cases[0].input $cases[0].progId
New-ExcelSample $cases[1].input $cases[1].progId
if ($Suite -eq "WPS") { Wait-ProcessExit "et" }
New-PowerPointSample $cases[2].input $cases[2].progId
if ($Suite -eq "WPS") { Wait-ProcessExit "wpp" }

foreach ($case in $cases) {
    if (Test-Path -LiteralPath $case.output) { Remove-Item -LiteralPath $case.output -Force }
    $responseText = & $worker --engine $case.engine --input $case.input --output $case.output
    $exitCode = $LASTEXITCODE
    $response = $responseText | ConvertFrom-Json
    $validPdf = $false
    if (Test-Path -LiteralPath $case.output) {
        $stream = [IO.File]::OpenRead($case.output)
        try { $header = New-Object byte[] 4; [void]$stream.Read($header, 0, 4); $validPdf = [Text.Encoding]::ASCII.GetString($header) -eq "%PDF" -and $stream.Length -gt 1024 } finally { $stream.Dispose() }
    }
    $case.result = [ordered]@{ exitCode = $exitCode; success = [bool]$response.Success; engine = $response.Engine; validPdf = $validPdf; bytes = if ($validPdf) { (Get-Item $case.output).Length } else { 0 }; message = $response.Message }
}

$report = [ordered]@{ generatedAt = [DateTimeOffset]::Now.ToString("O"); suite = $Suite; cases = $cases; allPassed = -not ($cases.result | Where-Object { -not $_.success -or -not $_.validPdf -or $_.exitCode -ne 0 }) }
$reportName = if ($Suite -eq "WPS") { "wps-acceptance.json" } else { "office-acceptance.json" }
$reportPath = Join-Path $OutputDirectory $reportName
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
$report | ConvertTo-Json -Depth 8
if (-not $report.allPassed) { exit 1 }
