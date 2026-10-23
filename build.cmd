@echo off
rem Baut G2Modifier.exe mit dem in Windows enthaltenen .NET-Framework-Compiler. Keine Installation noetig.
rem Builds G2Modifier.exe with the C# compiler that ships with Windows. Nothing to install.
cd /d "%~dp0"
if not exist "src\Program.cs" (
  echo Ordner "src" fehlt neben build.cmd. Bitte das ganze Repository herunterladen ^(Code - Download ZIP^) und entpacken.
  echo Folder "src" is missing next to build.cmd. Download the whole repository ^(Code - Download ZIP^) and extract it.
  echo Eine fertige G2Modifier.exe liegt im Ordner "release". / A ready-made G2Modifier.exe is in the "release" folder.
  pause & exit /b 1
)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /codepage:65001 /out:G2Modifier.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:lang\en.txt,G2Modifier.lang.en.txt /resource:lang\fr.txt,G2Modifier.lang.fr.txt /resource:lang\de.txt,G2Modifier.lang.de.txt /recurse:src\*.cs
if errorlevel 1 (echo Build fehlgeschlagen / Build failed & pause & exit /b 1)
echo Fertig / Done: G2Modifier.exe
pause
