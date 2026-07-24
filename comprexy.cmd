@echo off
setlocal
REM Thin shim so Windows users can run: comprexy.cmd <command>
REM Without fighting PowerShell ExecutionPolicy for a repo-local .ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0comprexy.ps1" %*
exit /b %ERRORLEVEL%
