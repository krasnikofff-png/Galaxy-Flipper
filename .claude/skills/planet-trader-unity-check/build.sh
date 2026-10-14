#!/bin/bash
# Собрать игру для macOS в unity/Build/PlanetTrader.app и коротко сказать, получилось ли.
# Лог сборки — в $TMPDIR/planet-trader-build.log (или в первом аргументе).
LOG="${1:-${TMPDIR:-/tmp}/planet-trader-build.log}"
unity run /Users/davidkrasnikov/galaxy-flipper/unity -- \
    -executeMethod PlanetTrader.EditorTools.BuildTools.BuildMac -buildOut Build/PlanetTrader.app -logFile "$LOG" > /dev/null 2>&1
grep -E "Сборка:|error CS" "$LOG" | head -10
grep -q "Сборка: Succeeded" "$LOG"
