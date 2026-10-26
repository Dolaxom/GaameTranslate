param([switch]$SkipTests, [string]$RuntimeVersion='10.0.13')
$ErrorActionPreference='Stop'
$root=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    if(-not $SkipTests){ & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release }
    $destination=Join-Path $root 'artifacts/publish/win-x64'
    dotnet publish src/GaameTranslate.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:GaameRuntimeVersion=$RuntimeVersion -p:TargetLatestRuntimePatch=true -o $destination
    if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $destination
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination $destination -Recurse -Force
    $userScripts=Join-Path $destination 'scripts'
    New-Item -ItemType Directory -Path $userScripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'scripts/Start-LocalModels.ps1') -Destination $userScripts -Force
    $packageVersion=([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    $archive=Join-Path $root "artifacts/GaameTranslate-$packageVersion-win-x64.zip"
    Compress-Archive -Path (Join-Path $destination '*') -DestinationPath $archive -Force
    $digest=Get-FileHash -LiteralPath $archive -Algorithm SHA256
    Set-Content -LiteralPath ($archive+'.sha256') -Value ($digest.Hash.ToLowerInvariant()+'  '+[System.IO.Path]::GetFileName($archive)) -Encoding ascii
    $digest | Format-List
    Write-Output "Published: $destination"
    Write-Output "Archive: $archive"
} finally { Pop-Location }
