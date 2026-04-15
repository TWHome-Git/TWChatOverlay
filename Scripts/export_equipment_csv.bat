@echo off
chcp 65001 >nul
set "BASE=%~dp0"
powershell -ExecutionPolicy Bypass -File "%BASE%equipment_csv_export.ps1"
pause
