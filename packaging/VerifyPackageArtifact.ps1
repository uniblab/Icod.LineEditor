param([Parameter(Mandatory=$true)][string]$ArtifactDirectory,[ValidateSet('Debug','Staging','Release')][string]$Configuration='Release',[string]$ExpectedVersion='',[switch]$AllowNoPackages,[string]$GitHubOutputPath='')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
if(-not [System.IO.Path]::IsPathRooted($ArtifactDirectory)){$ArtifactDirectory=Join-Path $repositoryRoot $ArtifactDirectory}
$packages=@(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter '*.nupkg' -File | Where-Object {-not $_.Name.EndsWith('.symbols.nupkg')} | Sort-Object Name)
if(-not [string]::IsNullOrWhiteSpace($ExpectedVersion)){$packages=@($packages|Where-Object{(Get-PackageMetadata -PackagePath $_.FullName).Version -eq $ExpectedVersion})}
if(0 -eq $packages.Count -and -not $AllowNoPackages){throw 'No matching NuGet packages were found.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($package in $packages){
    $metadata=Get-PackageMetadata -PackagePath $package.FullName
    $archive=[System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try{
        if(-not [string]::IsNullOrWhiteSpace($metadata.Readme) -and $null -eq ($archive.Entries|Where-Object{$_.FullName -eq $metadata.Readme}|Select-Object -First 1)){throw "Package '$($package.Name)' declares a missing readme."}
    }finally{$archive.Dispose()}
}
if(-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)){"package_count=$($packages.Count)" >> $GitHubOutputPath;"has_packages=$((0 -lt $packages.Count).ToString().ToLowerInvariant())" >> $GitHubOutputPath}
Write-Host "Exact package verification completed successfully for $($packages.Count) package(s) ($Configuration)."
