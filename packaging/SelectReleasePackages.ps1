param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force

foreach ($variableName in @('SourceDirectory', 'DestinationDirectory')) {
    $value = Get-Variable -Name $variableName -ValueOnly
    if (-not [System.IO.Path]::IsPathRooted($value)) {
        $value = Join-Path $repositoryRoot $value
    }
    Set-Variable -Name $variableName -Value ([System.IO.Path]::GetFullPath($value))
}

if (Test-Path -LiteralPath $DestinationDirectory) {
    Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null

$selected = @()
$packages = @(
    Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.nupkg' -File |
        Where-Object { -not $_.Name.EndsWith('.symbols.nupkg', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object Name
)
foreach ($package in $packages) {
    $metadata = Get-PackageMetadata -PackagePath $package.FullName
    if ($metadata.Version -ne $ExpectedVersion) {
        continue
    }
    Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $DestinationDirectory $package.Name)
    $selected += $package.Name
}

$hasPackages = 0 -lt $selected.Count
if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "has_packages=$($hasPackages.ToString().ToLowerInvariant())" >> $GitHubOutputPath
    "package_count=$($selected.Count)" >> $GitHubOutputPath
}
Write-Host "Selected $($selected.Count) package(s) for release $ExpectedVersion."
