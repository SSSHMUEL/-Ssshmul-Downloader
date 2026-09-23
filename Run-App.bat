@echo off
cd /d "%~dp0"
if exist "%~dp0SsshmulDownloader\bin\Release\net8.0-windows\SsshmulDownloader.exe" (
    start "" "%~dp0SsshmulDownloader\bin\Release\net8.0-windows\SsshmulDownloader.exe"
) else (
    start "" "%~dp0SsshmulDownloader\bin\Debug\net8.0-windows\SsshmulDownloader.exe"
)
