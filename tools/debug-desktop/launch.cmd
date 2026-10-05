@echo off
rem Launches the desktop app with the WebView2 debug port open, de-elevated,
rem so an agent shell (often elevated) can still attach. Run through explorer.exe
rem when the current shell is elevated, for example:
rem   explorer.exe "C:\path\to\tools\debug-desktop\launch.cmd"
rem Usage: launch.cmd [port]
setlocal
set "PORT=%~1"
if "%PORT%"=="" set "PORT=22600"
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=%PORT%"
set "ROOT=%~dp0..\.."
set "EXE=%ROOT%\IELTop.Desktop\bin\Debug\net10.0-windows10.0.19041.0\win-x64\IELTop.Desktop.exe"
if not exist "%EXE%" (
  echo Build first: dotnet build IELTop.slnx
  exit /b 1
)
start "" "%EXE%"
endlocal
