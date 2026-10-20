#!/bin/bash
# shot.sh <путь.png> [ключи автоснимка…] — снимок собранной игры 1600×900.
# Без -autoShotDelay ждёт 4 секунды: планеты пекутся на видеокарте мгновенно, интерфейсу нужно собраться.
OUT="$1"; shift
[ -z "$OUT" ] && { echo "Использование: shot.sh <путь.png> [ключи]"; exit 2; }
APP=/Users/davidkrasnikov/galaxy-flipper/unity/Build/PlanetTrader.app/Contents/MacOS/unity
DELAY=(); [[ " $* " == *" -autoShotDelay "* ]] || DELAY=(-autoShotDelay 4)
rm -f "$OUT"
"$APP" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -autoShot "$OUT" "${DELAY[@]}" "$@" > /dev/null 2>&1 &
for _ in $(seq 1 30); do [ -f "$OUT" ] && break; sleep 2; done
sleep 2
if [ -f "$OUT" ]; then echo "Снимок: $OUT"; else echo "Снимка нет — см. ~/Library/Logs/DefaultCompany/unity/Player.log"; exit 1; fi
