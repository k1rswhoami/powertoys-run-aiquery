$pluginDir = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\AIQuery"
$srcDir    = "$PSScriptRoot\Community.PowerToys.Run.Plugin.AIQuery"

Set-Location $srcDir
dotnet build -c Release -v quiet
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# Stop PowerToys Run
$pt = Get-Process -Name "PowerToys" -ErrorAction SilentlyContinue
if ($pt) {
    Write-Host "Stopping PowerToys..."
    $pt | Stop-Process -Force
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item "$srcDir\bin\Release\net10.0-windows\*" -Destination $pluginDir -Recurse -Force
Write-Host "Plugin deployed to $pluginDir"

# Restart PowerToys
$ptExe = "$env:LOCALAPPDATA\PowerToys\PowerToys.exe"
if (Test-Path $ptExe) {
    Write-Host "Restarting PowerToys..."
    Start-Process $ptExe
}
