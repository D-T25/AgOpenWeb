@echo off
cd /d "%~dp0"
echo Running offline implement draft tests. This can take a few minutes.
"%~dp0AgOpenWeb.PassiveHarness.exe" --draft --config "%~dp0Test-Settings.json" --output "%~dp0Results"
set "draft_status=%ERRORLEVEL%"
if "%draft_status%"=="2" goto error
if "%draft_status%"=="1" echo Some checks failed. Read the report; this is not a harness crash.
if exist "%~dp0Results\draft-simulation.html" start "" "%~dp0Results\draft-simulation.html"
goto done
:error
echo Configuration or runtime error. No new report was generated.
:done
pause
exit /b %draft_status%
