Get-CimInstance -ClassName Win32_VideoController | ForEach-Object {
    Write-Output ("GPU: {0} | RefreshRate={1}Hz | DriverVersion={2} | VideoMode={3}" -f $_.Name, $_.CurrentRefreshRate, $_.DriverVersion, $_.VideoModeDescription)
}
Write-Output "---power---"
powercfg /getactivescheme
