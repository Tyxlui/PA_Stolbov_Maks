<#
  Останов системы: nginx, Valuator, RankCalculator и NATS (если его запустил start.ps1).
#>
$root     = Split-Path -Parent $PSScriptRoot
$nginxDir = Join-Path $root 'nginx'
$runDir   = Join-Path $root 'scripts\.run'

# nginx
if (Get-Process nginx -ErrorAction SilentlyContinue) {
    Push-Location $nginxDir; & .\nginx.exe -s quit; Pop-Location
    Write-Host 'nginx остановлен'
} else {
    Write-Host 'nginx не запущен'
}

# Valuator, RankCalculator, EventsLogger, NATS, Redis - по pid-файлам
if (Test-Path $runDir) {
    Get-ChildItem $runDir -Filter '*.pid' | ForEach-Object {
        $id = [int](Get-Content $_.FullName)
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($p) {
            taskkill /PID $id /T /F | Out-Null
            Write-Host "Остановлен $($_.BaseName) (PID $id, $($p.ProcessName))"
        }
        Remove-Item $_.FullName -Force
    }
}
# Redis, запущенные в Docker
if (Test-Path $runDir) {
    Get-ChildItem $runDir -Filter 'redis-*.docker' | ForEach-Object {
        $port = $_.BaseName -replace '^redis-', ''
        docker rm -f "ds-redis-$port" | Out-Null
        Write-Host "Остановлен Redis в Docker (порт $port)"
        Remove-Item $_.FullName -Force
    }
}
Write-Host 'Система остановлена'
