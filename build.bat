@echo off
rem Builds bin\ArrowOverlay.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find the .NET Framework 4 C# compiler ^(csc.exe^).
    exit /b 1
)

if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /optimize+ /out:bin\ArrowOverlay.exe /win32manifest:app.manifest ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 exit /b 1
echo Built bin\ArrowOverlay.exe
