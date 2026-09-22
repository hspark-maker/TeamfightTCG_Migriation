@echo off
cd /d "%~dp0"
if not exist "node_modules\vite\bin\vite.js" (
  echo Run npm.cmd ci in this folder first.
  pause
  exit /b 1
)
echo Card Battle Dashboard: http://localhost:5173
call npm.cmd run dev
pause
