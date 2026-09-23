@echo off
where python >nul 2>&1
if %errorlevel% equ 0 (
    python "%~dp0nf_host.py" %*
) else (
    if exist "%~dp0..\..\Run-App.bat" (
        start "" "%~dp0..\..\Run-App.bat"
    ) else if exist "%~dp0..\..\SsshmulDownloader\bin\Release\net8.0-windows\SsshmulDownloader.exe" (
        start "" "%~dp0..\..\SsshmulDownloader\bin\Release\net8.0-windows\SsshmulDownloader.exe"
    ) else if exist "%~dp0..\..\SsshmulDownloader\bin\Debug\net8.0-windows\SsshmulDownloader.exe" (
        start "" "%~dp0..\..\SsshmulDownloader\bin\Debug\net8.0-windows\SsshmulDownloader.exe"
    )
)
