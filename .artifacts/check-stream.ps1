$names = @('GameViewerServer','GameViewerService','GameViewerHealthd')
$snap1 = @{}
foreach ($n in $names) {
    $p = Get-Process -Name $n -ErrorAction SilentlyContinue
    if ($p) { $snap1[$n] = $p.TotalProcessorTime.TotalMilliseconds }
}
Start-Sleep -Seconds 3
foreach ($n in $names) {
    $p = Get-Process -Name $n -ErrorAction SilentlyContinue
    if ($p -and $snap1.ContainsKey($n)) {
        $delta = $p.TotalProcessorTime.TotalMilliseconds - $snap1[$n]
        Write-Output ("{0}: CPU_delta_ms={1} threads={2} handles={3}" -f $n, [math]::Round($delta,1), $p.Threads.Count, $p.HandleCount)
    }
}
Write-Output "---network connections of GameViewerServer---"
$gv = Get-Process -Name GameViewerServer -ErrorAction SilentlyContinue
if ($gv) {
    Get-NetTCPConnection -OwningProcess $gv.Id -ErrorAction SilentlyContinue |
        Where-Object { $_.State -eq 'Established' } |
        Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State | Format-Table -AutoSize
    Get-NetUDPEndpoint -OwningProcess $gv.Id -ErrorAction SilentlyContinue |
        Select-Object LocalAddress, LocalPort | Format-Table -AutoSize
}
