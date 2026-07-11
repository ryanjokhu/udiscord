param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
}

$pluginOutput = Join-Path $repoRoot "src\uDiscord.Rocket\bin\$Configuration"
$coreOutput = Join-Path $repoRoot "src\uDiscord.Core\bin\$Configuration\netstandard2.0"
$pluginDll = Join-Path $pluginOutput 'uDiscord.dll'
$coreDll = Join-Path $coreOutput 'uDiscord.Core.dll'
if (-not (Test-Path $pluginDll)) { throw "Missing build output: $pluginDll" }
if (-not (Test-Path $coreDll)) { throw "Missing build output: $coreDll" }

$artifacts = Join-Path $repoRoot 'artifacts'
$stage = Join-Path $artifacts 'uDiscord-1.0.0'
$zip = Join-Path $artifacts 'uDiscord-1.0.0.zip'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Plugins\uDiscord') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Libraries') | Out-Null

Copy-Item $pluginDll (Join-Path $stage 'Plugins\uDiscord\uDiscord.dll')
Copy-Item $coreDll (Join-Path $stage 'Libraries\uDiscord.Core.dll')
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $repoRoot 'docs\INSTALLATION.md') (Join-Path $stage 'INSTALLATION.md')

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Created $zip"
