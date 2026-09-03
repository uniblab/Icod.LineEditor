param([Parameter(Mandatory=$true)][string]$RuntimeIdentifier,[Parameter(Mandatory=$true)][string]$Version,[ValidateSet('Debug','Staging','Release')][string]$Configuration='Release',[string]$ArchiveBaseName='Icod.LineEditor')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repositoryRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'RepositoryTools.psm1') -Force
$solutionPath=Get-RepositorySolution -RepositoryRoot $repositoryRoot
$projects=@(Get-SolutionProjects -SolutionPath $solutionPath -RepositoryRoot $repositoryRoot)
$executables=@(Get-ExecutableProjects -ProjectPaths $projects -Configuration $Configuration)
$releaseRoot=Join-Path $repositoryRoot 'artifacts/release'
$stageName="$ArchiveBaseName-$Version-$RuntimeIdentifier"
$stageParent=Join-Path $releaseRoot 'stage'
$stage=Join-Path $stageParent $stageName
$archive=Join-Path $releaseRoot "$stageName.zip"
if(Test-Path $stage){Remove-Item $stage -Recurse -Force}; New-Item -ItemType Directory -Path $stage -Force|Out-Null
Invoke-DotNet -Arguments @('restore',$solutionPath,'-r',$RuntimeIdentifier)
foreach($executable in $executables){
    $publish=Join-Path $releaseRoot "publish/$RuntimeIdentifier/$($executable.AssemblyName)"
    Invoke-DotNet -Arguments @('publish',$executable.ProjectPath,'-c',$Configuration,'-r',$RuntimeIdentifier,'--no-restore','--self-contained','false','-p:PublishSingleFile=true','-p:PublishTrimmed=false','-p:DebugType=None','-p:DebugSymbols=false','-p:ContinuousIntegrationBuild=true','-o',$publish)
    $file=if($RuntimeIdentifier.StartsWith('win-')){"$($executable.AssemblyName).exe"}else{$executable.AssemblyName}
    Copy-Item (Join-Path $publish $file) (Join-Path $stage $file)
}
foreach($support in @('README.md','LICENSE')){if(Test-Path (Join-Path $repositoryRoot $support)){Copy-Item (Join-Path $repositoryRoot $support) (Join-Path $stage $support)}}
if($RuntimeIdentifier.StartsWith('win-')){Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal}else{Get-ChildItem $stage -File|Where-Object{$_.Name -in @('lineeditor','ed','red','sed')}|ForEach-Object{& chmod +x $_.FullName}; Push-Location $stageParent;try{& zip -r -q $archive $stageName;if(0 -ne $LASTEXITCODE){throw 'zip failed'}}finally{Pop-Location}}
Write-Host "Created release archive: $archive"
