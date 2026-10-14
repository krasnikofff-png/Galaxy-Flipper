---
name: planet-trader-ship
description: Закончить шаг в Planet Trader — проверки, коммит, отправка в GitHub и копия в проект в Загрузках, который у автора открыт в Unity. Звать после каждого законченного изменения кода, интерфейса, моделей или баланса.
---

# Закончить шаг: проверить, закоммитить, отправить, разложить копию

Автор работает в Unity с копией проекта в `~/Downloads/Galaxy-Flipper-claude-economy-core` (не с репозиторием). Пока
туда не скопировать изменения, он их не увидит. Unity у него открыт и подхватывает файлы сам.

## 1. Проверки

```bash
export PATH=/opt/homebrew/bin:/usr/local/share/dotnet:$PATH
dotnet test                              # все тесты ядра
dotnet build tools/UnityCompileCheck     # скрипты Unity компилируются (без Unity)
```

- Изменился баланс или правила ядра — сначала навык `planet-trader-balance`.
- Изменился интерфейс или графика — посмотреть результат навыком `planet-trader-unity-check`, прежде чем говорить «готово».
- Новый модуль Unity (например, `ImageConversion`) не находится в проверке компиляции — добавить ссылку
  в `tools/UnityCompileCheck/UnityCompileCheck.csproj`, а не обходить.

## 2. Документы

Обновить то, что изменилось: `docs/ROADMAP.md` (что сделано), `docs/BALANCE.md` (числа и выводы симулятора),
`docs/art/README.md` (графика, ключи автоснимка), `CLAUDE.md` («Что дальше»), `README.md` (как играть).
Тексты — по-русски, простым языком.

## 3. Коммит и отправка

Ветка — `claude/economy-core`. Сообщение — по-английски, первая строка о сути, ниже — что и зачем.

```bash
git add -A && git commit -q -m "Короткая суть

Что изменилось и почему.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" && git push -q origin claude/economy-core
```

Перед `git add -A` — `git status --short`: в коммит не должны попасть `.mcp.json` (локальный, в `.git/info/exclude`),
`tools/portraits/.cache/` и сборки.

## 4. Копия в Загрузках

```bash
D=~/Downloads/Galaxy-Flipper-claude-economy-core
rsync -a unity/Assets/Scripts/ $D/unity/Assets/Scripts/
rsync -a unity/Assets/Resources/ $D/unity/Assets/Resources/
rsync -a unity/Assets/Editor/ $D/unity/Assets/Editor/
rsync -a --exclude '*.meta' src/PlanetTrader.Core/ $D/src/PlanetTrader.Core/
```

Если менялись только скрипты или только интерфейс, хватит соответствующей строки. `Packages/manifest.json` в копии
не трогать: там стоит пакет `com.unity.pipeline` для связи редактора с Claude.

## 5. Ответ автору

Коротко по-русски: что сделано, что проверено (тесты, снимки), какие цели симулятора не выполнены — честно.
Снимки из игры отправлять файлом (`SendUserFile`), если менялось видимое.
