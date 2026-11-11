#!/usr/bin/env bash
# Start the backend + Mini App locally (macOS / Linux).
set -euo pipefail

echo "========================================================"
echo " Запуск Telegram Homework Bot & Mini App Backend..."
echo "========================================================"
echo
echo "Адрес сайта/Mini App:   http://localhost:5000/"
echo "Документация API:       http://localhost:5000/swagger"
echo

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:5000"
# Bot does not receive messages locally: otherwise it steals them from the deployed bot.
# To test bot commands locally: BOT_UPDATES_MODE=polling ./start_backend.sh
export BOT_UPDATES_MODE="${BOT_UPDATES_MODE:-off}"
# The project targets .NET 8 but runs on newer installed runtimes too
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-LatestMajor}"
echo "Режим бота: $BOT_UPDATES_MODE"
echo

cd "$(dirname "$0")/Backend"
exec dotnet run
