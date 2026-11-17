<#
  Запуск системы: NATS + экземпляры EventsLogger + экземпляры RankCalculator + экземпляры Valuator + nginx.
  Использование:  .\scripts\start.ps1 [-Ports 5001,5002] [-Calculators 2] [-Loggers 2]
  Пароли Redis и NATS берутся из scripts\config.local.ps1 и передаются компонентам через переменные окружения.
  Если добавляете порты Valuator, добавьте их и в upstream в nginx/conf/nginx.conf.
  Требуется nats-server (nats\nats-server.exe или в PATH) и redis-server (redis\redis-server.exe, в PATH, либо Docker).
  Запускает 4 экземпляра Redis: DB_MAIN=:6000 (shard map), DB_RU=:6001, DB_EU=:6002, DB_ASIA=:6003.
#>
param(
    [int[]]$Ports = @(5001, 5002),
    [int]$Calculators = 2,
    [int]$Loggers = 2
)

$ErrorActionPreference = 'Stop'
$env:ASPNETCORE_ENVIRONMENT = 'Development'   # наследуется дочерними процессами
$root     = Split-Path -Parent $PSScriptRoot
$valDir   = Join-Path $root 'Valuator'
$rcDir    = Join-Path $root 'RankCalculator'
$elDir    = Join-Path $root 'EventsLogger'
$nginxDir = Join-Path $root 'nginx'
$runDir   = Join-Path $root 'scripts\.run'
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

# Пароли локального окружения (REDIS_PASSWORD, NATS_USER, NATS_PASSWORD)
. (Join-Path $PSScriptRoot 'config.local.ps1')

function Test-Port([int]$Port) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect('127.0.0.1', $Port); return $true } catch { return $false } finally { $c.Close() }
}

# Жив ли процесс из pid-файла (и тот ли это процесс)
function Test-Alive([string]$PidFile, [string[]]$Names) {
    if (-not (Test-Path $PidFile)) { return $null }
    $p = Get-Process -Id ([int](Get-Content $PidFile)) -ErrorAction SilentlyContinue
    if ($p -and ($Names -contains $p.ProcessName)) { return $p }
    return $null
}

# --- Redis: основная БД (shard map) и по одному серверу на регион ---
# Адреса передаются приложениям через переменные окружения (наследуются дочерними процессами)
$env:DB_MAIN = 'localhost:6000'
$env:DB_RU   = 'localhost:6001'
$env:DB_EU   = 'localhost:6002'
$env:DB_ASIA = 'localhost:6003'

$redisExe = $null
foreach ($name in @('redis-server', 'memurai')) {
    $local = Join-Path $root "redis\$name.exe"
    if (Test-Path $local) { $redisExe = $local; break }
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { $redisExe = $cmd.Source; break }
}
$useDocker = (-not $redisExe) -and [bool](Get-Command docker -ErrorAction SilentlyContinue)
if (-not $redisExe -and -not $useDocker) {
    throw 'Не найден redis-server: положите redis-server.exe в папку redis\, добавьте его в PATH или установите Docker.'
}

foreach ($port in 6000..6003) {
    if (Test-Port $port) { Write-Host "Redis на порту $port уже работает"; continue }
    if ($redisExe) {
        # у каждого экземпляра своя рабочая папка (туда пишется dump.rdb)
        $dir = Join-Path $runDir "redis-$port"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $p = Start-Process $redisExe -ArgumentList "--port $port --requirepass $env:REDIS_PASSWORD" -WorkingDirectory $dir -WindowStyle Hidden -PassThru
        $p.Id | Set-Content (Join-Path $runDir "redis-$port.pid")
    } else {
        docker run -d --rm --name "ds-redis-$port" -p "${port}:6379" redis redis-server --requirepass $env:REDIS_PASSWORD | Out-Null
        'docker' | Set-Content (Join-Path $runDir "redis-$port.docker")
    }
    for ($t = 0; $t -lt 20 -and -not (Test-Port $port); $t++) { Start-Sleep -Milliseconds 250 }
    if (Test-Port $port) { Write-Host "Redis запущен на порту $port" }
    else { Write-Warning "Redis не отвечает на порту $port" }
}

# --- Сборка (один раз для всех проектов) ---
Write-Host 'Сборка решения...'
dotnet build (Join-Path $root 'ds-2024.sln') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

# --- NATS ---
if (Test-Port 4222) {
    Write-Host 'NATS уже слушает порт 4222'
} else {
    $natsExe = Join-Path $root 'nats\nats-server.exe'
    if (-not (Test-Path $natsExe)) {
        $cmd = Get-Command nats-server -ErrorAction SilentlyContinue
        if ($cmd) { $natsExe = $cmd.Source } else { $natsExe = $null }
    }
    if (-not $natsExe) {
        throw 'nats-server не найден. Скачайте nats-server.exe (github.com/nats-io/nats-server/releases) в папку nats\ или запустите NATS в Docker: docker run -d -p 4222:4222 nats'
    }
    # Конфигурация NATS с аутентификацией по паролю (генерируется из config.local.ps1)
    $natsConf = Join-Path $runDir 'nats.conf'
    @(
        'port: 4222',
        'authorization {',
        "  user: `"$($env:NATS_USER)`"",
        "  password: `"$($env:NATS_PASSWORD)`"",
        '}'
    ) | Set-Content -Path $natsConf -Encoding ascii
    $p = Start-Process $natsExe -ArgumentList "-c `"$natsConf`"" -WindowStyle Hidden -PassThru
    $p.Id | Set-Content (Join-Path $runDir 'nats.pid')
    Start-Sleep -Seconds 1
    if (Test-Port 4222) { Write-Host "NATS запущен (PID $($p.Id))" }
    else { Write-Warning 'NATS не стартовал на порту 4222' }
}

# --- EventsLogger (запускаем до остальных, чтобы не пропустить события) ---
$elDll = Join-Path $elDir 'bin\Debug\net8.0\EventsLogger.dll'
for ($i = 1; $i -le $Loggers; $i++) {
    $pidFile = Join-Path $runDir "eventslogger-$i.pid"
    $alive = Test-Alive $pidFile @('dotnet')
    if ($alive) { Write-Host "EventsLogger #$i уже работает (PID $($alive.Id)), пропускаю"; continue }
    $log = Join-Path $runDir "eventslogger-$i.log"
    $p = Start-Process dotnet -ArgumentList "`"$elDll`"" -WorkingDirectory $elDir -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $p.Id | Set-Content $pidFile
    Write-Host "EventsLogger #$i запущен (PID $($p.Id)), лог: $log"
}

# --- RankCalculator ---
$rcDll = Join-Path $rcDir 'bin\Debug\net8.0\RankCalculator.dll'
for ($i = 1; $i -le $Calculators; $i++) {
    $pidFile = Join-Path $runDir "rankcalc-$i.pid"
    $alive = Test-Alive $pidFile @('dotnet')
    if ($alive) { Write-Host "RankCalculator #$i уже работает (PID $($alive.Id)), пропускаю"; continue }
    $log = Join-Path $runDir "rankcalc-$i.log"
    $p = Start-Process dotnet -ArgumentList "`"$rcDll`"" -WorkingDirectory $rcDir -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $p.Id | Set-Content $pidFile
    Write-Host "RankCalculator #$i запущен (PID $($p.Id)), лог: $log"
}

# --- Valuator ---
$valDll = Join-Path $valDir 'bin\Debug\net8.0\Valuator.dll'
foreach ($port in $Ports) {
    $pidFile = Join-Path $runDir "valuator-$port.pid"
    $alive = Test-Alive $pidFile @('dotnet')
    if ($alive) { Write-Host "Valuator на порту $port уже работает (PID $($alive.Id)), пропускаю"; continue }
    $log = Join-Path $runDir "valuator-$port.log"
    $p = Start-Process dotnet -ArgumentList "`"$valDll`" --urls http://0.0.0.0:$port" `
            -WorkingDirectory $valDir -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $p.Id | Set-Content $pidFile
    Write-Host "Valuator запущен на порту $port (PID $($p.Id)), лог: $log"
}

# --- nginx ---
# Без -p: nginx под Windows не понимает не-ASCII символы в пути, префикс берётся из расположения nginx.exe
if (Get-Process nginx -ErrorAction SilentlyContinue) {
    Push-Location $nginxDir; & .\nginx.exe -s reload; Pop-Location
    Write-Host 'nginx уже работал: конфигурация перечитана'
} else {
    Start-Process (Join-Path $nginxDir 'nginx.exe') -WorkingDirectory $nginxDir -WindowStyle Hidden
    Start-Sleep -Seconds 1
    if (Get-Process nginx -ErrorAction SilentlyContinue) { Write-Host 'nginx запущен' }
    else { Write-Warning 'nginx не запустился, смотрите nginx\logs\error.log' }
}

Write-Host 'Готово: http://localhost:8080/'
