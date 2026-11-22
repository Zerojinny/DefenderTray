@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
cd /d "%~dp0"
if not exist tests\bin mkdir tests\bin

rem Build app sources + tests into a console exe (no admin manifest) and run it
"%CSC%" /nologo /codepage:65001 /target:exe /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /main:DefenderTray.Tests.TestMain /out:tests\bin\Tests.exe /recurse:src\*.cs tests\*.cs
if errorlevel 1 (echo Test build failed & exit /b 1)
tests\bin\Tests.exe
exit /b %errorlevel%
