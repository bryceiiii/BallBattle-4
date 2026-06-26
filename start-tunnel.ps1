# BallBattle-4 Auto-Start (v4 - fully auto, no manual input)
$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$PORT = 3000
$URL_FILE = "$ScriptDir\tunnel-url.txt"
$TOKEN_FILE = "$ScriptDir\.cloudflare-tunnel\token.txt"
if ($env:GITHUB_TOKEN) {
    $TOKEN = $env:GITHUB_TOKEN
} elseif (Test-Path $TOKEN_FILE) {
    $TOKEN = (Get-Content $TOKEN_FILE -Raw).Trim()
} else {
    $TOKEN = $null
}
$PY = "C:\Users\Administrator\.workbuddy\binaries\python\versions\3.13.12\python.exe"
$HLP = "C:\Users\Administrator\.workbuddy\skills\cloudflare-tunnel\scripts\tunnel_helper.py"

New-Item -ItemType Directory -Force -Path "$ScriptDir\.cloudflare-tunnel" | Out-Null

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BallBattle-4 Server Starting..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host ""
Write-Host "[1/3] Starting SpacetimeDB..." -ForegroundColor Yellow
spacetime server stop 2>$null
Start-Sleep 1
Start-Process -NoNewWindow -FilePath spacetime -ArgumentList "start -l 0.0.0.0:$PORT --in-memory"

# Wait for server to be fully ready by trying to publish
Write-Host "  Waiting for server..."
Push-Location "$ScriptDir\server-csharp\spacetimedb"
$ok = $false
for ($i = 1; $i -le 15; $i++) {
    spacetime publish -s "http://127.0.0.1:$PORT" ballbattle4 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $ok = $true
        Write-Host "  [OK] SpacetimeDB ready (attempt $i)" -ForegroundColor Green
        break
    }
    Write-Host "  Starting... ($i/15)" -ForegroundColor DarkGray
    Start-Sleep 2
}
Pop-Location

if (-not $ok) {
    Write-Host "  [ERROR] SpacetimeDB failed to start"
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "[2/3] Creating Quick Tunnel (auto)..." -ForegroundColor Yellow

$json = & $PY $HLP quick --url "http://localhost:$PORT" --protocol quic 2>&1
$data = $json | ConvertFrom-Json

if (-not $data.ok) {
    Write-Host "  [ERROR] Tunnel failed: $($data.error)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$url = $data.public_url
Write-Host "  [OK] Tunnel: $url" -ForegroundColor Green
$url | Out-File -FilePath $URL_FILE -Encoding utf8

Write-Host ""
Write-Host "[3/3] Publishing URL..." -ForegroundColor Yellow

if ($TOKEN) {
    try {
        $gistFile = "$ScriptDir\.cloudflare-tunnel\gist-id.txt"
        $gistId = if (Test-Path $gistFile) { (Get-Content $gistFile -Raw).Trim() } else { $null }
        $body = @{ description = "BallBattle-4 Server"; public = $true; files = @{ "tunnel-url" = @{ content = $url } } } | ConvertTo-Json -Depth 3
        $h = @{ "Authorization" = "Bearer $TOKEN"; "Accept" = "application/vnd.github.v3+json" }

        if ($gistId) {
            Invoke-RestMethod -Uri "https://api.github.com/gists/$gistId" -Method Patch -Headers $h -Body $body -ContentType "application/json" | Out-Null
            Write-Host "  [OK] Gist updated" -ForegroundColor Green
        } else {
            $r = Invoke-RestMethod -Uri "https://api.github.com/gists" -Method Post -Headers $h -Body $body -ContentType "application/json"
            $gistId = $r.id
            $r.id | Out-File -FilePath $gistFile -Encoding utf8
            Write-Host "  [OK] Gist created (ID: $gistId)" -ForegroundColor Green
        }

        $rawUrl = "https://gist.githubusercontent.com/raw/$gistId/tunnel-url"
        Write-Host "  >> Fixed Entry: $rawUrl" -ForegroundColor Cyan
        Write-Host "     Share with friends. Auto-updates!" -ForegroundColor DarkGray
    } catch {
        Write-Host "  [WARN] GitHub: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "  No GitHub Token set" -ForegroundColor DarkGray
    Write-Host "  Save once: echo YOUR_TOKEN > .cloudflare-tunnel\token.txt" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Public URL: $url" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to stop all services"

Write-Host "Stopping..."
Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force
spacetime server stop 2>$null
Write-Host "Done."