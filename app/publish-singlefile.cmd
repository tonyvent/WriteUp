@echo off
REM ============================================================================
REM Build WriteUp as ONE self-contained .exe (no .NET install, no DLLs needed).
REM Outputs to a clean folder so there's no confusion with the build output:
REM   app\WriteUp\publish\WriteUp.exe   <-- copy just this one file
REM ============================================================================
setlocal
set "PROJ=%~dp0WriteUp\WriteUp.csproj"
set "OUT=%~dp0WriteUp\publish"

echo Cleaning "%OUT%" ...
if exist "%OUT%" rmdir /s /q "%OUT%"

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o "%OUT%"

if errorlevel 1 (
  echo.
  echo *** Publish FAILED. See the error above. ***
  pause
  exit /b 1
)

echo.
echo === Files produced in "%OUT%" ===
dir /b "%OUT%"
echo.
echo If you see only WriteUp.exe ^(plus maybe a .pdb^), you're done:
echo copy "%OUT%\WriteUp.exe" to your shared folder.
pause
