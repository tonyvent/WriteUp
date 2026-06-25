@echo off
REM Build WriteUp as a single, self-contained .exe (no .NET install needed to run it).
REM Output: app\WriteUp\bin\Release\net8.0-windows\win-x64\publish\WriteUp.exe
setlocal
cd /d "%~dp0WriteUp"

dotnet publish WriteUp.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false

echo.
echo ============================================================
echo Done. Copy this one file to your shared folder:
echo   app\WriteUp\bin\Release\net8.0-windows\win-x64\publish\WriteUp.exe
echo ============================================================
pause
