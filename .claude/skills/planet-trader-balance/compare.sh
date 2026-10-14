#!/bin/bash
# compare.sh <папка> <имя> <партий> "<стиль;стиль>" [--set Поле=значение …]
# Быстрое сравнение стилей бота: медианы капитала и отношение к первому стилю. Вывод — в <папка>/<имя>.txt и на экран.
DIR="$1"; NAME="$2"; RUNS="$3"; STYLES="$4"; shift 4
mkdir -p "$DIR"
cd /Users/davidkrasnikov/galaxy-flipper || exit 1
export PATH=/opt/homebrew/bin:/usr/local/share/dotnet:$PATH
dotnet run --project src/PlanetTrader.Sim -c Release --no-build -- --runs "$RUNS" --compare "$STYLES" "$@" > "$DIR/$NAME.txt" 2>&1
echo "== $NAME"; tail -4 "$DIR/$NAME.txt"
