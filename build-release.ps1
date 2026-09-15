param([string]$Version = "1.0.9", [string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$dotnet = $null
$dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
$dotnetCandidates = @(
    $(if ($null -ne $dotnetCommand) { $dotnetCommand.Source })
    $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles "dotnet\dotnet.exe" })
    $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" })
)
foreach ($candidate in $dotnetCandidates) {
    if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)) { continue }
    $installedSdks = @(& $candidate --list-sdks)
    if ($LASTEXITCODE -eq 0 -and $installedSdks.Count -gt 0) {
        $dotnet = $candidate
        break
    }
}
$appProject = Join-Path $projectRoot "src\FormatToolbox.App\FormatToolbox.App.csproj"
$workerProject = Join-Path $projectRoot "src\FormatToolbox.Worker\FormatToolbox.Worker.csproj"
$publishDir = Join-Path $projectRoot "artifacts\$Runtime"
$installerDir = Join-Path $projectRoot "artifacts\installer"
$makensis = Join-Path $projectRoot ".tools\nsis\makensis.exe"
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $projectRoot ".nuget\packages"

if ([string]::IsNullOrWhiteSpace($dotnet)) { throw "找不到 .NET SDK。请安装 .NET 8 SDK 后重新打开 PowerShell，再运行此脚本。" }
if (-not (Test-Path -LiteralPath $makensis)) { throw "找不到 NSIS：$makensis。请先安装到 .tools\nsis。" }
New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null

& $dotnet test (Join-Path $projectRoot "FormatToolbox.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "测试失败。" }
& $dotnet publish $appProject -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "发布失败。" }
& $dotnet publish $workerProject -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Worker 发布失败。" }

function Invoke-OptionalSigning([string]$path) {
    if ([string]::IsNullOrWhiteSpace($env:SIGN_CERT_PATH)) { return }
    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -eq $signTool) { throw "已设置 SIGN_CERT_PATH，但找不到 signtool.exe。" }
    $arguments = @("sign", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256", "/f", $env:SIGN_CERT_PATH)
    if (-not [string]::IsNullOrWhiteSpace($env:SIGN_CERT_PASSWORD)) { $arguments += @("/p", $env:SIGN_CERT_PASSWORD) }
    $arguments += $path
    & $signTool.Source @arguments
    if ($LASTEXITCODE -ne 0) { throw "数字签名失败：$path" }
}

Invoke-OptionalSigning (Join-Path $publishDir "格式转换工具箱.exe")
Invoke-OptionalSigning (Join-Path $publishDir "FormatToolbox.Worker.exe")
$setup = Join-Path $installerDir "格式转换工具箱-Setup-$Version-win-x64.exe"
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }
Push-Location (Join-Path $projectRoot "installer")
try { & $makensis "/INPUTCHARSET" "UTF8" "/DAPP_VERSION=$Version" "FormatToolbox.nsi" } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "安装包生成失败。" }
Invoke-OptionalSigning $setup
Write-Host "发布完成：$setup"
