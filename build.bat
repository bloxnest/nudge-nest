@echo off
rem Builds NudgeNest.exe with the C# compiler that ships inside Windows (.NET Framework 4).
rem Nothing to install.
setlocal
cd /d "%~dp0"

rem a running copy locks the exe, so ask it to close first (this waits until it has)
if exist NudgeNest.exe ".\NudgeNest.exe" --exit

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Couldn't find the .NET Framework 4 C# compiler.
    exit /b 1
)

rem the window is WPF (like BloxNest): its layout and animations are in src\MainWindow.xaml
set "FW=%CSC:\csc.exe=%"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:NudgeNest.exe ^
    /win32manifest:app.manifest /win32icon:assets\icon.ico ^
    /resource:src\MainWindow.xaml,MainWindow.xaml ^
    /resource:assets\icon.ico,NudgeNest.icon.ico ^
    /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" ^
    src\*.cs
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)
echo Built NudgeNest.exe
