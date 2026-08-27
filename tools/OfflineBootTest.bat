@echo off
chcp 65001 >nul
setlocal

rem TeamfightTCG - online-only boot verification helper.
rem Blocks Unity.exe outbound so the editor goes offline while the PC stays online.
rem Same entry point as the Unity menu: Tools/Card Battle/offline boot test.
rem All logic lives in OfflineBootTest.ps1 - this file only elevates and forwards.
rem
rem   OfflineBootTest.bat on       block  (needs admin - self elevates)
rem   OfflineBootTest.bat off      unblock
rem   OfflineBootTest.bat status   show current state

set "ACTION=%~1"
if "%ACTION%"=="" set "ACTION=status"

if /i not "%ACTION%"=="on" if /i not "%ACTION%"=="off" if /i not "%ACTION%"=="status" (
    echo Usage: %~nx0 [on^|off^|status]
    exit /b 1
)

rem status is read-only, so it runs without elevation.
if /i "%ACTION%"=="status" goto run

net session >nul 2>&1
if %errorlevel% equ 0 goto run

echo Requesting administrator privileges...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%ACTION%' -Verb RunAs"
exit /b

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0OfflineBootTest.ps1" -Action %ACTION%
echo.
pause
