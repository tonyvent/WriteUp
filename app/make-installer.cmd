@echo off
REM ============================================================================
REM Build the WriteUp INSTALLER in one go:
REM   1) publishes the single self-contained WriteUp.exe (same as
REM      publish-singlefile.cmd)
REM   2) compiles the Inno Setup installer around it
REM Output:  app\installer\output\WriteUp-Setup-<version>.exe
REM
REM One-time prerequisite: install Inno Setup 6 (free)
REM   https://jrsoftware.org/isdl.php   (default options are fine)
REM ============================================================================
setlocal
set "PROJ=%~dp0WriteUp\WriteUp.csproj"
set "OUT=%~dp0WriteUp\publish"
set "ISS=%~dp0installer\WriteUp.iss"

REM ---- find the Inno Setup compiler ------------------------------------------
set "ISCC="
where ISCC >nul 2>nul && set "ISCC=ISCC"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo.
  echo *** Inno Setup 6 was not found. ***
  echo Install it once from https://jrsoftware.org/isdl.php and run this again.
  pause
  exit /b 1
)

REM ---- read the app version from the csproj -----------------------------------
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Select-Xml -Path '%PROJ%' -XPath '//Version').Node.InnerText"`) do set "VER=%%v"
if not defined VER set "VER=0.0.0"
echo Building WriteUp %VER% ...

REM ---- 1) publish the single-file exe ------------------------------------------
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

REM ---- 2) compile the installer -------------------------------------------------
"%ISCC%" /DAppVersion=%VER% "%ISS%"
if errorlevel 1 (
  echo.
  echo *** Installer build FAILED. See the error above. ***
  pause
  exit /b 1
)

echo.
echo ================================================================
echo  Done: %~dp0installer\output\WriteUp-Setup-%VER%.exe
echo  Share that one file - people double-click it to install.
echo ================================================================
pause
