param([string]$Out = "$PSScriptRoot\DinoVisualizer.exe")
# Builds DinoVisualizer.exe with the C# compiler included in .NET Framework 4.x (no installs needed).
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path "$fw\csc.exe")) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }

$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Security.dll'

if (-not (Test-Path "$PSScriptRoot\icon.ico")) { & "$PSScriptRoot\make-icon.ps1" }

# NASA's maps of the Earth for the Earth visualizer, built into the exe (without them it generates a planet instead).
$maps = @{ 'land_shallow_topo_2048.jpg' = 'EarthLand.jpg'; 'cloud_combined_2048.jpg' = 'EarthClouds.jpg'; 'BlackMarble_2016_01deg_gray.jpg' = 'EarthLights.jpg' }
$resources = foreach ($file in $maps.Keys) {
    $path = "$PSScriptRoot\earth\$file"
    if (Test-Path $path) { "/resource:$path,$($maps[$file])" } else { Write-Host "Missing earth\$file (the Earth visualizer will generate its maps)" }
}

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:"$Out" `
    /win32icon:"$PSScriptRoot\icon.ico" ($refs | ForEach-Object { "/r:$_" }) $resources "$PSScriptRoot\src\DinoVisualizer.cs"
if ($LASTEXITCODE -eq 0) { Write-Host "Built $Out" }
