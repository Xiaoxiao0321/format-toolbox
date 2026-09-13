param(
    [string]$OutputDirectory = '',
    [string]$Dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    [string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe",
    [string]$Node = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe",
    [string]$NodeModules = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules",
    [switch]$ReuseSamples
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\acceptance\generated' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
Push-Location $root
try {
    if (-not $ReuseSamples) {
        & $Python (Join-Path $PSScriptRoot 'Generate-AcceptanceSamples.py') --output (Join-Path $OutputDirectory 'samples')
        if ($LASTEXITCODE) { throw '扫描 / 图片样本生成失败' }
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'node_modules'))) { New-Item -ItemType Junction -Path (Join-Path $PSScriptRoot 'node_modules') -Target $NodeModules | Out-Null }
        & $Node (Join-Path $PSScriptRoot 'Generate-LongTable.mjs') (Join-Path $OutputDirectory 'samples')
        if ($LASTEXITCODE) { throw '表格样本生成失败' }
    }
    & $Dotnet restore tests/FormatToolbox.Acceptance/FormatToolbox.Acceptance.csproj --configfile NuGet.Config
    if ($LASTEXITCODE) { throw '验收项目还原失败' }
    & $Dotnet build tests/FormatToolbox.Acceptance/FormatToolbox.Acceptance.csproj -c Release --no-restore
    if ($LASTEXITCODE) { throw '验收项目编译失败' }
    & (Join-Path $root 'tests\FormatToolbox.Acceptance\bin\Release\net8.0-windows\FormatToolbox.Acceptance.exe') $OutputDirectory
    $conversionExit = $LASTEXITCODE
    & $Python (Join-Path $PSScriptRoot 'Report-Acceptance.py') --directory $OutputDirectory
    if ($LASTEXITCODE -or $conversionExit) { throw "验收失败，详见 $OutputDirectory\report.md" }
    Write-Host "验收报告：$OutputDirectory\report.md；环境受限项目单独列出，不计为通过。"
} finally { Pop-Location }
