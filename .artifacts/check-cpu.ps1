$procId = 13276
$p = Get-Process -Id $procId -ErrorAction SilentlyContinue
if (-not $p) {
    Write-Output "PROCESS_NOT_FOUND"
    exit 0
}
$cpu0 = $p.TotalProcessorTime.TotalMilliseconds
Start-Sleep -Seconds 3
$p = Get-Process -Id $procId -ErrorAction SilentlyContinue
$cpu1 = $p.TotalProcessorTime.TotalMilliseconds
$delta = $cpu1 - $cpu0
$percent = [math]::Round($delta / 3000 * 100, 1)
Write-Output ("CPU_delta_ms={0} avg_percent={1} threads={2} workingSetMB={3}" -f [math]::Round($delta,1), $percent, $p.Threads.Count, [math]::Round($p.WorkingSet64/1MB,1))
