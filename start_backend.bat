@echo off
chcp 65001 > nul
echo ========================================================
echo  Запуск Telegram Homework Bot ^& Mini App Backend...
echo ========================================================
echo.
echo Адрес сайта/Mini App:  http://localhost:5000/
echo Адрес документации API: http://localhost:5000/swagger
echo.
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5000;https://localhost:5001
rem Bot does not receive messages locally: otherwise it steals them from the deployed bot.
rem To test bot commands locally: set BOT_UPDATES_MODE=polling before running this script.
if "%BOT_UPDATES_MODE%"=="" set BOT_UPDATES_MODE=off
echo Режим бота: %BOT_UPDATES_MODE%
echo.
cd /d "%~dp0Backend"
dotnet run
pause
