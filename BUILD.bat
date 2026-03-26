@echo off
:: BUILD.bat — Compiles ps1compiler.exe using the .NET Framework csc.exe
:: No Visual Studio or SDK needed. Just .NET Framework (comes with Windows).

setlocal

:: ── Find csc.exe ────────────────────────────────────────────────────────────
set CSC=

:: Try Framework64 first (v4.x, v3.x, v2.x)
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework64\v4*") do set CSC=%%D\csc.exe
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework64\v3*") do if not exist "%CSC%" set CSC=%%D\csc.exe
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework64\v2*") do if not exist "%CSC%" set CSC=%%D\csc.exe

:: Fallback to Framework (32-bit)
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework\v4*")   do if not exist "%CSC%" set CSC=%%D\csc.exe
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework\v3*")   do if not exist "%CSC%" set CSC=%%D\csc.exe
for /d %%D in ("%SystemRoot%\Microsoft.NET\Framework\v2*")   do if not exist "%CSC%" set CSC=%%D\csc.exe

if not exist "%CSC%" (
    echo ERROR: csc.exe not found. Make sure .NET Framework is installed.
    pause
    exit /b 1
)

echo Compiler : %CSC%
echo Building ps1compiler.exe ...

"%CSC%" /nologo /optimize+ /target:exe /out:ps1compiler.exe ps1compiler.cs

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo BUILD OK -- ps1compiler.exe is ready.
echo.
echo Usage examples:
echo   ps1compiler.exe myscript.ps1
echo   ps1compiler.exe myscript.ps1 --icon myapp.ico
echo   ps1compiler.exe myscript.ps1 output.exe --icon app.ico --manifest app.manifest
echo   ps1compiler.exe myscript.ps1 --no-hidden     (keeps terminal visible)
echo.
pause
