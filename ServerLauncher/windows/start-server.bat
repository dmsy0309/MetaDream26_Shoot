@echo off
rem Metadream Shoot dedicated server launcher (Windows x64)
rem   start-server.bat                       uses server.json in this folder
rem   start-server.bat -teamsize 2 -port 7800
setlocal
cd /d "%~dp0"
if "%METADREAM_CONFIG%"=="" set "METADREAM_CONFIG=server.json"
title Metadream Shoot Server
MetadreamShootServer.exe -batchmode -nographics -logFile - -server -config "%METADREAM_CONFIG%" %*
echo.
echo Server stopped (exit code %ERRORLEVEL%).
pause
endlocal
