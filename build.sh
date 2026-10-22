#!/bin/sh
# Builds AnotherDLSSTool.exe with Mono's C# compiler, then the installer with NSIS.
# Needs: mono-mcs (or mono-devel) and nsis. Works on Linux, macOS and Windows (Mono for Windows).
set -e
cd "$(dirname "$0")"
mkdir -p out
mcs -sdk:4.5 -target:winexe -optimize+ -out:out/AnotherDLSSTool.exe -win32icon:app.ico -resource:logo.png,logo.png \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  -r:System.Runtime.Serialization.dll -r:System.Security.dll -r:System.Core.dll -r:System.Web.Extensions.dll src/*.cs
cp out/AnotherDLSSTool.exe stage/
if [ -f stage/files/nvngx_dlss.dll ] && [ -f stage/files/nvngx_dlssnr.dll ] && [ "$1" = "--bundle-dlls" ]; then
  # Personal build with NVIDIA's files inside. Don't publish this one.
  makensis -V2 -DBUNDLE_DLLS installer.nsi
else
  makensis -V2 installer.nsi
fi
echo "Built out/Another-DLSS-Tool-Installer.exe"
