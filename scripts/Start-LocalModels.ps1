param([string]$Model='translategemma:4b',[switch]$Assistant)
$ErrorActionPreference='Stop'
$root=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsDirectory=Join-Path $root 'artifacts/tools/ollama'
$modelDirectory=Join-Path $root 'artifacts/models/ollama'
New-Item -ItemType Directory -Path $toolsDirectory,$modelDirectory -Force | Out-Null
$ollama=Join-Path $toolsDirectory 'ollama.exe'
try { $null=Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/version' -TimeoutSec 3; $running=$true } catch { $running=$false }
if(-not $running) {
    if(-not (Test-Path -LiteralPath $ollama)) {
        $release=Invoke-RestMethod -Uri 'https://api.github.com/repos/ollama/ollama/releases/latest'
        $asset=$release.assets | Where-Object {$_.name -eq 'ollama-windows-amd64.zip'} | Select-Object -First 1
        if(-not $asset){throw 'Official Windows x64 portable archive not found.'}
        $archive=Join-Path $root 'artifacts/tools/ollama-windows-amd64.zip'
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive
        if($asset.digest -and $asset.digest.StartsWith('sha256:')) {
            if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $asset.digest.Substring(7)){throw 'Ollama archive checksum mismatch.'}
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $toolsDirectory -Force
    }
    $env:OLLAMA_HOST='127.0.0.1:11434'
    $env:OLLAMA_MODELS=$modelDirectory
    Start-Process -FilePath $ollama -ArgumentList 'serve' -WorkingDirectory $toolsDirectory -WindowStyle Hidden -RedirectStandardOutput (Join-Path $toolsDirectory 'server.log') -RedirectStandardError (Join-Path $toolsDirectory 'server-error.log')
    $ready=$false
    for($attempt=0;$attempt -lt 15;$attempt++) { try { $null=Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/version' -TimeoutSec 2;$ready=$true;break } catch {Start-Sleep -Seconds 2} }
    if(-not $ready){throw 'Local Ollama did not start. Check artifacts/tools/ollama/server-error.log.'}
}
# The pull API works with both the portable server and an existing user-managed Ollama.
$body=@{name=$Model;stream=$false}|ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:11434/api/pull' -ContentType 'application/json' -Body $body -TimeoutSec 3600 | ConvertTo-Json
if($Assistant) {
    Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:11434/api/pull' -ContentType 'application/json' -Body (@{name='qwen3:4b-instruct-2507-q4_K_M';stream=$false}|ConvertTo-Json) -TimeoutSec 3600 | ConvertTo-Json
}
Write-Output 'Ollama ready at http://127.0.0.1:11434. Select Ollama in GaameTranslate settings.'
