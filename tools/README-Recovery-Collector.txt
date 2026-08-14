WitchDrawer recovery diagnostic collector

1. Close WitchDrawer and any editor that may have files open.
2. Connect an external USB drive. Use a drive that is different from the data drives.
3. Copy Collect-WitchDrawerRecoveryBundle.ps1 and this file to the USB drive.
4. Open PowerShell and run, replacing E: with the USB drive letter:

   Set-ExecutionPolicy -Scope Process Bypass
   & "E:\Collect-WitchDrawerRecoveryBundle.ps1" -OutputRoot "E:\WitchDrawer-Recovery" -DataDirectories "C:\Users\<USER>\AppData\Local\WitchDrawer","D:\WD"

5. If the original source folder still exists, add its path:

   -SourceFolders "D:\path\to\original\folder"

6. Send the generated ZIP file to the developer. Do not edit or delete anything
   from the original computer before the recovery work is complete.

The bundle contains database copies, logs, volume information, and metadata-only
file inventories. It does not contain user file contents, but database and logs
may contain filenames and full paths.
