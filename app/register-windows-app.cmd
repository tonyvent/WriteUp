@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0register-windows-app.ps1"
if errorlevel 1 pause
