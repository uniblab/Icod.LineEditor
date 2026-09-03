param([ValidateSet('all','clean','restore','build','test','pack','validate')][string]$Section='all',[ValidateSet('Debug','Staging','Release')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$solutionPath=Get-RepositorySolution -RepositoryRoot $repositoryRoot
$artifactDirectory=Join-Path $repositoryRoot 'artifacts'
function Clean { Invoke-DotNet -Arguments @('clean',$solutionPath,'-c',$Configuration) }
function Restore { Invoke-DotNet -Arguments @('restore',$solutionPath) }
function Build { Invoke-DotNet -Arguments @('build',$solutionPath,'-c',$Configuration,'--no-restore') }
function Test { Invoke-DotNet -Arguments @('test',$solutionPath,'-c',$Configuration,'--no-build','--no-restore') }
function Pack { New-Item -ItemType Directory -Path $artifactDirectory -Force|Out-Null; Invoke-DotNet -Arguments @('pack',$solutionPath,'-c',$Configuration,'--no-build','--no-restore','-o',$artifactDirectory) }
function Validate { & (Join-Path $PSScriptRoot 'VerifyPackageArtifact.ps1') -ArtifactDirectory $artifactDirectory -Configuration $Configuration -AllowNoPackages }
switch($Section){'all'{Clean;Restore;Build;Test;Pack;Validate}'clean'{Clean}'restore'{Restore}'build'{Build}'test'{Test}'pack'{Pack}'validate'{Validate}}
