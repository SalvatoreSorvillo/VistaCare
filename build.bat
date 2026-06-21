@echo off
rem Build VistaCare.exe using the C# compiler that ships with Windows (no install needed).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /out:VistaCare.exe /win32icon:VistaCare.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  VistaCare.cs
if errorlevel 1 ( echo BUILD FAILED & pause ) else ( echo Built VistaCare.exe )
