param(
    [Parameter(Mandatory = $true)][string]$Version,   # the new version, like 1.0.1
    [string]$Notes = ""                               # what's new (shown on the release page)
)
# Publishes a new version: puts the version number in the source, builds DinoVisualizer.exe, commits and pushes the
# code, and makes a GitHub release with the exe attached. Copies of the app that are running find it on their next
# check (or when Check For Updates is clicked) and update themselves.
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version should look like 1.2.3" }
$git = 'C:\Program Files\Git\cmd\git.exe'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'

$src = "$PSScriptRoot\src\DinoVisualizer.cs"
$text = [IO.File]::ReadAllText($src)
$text = [regex]::Replace($text, 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(""$Version.0"")")
[IO.File]::WriteAllText($src, $text, (New-Object Text.UTF8Encoding $true))

$exe = "$PSScriptRoot\DinoVisualizer.exe"
$before = if (Test-Path $exe) { (Get-Item $exe).LastWriteTimeUtc } else { [DateTime]::MinValue }
& "$PSScriptRoot\build.ps1"
if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTimeUtc -le $before) { throw "The build failed (is the app still running? Exit it from the tray first)" }
$built = [Reflection.AssemblyName]::GetAssemblyName($exe).Version
if ($built.ToString(3) -ne $Version) { throw "The exe says version $built, not $Version" }

& $git -C $PSScriptRoot add -A
& $git -C $PSScriptRoot commit -m "Version $Version"
& $git -C $PSScriptRoot tag "v$Version"
& $git -C $PSScriptRoot push origin HEAD "v$Version"
if ($LASTEXITCODE -ne 0) { throw "Pushing to GitHub failed" }

if ($Notes -eq "") { $Notes = "Dino Visualizer $Version" }
& $gh release create "v$Version" $exe --repo (& $git -C $PSScriptRoot remote get-url origin) --title "Dino Visualizer $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Making the GitHub release failed" }
Write-Host "Released Dino Visualizer $Version"
