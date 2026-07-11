param(
    [Parameter(Mandatory = $true)]
    [string] $Source
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$target = Join-Path $repoRoot 'lib'

if (-not (Test-Path $Source)) {
    throw "Reference source path does not exist: $Source"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$required = @(
    'Assembly-CSharp.dll',
    'Rocket.API.dll',
    'Rocket.Core.dll',
    'Rocket.Unturned.dll',
    'SDG.NetTransport.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'Newtonsoft.Json.dll'
)

function Find-Reference([string] $Name) {
    $direct = Join-Path $Source $Name
    if (Test-Path $direct) { return (Resolve-Path $direct).Path }

    $matches = @(Get-ChildItem -Path $Source -Filter $Name -File -Recurse -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) { return $null }

    $preferred = $matches | Sort-Object @{ Expression = {
        if ($_.FullName -match 'Rocket[\\/]Libraries|Rocket[\\/]Modules|Unturned_Data[\\/]Managed') { 0 } else { 1 }
    } }, FullName | Select-Object -First 1
    return $preferred.FullName
}

foreach ($name in $required) {
    $sourcePath = Find-Reference $name
    if (-not $sourcePath) { throw "Missing required reference '$name' under: $Source" }
    Copy-Item $sourcePath (Join-Path $target $name) -Force
    Write-Host "Staged $name from $sourcePath"
}

$steamworks = Find-Reference 'com.rlabrecque.steamworks.net.dll'
if (-not $steamworks) { $steamworks = Find-Reference 'Steamworks.NET.dll' }
if (-not $steamworks) { throw "Missing Steamworks runtime assembly under: $Source" }
Copy-Item $steamworks (Join-Path $target ([IO.Path]::GetFileName($steamworks))) -Force
Write-Host "Staged $([IO.Path]::GetFileName($steamworks)) from $steamworks"

Write-Host "References staged in $target"
Write-Host 'These files are ignored by Git and must not be committed.'
