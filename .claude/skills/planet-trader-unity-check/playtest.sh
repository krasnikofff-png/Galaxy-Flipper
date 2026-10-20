#!/bin/bash
# playtest.sh [версия] — сборка для плейтеста: «Planet Trader.app» и памятка docs/playtest.md («Как играть.txt»)
# в архиве unity/Build/Playtest/PlanetTrader-mac-<дата>.zip. Настройки проекта, которые Unity переписывает при сборке, откатываются.
DATE=$(date +%Y-%m-%d)
VERSION="${1:-0.9-playtest-$DATE}"
REPO=/Users/davidkrasnikov/galaxy-flipper
LOG="${TMPDIR:-/tmp}/planet-trader-playtest.log"
cd "$REPO" || exit 1
rm -rf unity/Build/Playtest
unity run "$REPO/unity" -- -executeMethod PlanetTrader.EditorTools.BuildTools.BuildPlaytest -buildVersion "$VERSION" -logFile "$LOG" > /dev/null 2>&1
git checkout -q unity/ProjectSettings/ProjectSettings.asset
grep -E "Сборка:|error CS" "$LOG" | head -5
grep -q "Сборка: Succeeded" "$LOG" || exit 1
DIR="unity/Build/Playtest/Planet Trader — плейтест $DATE"
mkdir -p "$DIR"
mv "unity/Build/Playtest/Planet Trader.app" "$DIR/"
cp docs/playtest.md "$DIR/Как играть.txt"
ZIP="unity/Build/Playtest/PlanetTrader-mac-$DATE.zip"
( cd unity/Build/Playtest && ditto -c -k --sequesterRsrc --keepParent "$(basename "$DIR")" "$(basename "$ZIP")" )
echo "Архив: $REPO/$ZIP ($(du -h "$ZIP" | cut -f1))"
