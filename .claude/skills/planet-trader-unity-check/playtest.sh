#!/bin/bash
# playtest.sh [версия] — сборки для плейтеста под Mac и Windows с памяткой docs/playtest.md («Как играть.txt»):
# unity/Build/Playtest/PlanetTrader-mac-<дата>.zip и PlanetTrader-windows-<дата>.zip.
# Настройки проекта, которые Unity переписывает при сборке, откатываются. Windows — нужен модуль windows-mono.
DATE=$(date +%Y-%m-%d)
VERSION="${1:-0.9-playtest-$DATE}"
REPO=/Users/davidkrasnikov/galaxy-flipper
LOG="${TMPDIR:-/tmp}/planet-trader-playtest"
cd "$REPO" || exit 1
rm -rf unity/Build/Playtest
build() {  # $1 — mac или windows; редактор сразу открывается под нужную платформу
    local target=OSXUniversal; [ "$1" = windows ] && target=Win64
    unity run "$REPO/unity" -- -buildTarget "$target" -executeMethod PlanetTrader.EditorTools.BuildTools.BuildPlaytest -buildVersion "$VERSION" -playtestOs "$1" -logFile "$LOG-$1.log" > /dev/null 2>&1
    git checkout -q unity/ProjectSettings/ProjectSettings.asset
    grep -E "Сборка:|error CS" "$LOG-$1.log" | head -5
    grep -q "Сборка: Succeeded" "$LOG-$1.log"
}
NAME="Planet Trader — плейтест $DATE"
build mac || exit 1
mkdir -p "unity/Build/Playtest/mac/$NAME"
mv "unity/Build/Playtest/Planet Trader.app" "unity/Build/Playtest/mac/$NAME/"
cp docs/playtest.md "unity/Build/Playtest/mac/$NAME/Как играть.txt"
( cd unity/Build/Playtest/mac && ditto -c -k --sequesterRsrc --keepParent "$NAME" "../PlanetTrader-mac-$DATE.zip" )
if build windows; then
    mkdir -p "unity/Build/Playtest/win/$NAME"
    # Папка отладочных символов игрокам не нужна.
    rm -rf unity/Build/Playtest/Windows/*_BurstDebugInformation_DoNotShip
    mv unity/Build/Playtest/Windows/* "unity/Build/Playtest/win/$NAME/"
    cp docs/playtest.md "unity/Build/Playtest/win/$NAME/Как играть.txt"
    ( cd unity/Build/Playtest/win && zip -qrX "../PlanetTrader-windows-$DATE.zip" "$NAME" )
fi
ls -la unity/Build/Playtest/*.zip
