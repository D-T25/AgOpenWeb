@echo off
cd /d "%~dp0"
echo Running offline passive guidance tests. No tractor hardware is used.
"%~dp0AgOpenWeb.PassiveHarness.exe" --output "%~dp0Results"
set "harness_exit=%ERRORLEVEL%"
if "%harness_exit%"=="0" (echo All checks passed.) else (echo Some checks failed. Review the report for details.)
if exist "%~dp0Results\report.html" start "" "%~dp0Results\report.html"
pause
exit /b %harness_exit%
