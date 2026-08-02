@echo off
setlocal enabledelayedexpansion

echo ====================================================================
echo   Building Windows Secure Browser (Single-File Portable EXE .NET 9)
echo ====================================================================

:: 1. Check .NET SDK
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK is not installed or not found in PATH!
    echo Please install .NET 9 SDK from https://dotnet.microsoft.com/download
    exit /b 1
)

:: 2. Close any running AntiRecorder instances to prevent file lock
powershell -Command "Stop-Process -Name 'AntiRecorder' -Force -ErrorAction SilentlyContinue" >nul 2>&1
powershell -Command "Stop-Process -Name 'SecureBrowser' -Force -ErrorAction SilentlyContinue" >nul 2>&1

:: 3. Set Paths (Output directly to BuildOutput root)
set PROJ_DIR=%~dp0..\WindowsSecureBrowser
set OUTPUT_DIR=%~dp0..\BuildOutput

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo [1/3] Restoring NuGet Packages...
cd /d "%PROJ_DIR%"
dotnet restore WindowsSecureBrowser.csproj
if %errorlevel% neq 0 (
    echo [ERROR] NuGet restore failed!
    exit /b 1
)

echo [2/3] Publishing Standalone 1-File AntiRecorder.exe to BuildOutput...
dotnet publish WindowsSecureBrowser.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUTPUT_DIR%"
if %errorlevel% neq 0 (
    echo [ERROR] dotnet publish failed!
    exit /b 1
)

echo [3/3] Finalizing Clean Package Output...
del /q "%OUTPUT_DIR%\*.xml" >nul 2>&1
del /q "%OUTPUT_DIR%\*.pdb" >nul 2>&1
if exist "%OUTPUT_DIR%\SecureBrowser.exe" del /f /q "%OUTPUT_DIR%\SecureBrowser.exe" >nul 2>&1
copy /y "%PROJ_DIR%\app_icon.ico" "%OUTPUT_DIR%\app_icon.ico" >nul 2>&1

if exist "%OUTPUT_DIR%\AntiRecorder.exe" (
    echo ====================================================================
    echo [SUCCESS] Windows AntiRecorder Portable Executable Built Successfully!
    echo Output File: %OUTPUT_DIR%\AntiRecorder.exe
    echo ====================================================================
) else (
    echo [ERROR] Could not locate output AntiRecorder.exe file!
    exit /b 1
)

endlocal
pause
