param(
    [Parameter(Mandatory = $true)][string]$Version,   # the new version, like 1.0.1
    [string]$Notes = ""                               # what's new (shown on the release page)
)
# Publishes a new version: puts the version number in the source, builds DinoVisualizer.exe, commits and pushes the
# code, and makes a GitHub release with the exe attached. Copies of the app that are running find it on their next
# check (or when Check For Updates is clicked) and update themselves. It's built in _release, so the app can stay
# running; the copy in this folder is replaced too if it isn't (otherwise it updates itself like the others).
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version should look like 1.2.3" }
$git = 'C:\Program Files\Git\cmd\git.exe'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
$env:Path = (Split-Path $git) + ";" + $env:Path   # gh calls git itself

$src = "$PSScriptRoot\src\DinoVisualizer.cs"
$text = [IO.File]::ReadAllText($src)
$text = [regex]::Replace($text, 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(""$Version.0"")")
[IO.File]::WriteAllText($src, $text, (New-Object Text.UTF8Encoding $true))

$exe = "$PSScriptRoot\_release\DinoVisualizer.exe"
New-Item -ItemType Directory -Force (Split-Path $exe) | Out-Null
if (Test-Path $exe) { Remove-Item $exe }
& "$PSScriptRoot\build.ps1" -Out $exe
if (-not (Test-Path $exe)) { throw "The build failed" }
$built = [Reflection.AssemblyName]::GetAssemblyName($exe).Version
if ($built.ToString(3) -ne $Version) { throw "The exe says version $built, not $Version" }

& $git -C $PSScriptRoot add -A
& $git -C $PSScriptRoot commit -m "Version $Version"
& $git -C $PSScriptRoot tag "v$Version"
& $git -C $PSScriptRoot push origin HEAD "v$Version"
if ($LASTEXITCODE -ne 0) { throw "Pushing to GitHub failed" }

if ($Notes -eq "") { $Notes = "Dino Visualizer $Version" }
# The notes go in a file: quotes in them would otherwise be mangled on the way to gh.
$notesFile = "$PSScriptRoot\_release\notes.md"
[IO.File]::WriteAllText($notesFile, $Notes, (New-Object Text.UTF8Encoding $false))
Push-Location $PSScriptRoot   # gh finds the repo from this folder's git remote
try { & $gh release create "v$Version" $exe --title "Dino Visualizer $Version" --notes-file $notesFile }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Making the GitHub release failed" }

try { Copy-Item $exe "$PSScriptRoot\DinoVisualizer.exe" -Force -ErrorAction Stop; Write-Host "Updated DinoVisualizer.exe here too" }
catch { Write-Host "DinoVisualizer.exe here is running; it'll update itself from the release (or click Check For Updates)" }
Write-Host "Released Dino Visualizer $Version"
