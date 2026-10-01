@echo off
rem Builds and runs the self-tests against stand-in Roblox windows (never the real game).
rem Takes about two minutes and moves focus and the mouse around while it runs.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /optimize+ /out:bin\RobloxPlayerBeta.exe /win32manifest:..\app.manifest FakeRoblox.cs || exit /b 1
copy /y bin\RobloxPlayerBeta.exe bin\OtherApp.exe >nul

"%CSC%" /nologo /target:exe /optimize+ /codepage:65001 /main:NudgeNest.SelfTest /out:bin\SelfTest.exe ^
    /win32manifest:..\app.manifest /win32icon:..\assets\icon.ico ^
    /resource:..\assets\logo-32.png,NudgeNest.logo-32.png ^
    /resource:..\assets\logo-64.png,NudgeNest.logo-64.png ^
    /resource:..\assets\logo-256.png,NudgeNest.logo-256.png ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    ..\src\*.cs SelfTest.cs || exit /b 1

rem no arguments = every test; or name the parts to run: keys settings log engine flicker window
bin\SelfTest.exe %*
