param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

Push-Location $repoRoot
try {
    dotnet test '.\tests\uDiscord.Core.Tests\uDiscord.Core.Tests.csproj' -c $Configuration
    dotnet build '.\src\uDiscord.Rocket\uDiscord.Rocket.csproj' -c $Configuration
} finally {
    Pop-Location
}
