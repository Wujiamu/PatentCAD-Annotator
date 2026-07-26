@echo off
REM PatentMarker 2007 Installer (BAT fallback)
REM Pure ASCII encoding
REM
REM Usage: Place in same folder as PatentMarker.dll, double-click to run
REM Note: This BAT uses a fixed product code. If auto-detection is needed,
REM       use install-2007.vbs instead.

setlocal enabledelayedexpansion

REM Get script directory
set "SCRIPT_DIR=%~dp0"
set "DLL_PATH=%SCRIPT_DIR%PatentMarker.dll"

REM Check DLL exists
if not exist "%DLL_PATH%" (
    echo ERROR: PatentMarker.dll not found in: %SCRIPT_DIR%
    echo Place this script in the same folder as PatentMarker.dll
    pause
    exit /b 1
)

REM Try common AutoCAD 2007 product codes
set "REG_BASE=HKCU\Software\Autodesk\AutoCAD\R17.0"
set "FOUND=0"

REM Chinese version
reg query "%REG_BASE%\ACAD-5001:804" >nul 2>&1
if %errorlevel%==0 (
    call :install "ACAD-5001:804"
    set "FOUND=1"
)

REM English version
reg query "%REG_BASE%\ACAD-5001:409" >nul 2>&1
if %errorlevel%==0 (
    call :install "ACAD-5001:409"
    set "FOUND=1"
)

if "%FOUND%"=="0" (
    echo ERROR: No AutoCAD 2007 product code found under:
    echo   %REG_BASE%
    echo Please use install-2007.vbs for auto-detection, or
    echo check registry manually with regedit.
    pause
    exit /b 1
)

echo.
echo === Installation Complete ===
echo DLL: %DLL_PATH%
echo.
echo Restart AutoCAD 2007 to auto-load.
echo If auto-load fails, use NETLOAD command:
echo   %DLL_PATH%
echo.
echo Commands: BZ(palette) BZM(mark) BZC(check) BZA(align) BZS(select)
pause
exit /b 0

:install
set "APP_KEY=%REG_BASE%\%~1\Applications\PatentMarker"
reg add "%APP_KEY%" /ve /f >nul 2>&1
reg add "%APP_KEY%" /v DESCRIPTION /t REG_SZ /d "PatentMarker - Patent Drawing Annotation Plugin" /f >nul
reg add "%APP_KEY%" /v LOADCTRLS /t REG_DWORD /d 14 /f >nul
reg add "%APP_KEY%" /v MANAGED /t REG_DWORD /d 1 /f >nul
reg add "%APP_KEY%" /v LOADER /t REG_SZ /d "%DLL_PATH%" /f >nul
echo Installed for: %~1
goto :eof
