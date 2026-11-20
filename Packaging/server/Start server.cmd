@echo off
rem Starts the Steambirds Alliance Revive server from this folder: it reads its maps, settings and accounts from here.
rem Stop it with Ctrl+C (or by closing this window).
title Steambirds server
cd /d "%~dp0"
if not exist "data\units_config.txt" (
  echo Game data missing: run SteambirdsGameFiles.exe once ^(it takes the data from your copy of the game^).
  echo Without it the server still starts, but enemies, bosses and tutorial walls need that data.
  echo.
)
"%~dp0SteambirdsServer.exe"
echo.
echo The server stopped.
pause
