[CmdletBinding()]
param(
    [string] $LabVIEWRoot
)

$ErrorActionPreference = 'Stop'

if (-not $LabVIEWRoot) {
    $defaultFolders = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ }
    $LabVIEWRoot = $defaultFolders | ForEach-Object { Join-Path $_ 'National Instruments\LabVIEW 2026' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LabVIEW.exe') -PathType Leaf } | Select-Object -First 1
}
if (-not $LabVIEWRoot -or -not (Test-Path -LiteralPath (Join-Path $LabVIEWRoot 'LabVIEW.exe') -PathType Leaf)) {
    throw 'LabVIEW wurde nicht gefunden. Gib den Installationsordner mit -LabVIEWRoot an.'
}

$root = (Resolve-Path -LiteralPath $LabVIEWRoot).Path
$source = Join-Path $PSScriptRoot 'LabVIEW\Intern'
$target = Join-Path $root 'user.lib\_express\PuT3'
$files = @('Virtual DAQSource.llb', '_Virtual DAQConfig.llb', 'PuT3 DAQ.vi', 'PuT3 TCP Austausch.vi')

$changes = foreach ($name in $files) {
    $from = Join-Path $source $name
    $to = Join-Path $target $name
    $hash = (Get-FileHash -LiteralPath $from).Hash
    if (Test-Path -LiteralPath $to -PathType Container) {
        throw "Am Ziel steht ein Ordner statt einer Datei: $to"
    }
    $exists = Test-Path -LiteralPath $to -PathType Leaf
    if (-not $exists -or (Get-FileHash -LiteralPath $to).Hash -ne $hash) {
        [pscustomobject]@{ Name = $name; Source = $from; Target = $to; Hash = $hash; Exists = $exists }
    }
}

if (-not $changes) {
    Write-Host "Virtual DAQ ist bereits aktuell in $target"
    return
}

$backup = $null
if ($changes | Where-Object Exists) {
    $backup = Join-Path $env:LOCALAPPDATA "PuT-Simulationen\LabVIEW-Sicherungen\$(Get-Date -Format 'yyyyMMdd-HHmmss-fffffff')"
    New-Item -Path $backup -ItemType Directory -Force | Out-Null
    foreach ($file in $changes | Where-Object Exists) {
        Copy-Item -LiteralPath $file.Target -Destination $backup
    }
}

$attempted = @()
try {
    New-Item -Path $target -ItemType Directory -Force | Out-Null
    foreach ($file in $changes) {
        $attempted += $file
        Copy-Item -LiteralPath $file.Source -Destination $file.Target -Force
        if ((Get-FileHash -LiteralPath $file.Target).Hash -ne $file.Hash) {
            throw "Pruefsumme stimmt nicht: $($file.Name)"
        }
    }
} catch {
    $failure = $_
    foreach ($file in $attempted) {
        try {
            if ($file.Exists) {
                Copy-Item -LiteralPath (Join-Path $backup $file.Name) -Destination $file.Target -Force
            } elseif (Test-Path -LiteralPath $file.Target -PathType Leaf) {
                Remove-Item -LiteralPath $file.Target
            }
        } catch {
            Write-Warning "Wiederherstellung fehlgeschlagen: $($file.Target). Sicherung: $backup"
        }
    }
    if ($failure.Exception -is [System.UnauthorizedAccessException] -or $failure.CategoryInfo.Category -eq 'PermissionDenied') {
        Write-Warning 'Windows verweigert den Schreibzugriff. Starte PowerShell als Administrator und wiederhole den Aufruf.'
    }
    throw $failure
}

Write-Host "Virtual DAQ installiert in $target"
if ($backup) { Write-Host "Sicherung der ersetzten Dateien in $backup" }
if (Get-Process -Name LabVIEW -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $root 'LabVIEW.exe') }) {
    Write-Host 'LabVIEW neu starten, damit die Palette die neue Bibliothek laedt.'
}
