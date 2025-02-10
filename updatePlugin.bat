@echo off
:: BatchGotAdmin
:-------------------------------------
@REM Check for permissions
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"

@REM If error flag set, we do not have admin.
if '%errorlevel%' NEQ '0' (
    echo Requesting administrative privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    set params = %*:"=""
    echo UAC.ShellExecute "cmd.exe", "/c %~s0 %params%", "", "runas", 1 >> "%temp%\getadmin.vbs"

    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    pushd "%CD%"
    CD /D "%~dp0"
:--------------------------------------
:: CheckProgramRunning
@echo off
set "program=PowerToys.exe"

echo Checking if %program% is running...
tasklist /FI "IMAGENAME eq %program%" | find /I "%program%" >nul 2>&1

if '%errorlevel%' NEQ '0' (
    echo %program% is not running ^(already killed or never started^).
) else (
    echo Terminating PowerToys processes...
    taskkill /F /T /IM PowerToys.exe
    echo Done.
)

:: CopyLatestFiles
echo Updating plugin...
xcopy ".\Community.PowerToys.Run.Plugin.MyGOPowerToys\bin\x64\Debug\net9.0-windows10.0.22621.0" "C:\Program Files\PowerToys\RunPlugins\MyGOPowerToys" /E /H /C /I /Y /Q >nul 2>&1
echo Done.

timeout /t 3 /nobreak

start "" "C:\Program Files\PowerToys\PowerToys.exe"