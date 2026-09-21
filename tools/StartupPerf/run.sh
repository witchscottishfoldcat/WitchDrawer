#!/usr/bin/env bash
# 手工编译并运行启动性能工具（沙箱无法 dotnet restore，直接用 Roslyn csc 编译）。
# 依赖: 已构建的 src/WitchDrawer.Core/bin/Debug/net10.0 与 SDK 引用程序集。
set -e
OUT=tools/StartupPerf/bin
mkdir -p "$OUT"

REFS=()
for dll in "/c/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.8/ref/net10.0"/*.dll; do
  REFS+=("-r:$dll")
done

env PATH="/c/Program Files/dotnet:$PATH" \
  TEMP='C:\Users\Administrator\AppData\Local\Temp' TMP='C:\Users\Administrator\AppData\Local\Temp' \
  dotnet exec "C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\csc.dll" \
    -nologo -nostdlib -target:exe -langversion:latest -nullable:enable \
    -out:"$OUT/StartupPerf.dll" \
    tools/StartupPerf/Program.cs \
    "${REFS[@]}" \
    -r:src/WitchDrawer.Core/bin/Debug/net10.0/WitchDrawer.Core.dll \
    -r:tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/Microsoft.Data.Sqlite.dll \
    -r:tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.core.dll \
    -r:tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.provider.winsqlite3.dll \
    -r:tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.batteries_v2.dll

cat > "$OUT/StartupPerf.runtimeconfig.json" <<'EOF'
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
  }
}
EOF

cp -f src/WitchDrawer.Core/bin/Debug/net10.0/WitchDrawer.Core.dll "$OUT/"
cp -f tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/Microsoft.Data.Sqlite.dll "$OUT/"
cp -f tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.core.dll "$OUT/"
cp -f tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.provider.winsqlite3.dll "$OUT/"
cp -f tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/SQLitePCLRaw.batteries_v2.dll "$OUT/"
mkdir -p "$OUT/runtimes/win-x64/native"
cp -f tests/WitchDrawer.Core.Tests/bin/Debug/net10.0/runtimes/win-x64/native/winsqlite3.dll "$OUT/runtimes/win-x64/native/" 2>/dev/null || true

"/c/Program Files/dotnet/dotnet.exe" "$OUT/StartupPerf.dll" "$@"
