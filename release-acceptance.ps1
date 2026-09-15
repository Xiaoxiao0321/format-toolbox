param(
    [string]$Version = "1.0.8",
    [switch]$SkipTests,
    [switch]$SkipStress,
    [switch]$SmokeInstall
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$setup = Join-Path $root "artifacts\installer\格式转换工具箱-Setup-$Version-win-x64.exe"
$app = Join-Path $root "artifacts\win-x64\格式转换工具箱.exe"
$worker = Join-Path $root "artifacts\win-x64\FormatToolbox.Worker.exe"
$workerAssembly = Join-Path $root "artifacts\win-x64\FormatToolbox.Worker.dll"
$reportDirectory = Join-Path $root "artifacts\acceptance"
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

if (-not (Test-Path -LiteralPath $setup)) { throw "找不到安装包：$setup" }
if (-not (Test-Path -LiteralPath $app)) { throw "找不到发布程序：$app" }
if (-not (Test-Path -LiteralPath $worker)) { throw "找不到发布版 Worker：$worker" }
if (-not (Test-Path -LiteralPath $workerAssembly)) { throw "发布版 Worker 不完整，缺少：$workerAssembly" }

function Get-ComStatusInView([string]$progId, [Microsoft.Win32.RegistryView]$view) {
    try {
        $classesRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::ClassesRoot, $view)
        $key = $classesRoot.OpenSubKey("$progId\CLSID")
        try { $clsid = if ($null -ne $key) { [string]$key.GetValue($null) } else { "" } } finally { if ($null -ne $key) { $key.Dispose() } }
        if ([string]::IsNullOrWhiteSpace($clsid)) { return $false }
        $localServer = $classesRoot.OpenSubKey("CLSID\$clsid\LocalServer32")
        $inprocServer = $classesRoot.OpenSubKey("CLSID\$clsid\InprocServer32")
        try {
            $localValue = if ($null -ne $localServer) { [string]$localServer.GetValue($null) } else { "" }
            $inprocValue = if ($null -ne $inprocServer) { [string]$inprocServer.GetValue($null) } else { "" }
            return -not ([string]::IsNullOrWhiteSpace($localValue) -and [string]::IsNullOrWhiteSpace($inprocValue))
        }
        finally { if ($null -ne $localServer) { $localServer.Dispose() }; if ($null -ne $inprocServer) { $inprocServer.Dispose() } }
    }
    catch { return $false }
    finally { if ($null -ne $classesRoot) { $classesRoot.Dispose() } }
}

function Get-ComStatus([string]$progId) {
    return (Get-ComStatusInView $progId ([Microsoft.Win32.RegistryView]::Registry64)) -or
           (Get-ComStatusInView $progId ([Microsoft.Win32.RegistryView]::Registry32))
}

$tests = [ordered]@{ status = "Skipped"; exitCode = $null }
if (-not $SkipTests) {
    & $dotnet test (Join-Path $root "FormatToolbox.sln") -c Release --no-restore
    $tests.exitCode = $LASTEXITCODE
    $tests.status = if ($LASTEXITCODE -eq 0) { "Passed" } else { "Failed" }
}

$stress = [ordered]@{ status = "Skipped"; exitCode = $null; report = $null }
if (-not $SkipStress) {
    & $dotnet run --project (Join-Path $root "tests\FormatToolbox.Stress\FormatToolbox.Stress.csproj") -c Release --no-build -- --items 60 --pages 3 --rounds 3
    $stress.exitCode = $LASTEXITCODE
    $stress.status = if ($LASTEXITCODE -eq 0) { "Passed" } else { "Failed" }
    $stress.report = Join-Path $root "artifacts\performance\latest.json"
}

$smoke = [ordered]@{
    status = "Skipped"
    installExitCode = $null
    started = $false
    workerExecutable = $false
    workerManagedAssembly = $false
    pdfRenderer = $false
    ocrEngine = $false
    chineseOcrModel = $false
    englishOcrModel = $false
    uninstallExitCode = $null
    removed = $null
}
if ($SmokeInstall) {
    $installPath = Join-Path $env:LOCALAPPDATA "Programs\FormatToolbox"
    if (Test-Path -LiteralPath $installPath) { throw "检测到已有安装，为避免覆盖而停止：$installPath" }
    $installer = Start-Process -FilePath $setup -ArgumentList "/S" -PassThru -Wait -WindowStyle Hidden
    $smoke.installExitCode = $installer.ExitCode
    $installedApp = Join-Path $installPath "格式转换工具箱.exe"
    if ($installer.ExitCode -eq 0 -and (Test-Path -LiteralPath $installedApp)) {
        $smoke.workerExecutable = Test-Path -LiteralPath (Join-Path $installPath "FormatToolbox.Worker.exe")
        $smoke.workerManagedAssembly = Test-Path -LiteralPath (Join-Path $installPath "FormatToolbox.Worker.dll")
        $smoke.pdfRenderer = (Test-Path -LiteralPath (Join-Path $installPath "pdfium.dll")) -and (Test-Path -LiteralPath (Join-Path $installPath "PDFtoImage.dll"))
        $smoke.ocrEngine = Test-Path -LiteralPath (Join-Path $installPath "Tesseract.dll")
        $smoke.chineseOcrModel = Test-Path -LiteralPath (Join-Path $installPath "tessdata\chi_sim.traineddata")
        $smoke.englishOcrModel = Test-Path -LiteralPath (Join-Path $installPath "tessdata\eng.traineddata")
        $process = Start-Process -FilePath $installedApp -PassThru -WindowStyle Hidden
        Start-Sleep -Seconds 5
        $process.Refresh(); $smoke.started = -not $process.HasExited
        if ($smoke.started) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            }
        }
        $uninstaller = Start-Process -FilePath (Join-Path $installPath "卸载格式转换工具箱.exe") -ArgumentList "/S" -PassThru -Wait -WindowStyle Hidden
        $smoke.uninstallExitCode = $uninstaller.ExitCode
        Start-Sleep -Seconds 2
        $smoke.removed = -not (Test-Path -LiteralPath $installPath)
    }
    $componentsPresent = $smoke.workerExecutable -and $smoke.workerManagedAssembly -and $smoke.pdfRenderer -and $smoke.ocrEngine -and $smoke.chineseOcrModel -and $smoke.englishOcrModel
    $smoke.status = if ($smoke.installExitCode -eq 0 -and $smoke.started -and $componentsPresent -and $smoke.uninstallExitCode -eq 0 -and $smoke.removed) { "Passed" } else { "Failed" }
}

$sourceFiles = Get-ChildItem (Join-Path $root "src") -Recurse -Filter "*.cs"
$networkReferences = $sourceFiles | Select-String -Pattern "HttpClient|WebRequest|TcpClient|UdpClient|System.Net.Sockets" | Select-Object -ExpandProperty Path -Unique
$externalLinks = $sourceFiles | Select-String -Pattern 'https?://[^"\s]+' -AllMatches | ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
$signature = Get-AuthenticodeSignature -FilePath $setup
$engines = [ordered]@{
    word = Get-ComStatus "Word.Application"
    excel = Get-ComStatus "Excel.Application"
    powerpoint = Get-ComStatus "PowerPoint.Application"
    wpsWriter = Get-ComStatus "kwps.application"
    wpsSpreadsheet = Get-ComStatus "ket.application"
    wpsPresentation = Get-ComStatus "kwpp.application"
    autocad = Get-ComStatus "AutoCAD.Application"
}
$automatedPassed = ($tests.status -in @("Passed", "Skipped")) -and ($stress.status -in @("Passed", "Skipped")) -and ($smoke.status -in @("Passed", "Skipped")) -and (@($networkReferences).Count -eq 0)
$report = [ordered]@{
    generatedAt = [DateTimeOffset]::Now.ToString("O")
    version = $Version
    automatedAcceptance = if ($automatedPassed) { "Passed" } else { "Failed" }
    installer = [ordered]@{
        path = $setup
        bytes = (Get-Item -LiteralPath $setup).Length
        sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
        signatureStatus = $signature.Status.ToString()
    }
    application = [ordered]@{
        fileVersion = (Get-Item -LiteralPath $app).VersionInfo.FileVersion
        workerExecutable = Test-Path -LiteralPath $worker
        workerManagedAssembly = Test-Path -LiteralPath $workerAssembly
        thirdPartyNotices = Test-Path (Join-Path $root "artifacts\win-x64\THIRD-PARTY-NOTICES.txt")
        chineseOcrModel = Test-Path (Join-Path $root "artifacts\win-x64\tessdata\chi_sim.traineddata")
        englishOcrModel = Test-Path (Join-Path $root "artifacts\win-x64\tessdata\eng.traineddata")
        sourceNetworkApiReferences = @($networkReferences)
        userInitiatedExternalLinks = @($externalLinks)
    }
    tests = $tests
    stress = $stress
    installationSmoke = $smoke
    conditionalEngines = $engines
    manualAcceptanceRequired = @(
        "Office 和 AutoCAD 视觉保真需在安装对应桌面软件的机器上使用真实基准文件验证",
        "正式公开分发前配置 Authenticode 代码签名证书",
        "在至少一台干净的 Windows 10 x64 和 Windows 11 x64 虚拟机验证安装与卸载"
    )
}

$jsonPath = Join-Path $reportDirectory "release-acceptance-$Version.json"
$markdownPath = Join-Path $reportDirectory "release-acceptance-$Version.md"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8
$markdown = @(
    "# 格式转换工具箱 $Version 发布验收",
    "",
    "- 自动化验收：$($report.automatedAcceptance)",
    "- 安装包 SHA-256：``$($report.installer.sha256)``",
    "- 数字签名：$($report.installer.signatureStatus)",
    "- 单元/集成测试：$($tests.status)",
    "- 压力测试：$($stress.status)",
    "- 安装冒烟：$($smoke.status)",
    "- 源码网络 API 引用：$(@($networkReferences).Count)",
    "- 用户主动打开的外部链接：$(@($externalLinks).Count)（需用户确认，不自动上传数据）",
    "",
    "## 条件引擎",
    "",
    "- Word：$($engines.word)",
    "- Excel：$($engines.excel)",
    "- PowerPoint：$($engines.powerpoint)",
    "- WPS 文字：$($engines.wpsWriter)",
    "- WPS 表格：$($engines.wpsSpreadsheet)",
    "- WPS 演示：$($engines.wpsPresentation)",
    "- AutoCAD：$($engines.autocad)",
    "",
    "## 尚需人工验收",
    "",
    ($report.manualAcceptanceRequired | ForEach-Object { "- $_" })
)
$markdown | Set-Content -LiteralPath $markdownPath -Encoding utf8
Write-Host "验收报告：$markdownPath"
if (-not $automatedPassed) { exit 1 }
