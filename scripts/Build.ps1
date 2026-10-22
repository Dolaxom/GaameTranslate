param([ValidateSet('Debug','Release')][string]$Configuration='Debug', [switch]$NativeVerification)
$ErrorActionPreference='Stop'
$root=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    dotnet restore GaameTranslate.sln
    if($LASTEXITCODE -ne 0){throw 'Restore failed.'}
    dotnet build GaameTranslate.sln -c $Configuration --no-restore
    if($LASTEXITCODE -ne 0){throw 'Build failed.'}
    dotnet test tests/GaameTranslate.Tests -c $Configuration --no-build --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts/test-results
    if($LASTEXITCODE -ne 0){throw 'Unit tests failed.'}
    if($NativeVerification){
        dotnet run --project tests/GaameTranslate.Windows.Verify -c $Configuration --no-build -- artifacts/verification
        if($LASTEXITCODE -ne 0){throw 'Native verification failed. Read artifacts/verification/failure.txt.'}
    }
} finally { Pop-Location }
