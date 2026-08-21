$ErrorActionPreference = 'Stop'

$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\DisplayDeck'
$DataDir = Join-Path $env:LOCALAPPDATA 'DisplayDeck'
$PackageDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceFile = Join-Path $PackageDir 'src\DisplayDeck.Wpf.cs'
$ManifestFile = Join-Path $PackageDir 'src\app.manifest'
$IconFile = Join-Path $PackageDir 'assets\DisplayDeck.ico'
$IconBase64File = Join-Path $PackageDir 'assets\DisplayDeck.ico.b64'
$ThemeFile = Join-Path $PackageDir 'Theme.xaml'
$EngineSource = Join-Path $PackageDir 'NvDisplayEngine.exe'
$EngineGoSource = Join-Path $PackageDir 'src\NvDisplayEngine.go'
$ExeFile = Join-Path $InstallDir 'DisplayDeck.exe'

Write-Host ''
Write-Host '  DisplayDeck 1.0' -ForegroundColor Green
Write-Host '  Installing the display profile manager...' -ForegroundColor DarkGray
Write-Host ''

Get-Process -Name 'DisplayDeck','DisplayModes' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 350

New-Item -ItemType Directory -Force -Path $InstallDir, $DataDir | Out-Null

# Preserve profiles from the final Display Modes beta when upgrading on the same PC.
$LegacyDataDir = Join-Path $env:LOCALAPPDATA 'DisplayModesNext'
if ((Test-Path $LegacyDataDir) -and -not (Test-Path (Join-Path $DataDir 'profiles\catalog.json'))) {
    Write-Host '       Importing existing display profiles...' -ForegroundColor DarkGray
    Copy-Item (Join-Path $LegacyDataDir '*') $DataDir -Recurse -Force -ErrorAction SilentlyContinue
}

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'The built-in .NET Framework C# compiler could not be found. Enable .NET Framework 4.8 and run Setup again.' }
if (-not (Test-Path $ThemeFile)) { throw 'Theme.xaml is missing from this package.' }

# Release packages include the prebuilt engine. Source checkouts can build it locally when Go is available.
if (-not (Test-Path $EngineSource)) {
    $go = Get-Command go.exe -ErrorAction SilentlyContinue
    if (-not $go) {
        throw 'NvDisplayEngine.exe is not present. Download a packaged release, or install Go and run Setup again to build the NVAPI engine from source.'
    }
    if (-not (Test-Path $EngineGoSource)) { throw 'src\NvDisplayEngine.go is missing.' }
    $EngineSource = Join-Path $env:TEMP 'DisplayDeck-NvDisplayEngine.exe'
    Write-Host '       Building the NVAPI engine from source...' -ForegroundColor DarkGray
    & $go.Source build -trimpath -ldflags='-s -w' -o $EngineSource $EngineGoSource
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $EngineSource)) { throw 'The NVAPI engine could not be built.' }
}

# Packaged releases include the ICO. Source checkouts can reconstruct it from the tracked base64 asset.
if (-not (Test-Path $IconFile)) {
    if (-not (Test-Path $IconBase64File)) { throw 'DisplayDeck icon asset is missing.' }
    $IconFile = Join-Path $env:TEMP 'DisplayDeck.ico'
    [IO.File]::WriteAllBytes($IconFile, [Convert]::FromBase64String((Get-Content $IconBase64File -Raw).Trim()))
}

$frameworkDir = Split-Path -Parent $csc
$wpfDir = Join-Path $frameworkDir 'WPF'
function Resolve-FrameworkAssembly([string]$name) {
    $candidates = @((Join-Path $wpfDir $name), (Join-Path $frameworkDir $name))
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) { return $found }
    return $name
}

Write-Host '  [1/5] Building the WPF application...' -ForegroundColor Gray
$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Xml.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll',
    'WindowsBase.dll',
    'PresentationCore.dll',
    'PresentationFramework.dll',
    'System.Xaml.dll'
) | ForEach-Object { Resolve-FrameworkAssembly $_ }

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$ExeFile",
    "/win32icon:$IconFile",
    "/win32manifest:$ManifestFile"
)
foreach ($reference in $references) { $compilerArgs += "/reference:$reference" }
$compilerArgs += $SourceFile
& $csc @compilerArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ExeFile)) { throw 'DisplayDeck could not be compiled.' }

Write-Host '  [2/5] Installing the NVAPI engine and theme...' -ForegroundColor Gray
Copy-Item $EngineSource (Join-Path $InstallDir 'NvDisplayEngine.exe') -Force
Copy-Item $ThemeFile (Join-Path $InstallDir 'Theme.xaml') -Force
Copy-Item $IconFile (Join-Path $InstallDir 'DisplayDeck.ico') -Force

try {
    $probe = & (Join-Path $InstallDir 'NvDisplayEngine.exe') probe 2>&1
    if ($LASTEXITCODE -eq 0) { Write-Host ('       ' + ($probe -join ' ')) -ForegroundColor DarkGray }
    else { Write-Host ('       NVAPI probe warning: ' + ($probe -join ' ')) -ForegroundColor Yellow }
} catch {
    Write-Host '       NVAPI probe could not run yet. Diagnostics can retry after launch.' -ForegroundColor Yellow
}

# Clean up the older beta startup entry if present.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DisplayModes' -ErrorAction SilentlyContinue

Write-Host '  [3/5] Creating shortcuts...' -ForegroundColor Gray
$wsh = New-Object -ComObject WScript.Shell
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DisplayDeck'
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DisplayDeck.lnk'
$startMenu = Join-Path $startMenuDir 'DisplayDeck.lnk'
foreach ($shortcutPath in @($desktop, $startMenu)) {
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $ExeFile
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = "$ExeFile,0"
    $shortcut.Description = 'Save and switch complete NVIDIA display layouts'
    $shortcut.Save()
}

$uninstallSource = Join-Path $PackageDir 'Uninstall.ps1'
if (Test-Path $uninstallSource) { Copy-Item $uninstallSource (Join-Path $InstallDir 'Uninstall.ps1') -Force }
$uninstallShortcut = Join-Path $startMenuDir 'Uninstall DisplayDeck.lnk'
$u = $wsh.CreateShortcut($uninstallShortcut)
$u.TargetPath = 'powershell.exe'
$u.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'Uninstall.ps1')`""
$u.WorkingDirectory = $InstallDir
$u.IconLocation = "$ExeFile,0"
$u.Save()

Write-Host '  [4/5] Registering uninstall information...' -ForegroundColor Gray
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DisplayDeck'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'DisplayDeck'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '1.0.0'
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'YungZanji'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$ExeFile,0"
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'Uninstall.ps1')`""
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

Write-Host '  [5/5] Launching DisplayDeck...' -ForegroundColor Gray
Start-Process $ExeFile

Write-Host ''
Write-Host '  Installed successfully.' -ForegroundColor Green
Write-Host '  DisplayDeck is ready.' -ForegroundColor DarkGray
Write-Host ''
Start-Sleep -Milliseconds 1200
