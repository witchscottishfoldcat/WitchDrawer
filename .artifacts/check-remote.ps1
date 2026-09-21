Write-Output "---remote processes---"
Get-Process | Where-Object { $_.ProcessName -match 'sunlogin|oray|gameviewer|uu|todesk|anydesk|teamviewer|parsecd|moonlight|nvstreamer|sunshine' } |
    Select-Object ProcessName, Id, @{N='CPU_s';E={[math]::Round($_.TotalProcessorTime.TotalSeconds,1)}}, @{N='WS_MB';E={[math]::Round($_.WorkingSet64/1MB,0)}} |
    Format-Table -AutoSize
Write-Output "---sessions---"
query session 2>$null
Write-Output "---virtual display monitors---"
Get-CimInstance -Namespace root\cimv2 -ClassName Win32_DesktopMonitor | Select-Object Name, MonitorType | Format-Table -AutoSize
