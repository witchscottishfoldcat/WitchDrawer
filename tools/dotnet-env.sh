#!/usr/bin/env bash
# 沙箱 shell 缺少若干 Windows 环境变量，会导致 dotnet/NuGet 还原失败
# （NuGetEnvironment.GetFolderPath 返回 null，报 "Value cannot be null (Parameter 'path1')"）。
# 用法: tools/dotnet-env.sh <dotnet 参数...>
env \
  PATH="/c/Program Files/dotnet:$PATH" \
  TEMP='C:\Users\Administrator\AppData\Local\Temp' \
  TMP='C:\Users\Administrator\AppData\Local\Temp' \
  PROGRAMDATA='C:\ProgramData' \
  ALLUSERSPROFILE='C:\ProgramData' \
  ProgramFiles='C:\Program Files' \
  'ProgramFiles(x86)=C:\Program Files (x86)' \
  ProgramW6432='C:\Program Files' \
  CommonProgramFiles='C:\Program Files\Common Files' \
  'CommonProgramFiles(x86)=C:\Program Files (x86)\Common Files' \
  CommonProgramW6432='C:\Program Files\Common Files' \
  dotnet "$@"
