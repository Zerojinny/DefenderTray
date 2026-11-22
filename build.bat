@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
cd /d "%~dp0"
set REFS=/r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll

rem Pass 1: build a temp exe without manifest, use it to generate app.ico (no UAC)
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu %REFS% /out:"%TEMP%\dftmp_icongen.exe" /recurse:src\*.cs
if errorlevel 1 (echo Build failed - icon gen & exit /b 1)
"%TEMP%\dftmp_icongen.exe" --make-icon "%~dp0app.ico"
del "%TEMP%\dftmp_icongen.exe" >nul 2>&1

rem Pass 2: final exe with embedded icon and admin manifest
set ICONOPT=
if exist "%~dp0app.ico" set ICONOPT=/win32icon:app.ico
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /platform:anycpu /win32manifest:app.manifest %ICONOPT% %REFS% /out:DefenderTray.exe /recurse:src\*.cs
if errorlevel 1 (echo Build failed & exit /b 1)
echo Build OK: %~dp0DefenderTray.exe
