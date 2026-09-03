param([ValidateSet('Debug','Staging','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$solutionPath=Get-RepositorySolution -RepositoryRoot $repositoryRoot
$packageDirectory=Join-Path $repositoryRoot 'artifacts/distribution-validation/packages'
if(Test-Path (Split-Path $packageDirectory -Parent)){Remove-Item (Split-Path $packageDirectory -Parent) -Recurse -Force}
New-Item -ItemType Directory -Path $packageDirectory -Force|Out-Null
Invoke-DotNet -Arguments @('restore',$solutionPath)
Invoke-DotNet -Arguments @('build',$solutionPath,'-c',$Configuration,'--no-restore','-p:ContinuousIntegrationBuild=true')
Invoke-DotNet -Arguments @('test',$solutionPath,'-c',$Configuration,'--no-build','--no-restore','--logger','trx')
Invoke-DotNet -Arguments @('pack',$solutionPath,'-c',$Configuration,'--no-build','--no-restore','-o',$packageDirectory,'-p:ContinuousIntegrationBuild=true')
& (Join-Path $PSScriptRoot 'VerifyPackageArtifact.ps1') -ArtifactDirectory $packageDirectory -Configuration $Configuration -AllowNoPackages
