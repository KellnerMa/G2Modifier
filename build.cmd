@echo off
rem Baut G2Modifier.exe mit dem in Windows enthaltenen .NET-Framework-Compiler. Keine Installation noetig.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /codepage:65001 /out:G2Modifier.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:lang\en.txt,G2Modifier.lang.en.txt /resource:lang\fr.txt,G2Modifier.lang.fr.txt /resource:lang\de.txt,G2Modifier.lang.de.txt /recurse:src\*.cs
if errorlevel 1 (echo Build fehlgeschlagen & pause & exit /b 1)
echo Fertig: G2Modifier.exe
pause
