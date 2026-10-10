# Installs NestLaptop on this laptop as a task that starts with Windows (run by install_laptop.bat).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

try {
    if (-not (Test-Path config.json)) {
        throw 'config.json not found. Copy config.example.json to config.json and fill it in first.'
    }

    $py = @('py', 'python') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $py) { throw 'Python was not found. Install Python 3.10 or newer from python.org.' }
    $exe = & $py -c 'import sys; print(sys.executable)'
    & $exe -c 'import sys; sys.exit(sys.version_info < (3, 10))'
    if ($LASTEXITCODE -ne 0) { throw "Python 3.10 or newer is needed (found $exe)." }
    $pythonw = Join-Path (Split-Path $exe) 'pythonw.exe'

    # Into a local folder, so the task (which runs as SYSTEM) finds it.
    Write-Host 'Installing the Sinric Pro library...'
    & $exe -m pip install --upgrade --target "$here\lib" -r requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'pip install failed.' }

    & $exe nest_laptop.py check-laptop
    if ($LASTEXITCODE -ne 0) { throw 'Fix config.json (see the message above) and run this again.' }

    $cfg = Get-Content config.json -Raw | ConvertFrom-Json
    Remove-NetFirewallRule -DisplayName 'NestLaptop agent' -ErrorAction SilentlyContinue
    if ($cfg.setup -eq 'with_bridge') {
        $port = if ($cfg.agent_port) { $cfg.agent_port } else { 8765 }
        New-NetFirewallRule -DisplayName 'NestLaptop agent' -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort $port -Profile Private, Domain | Out-Null
        Write-Host "Allowed the bridge to reach this laptop on port $port (private networks only)."
    }

    Stop-ScheduledTask -TaskName NestLaptop -ErrorAction SilentlyContinue
    $action = New-ScheduledTaskAction -Execute $pythonw -Argument "`"$here\nest_laptop.py`" laptop" -WorkingDirectory $here
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    # Laptops: keep running on battery, never time out, restart if it crashes.
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -StartWhenAvailable
    Register-ScheduledTask -TaskName NestLaptop -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName NestLaptop

    Write-Host ''
    Write-Host 'Done. NestLaptop is running and will start with Windows.'
    Write-Host "Its log is $here\nest_laptop.log"
} catch {
    Write-Host ''
    Write-Host "Setup failed: $($_.Exception.Message)" -ForegroundColor Red
}
