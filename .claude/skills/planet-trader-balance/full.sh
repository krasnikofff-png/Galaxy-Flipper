#!/bin/bash
# full.sh <папка> — полная проверка баланса: основной отчёт (в docs/sim), кооператив и большой сектор (в <папка>/coop, <папка>/big).
# Строки с «❌» из каждого отчёта печатает в конце. Большой сектор — самый долгий (пара минут).
DIR="$1"; [ -z "$DIR" ] && { echo "Использование: full.sh <папка>"; exit 2; }
mkdir -p "$DIR/coop" "$DIR/big"
cd /Users/davidkrasnikov/galaxy-flipper || exit 1
export PATH=/opt/homebrew/bin:/usr/local/share/dotnet:$PATH
dotnet build src/PlanetTrader.Sim -c Release 2>&1 | grep -E " error |Error\(s\)"
SIM=(dotnet run --project src/PlanetTrader.Sim -c Release --no-build --)
"${SIM[@]}" > "$DIR/main.txt" 2>&1 &
"${SIM[@]}" --runs 10 --players 3 --set SectorPlanets=90 --set MaxSectorPlanets=150 --out "$DIR/coop" > /dev/null 2>&1 &
"${SIM[@]}" --runs 10 --set SectorPlanets=300 --set MaxSectorPlanets=500 --out "$DIR/big" > /dev/null 2>&1 &
wait
for f in docs/sim/report.md "$DIR/coop/report.md" "$DIR/big/report.md"; do
    echo "== $f"
    [ -f "$f" ] && grep -E "^\| (Пассивный|Активный) \||❌" "$f" | cut -c1-220
done
