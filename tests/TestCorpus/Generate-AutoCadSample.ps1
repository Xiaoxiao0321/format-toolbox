param([string]$OutputDirectory = "$PSScriptRoot\..\..\artifacts\acceptance\generated\samples")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$app = $null; $doc = $null
try {
    $app = New-Object -ComObject AutoCAD.Application
    $app.Visible = $false
    $doc = $app.Documents.Add()
    $doc.ModelSpace.AddText('MODEL MUST BE SKIPPED', [double[]]@(0,0,0), 10) | Out-Null
    $defaults = @($doc.Layouts | Where-Object { -not $_.ModelType } | ForEach-Object { $_.Name })
    foreach ($item in @(@('Z-First',1),@('A-Second',2),@('M-Third',3))) {
        $layout = $doc.Layouts.Add($item[0]); $layout.TabOrder = $item[1]
        $doc.ActiveLayout = $layout
        $layout.ConfigName = 'DWG To PDF.pc3'; $layout.RefreshPlotDeviceInfo()
        $layout.PlotType = 1; $layout.UseStandardScale = $true; $layout.StandardScale = 0; $layout.CenterPlot = $true
        $layout.Block.AddText("LAYOUT $($item[1]) $($item[0])", [double[]]@(30,30,0), 8) | Out-Null
    }
    foreach ($name in $defaults) { $doc.Layouts.Item($name).Delete() }
    $doc.Layouts.Item('M-Third').ConfigName = 'None'
    $doc.SaveAs([IO.Path]::GetFullPath((Join-Path $OutputDirectory 'cad-layouts.dwg')))
} finally {
    if ($null -ne $doc) { try { $doc.Close($false) } catch {} }
    if ($null -ne $app) { try { $app.Quit() } catch {} }
    foreach ($value in @($doc,$app)) { if ($null -ne $value -and [Runtime.InteropServices.Marshal]::IsComObject($value)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($value) } }
}
