using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Сохранения: «Сохранить» и «Загрузить» в меню, автосохранение каждые несколько ходов и при выходе,
    /// «Продолжить партию» на экране выбора. Файлы — в папке saves рядом с настройками игры.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        /// <summary>Автосохранение — раз в столько ходов (и при выходе из игры).</summary>
        const int AutosaveEveryTurns = 5;
        const string AutosaveName = "autosave";

        /// <summary>Файл сохранения: сведения для списка, сама партия (ядро) и то, что видно на экране, — журнал и графики цен.</summary>
        [Serializable]
        sealed class SaveFile
        {
            public int Version = 1;
            public string Title;
            public int Turn;
            public string Character;
            public double Capital;
            public long SavedAt;
            public string Game;
            public List<SavedLog> Log = new List<SavedLog>();
            public List<SavedPrices> Prices = new List<SavedPrices>();
        }

        [Serializable]
        sealed class SavedLog
        {
            public int Turn;
            public string Text, Kind;
        }

        [Serializable]
        sealed class SavedPrices
        {
            public string Resource;
            public List<double> Values = new List<double>();
        }

        /// <summary>Идёт настоящая партия (а не та, что за экраном выбора при запуске) — её есть смысл сохранять.</summary>
        bool _playing;

        VisualElement _saves;
        ScrollView _savesList;
        Button _continueButton, _startLoadButton;

        static string SavesDir => Path.Combine(Application.persistentDataPath, "saves");
        static string SavePath(string name) => Path.Combine(SavesDir, name + ".json");

        void CreateSaves(VisualElement root)
        {
            _saves = root.Q("saves");
            _savesList = root.Q<ScrollView>("savesList");
            root.Q<Button>("savesClose").clicked += () => _saves.AddToClassList("hidden");
            _saves.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _saves) _saves.AddToClassList("hidden");
            });
            root.Q<Button>("saveButton").clicked += () =>
            {
                _menu.AddToClassList("hidden");
                Later(SaveByHand);
            };
            root.Q<Button>("loadButton").clicked += () =>
            {
                _menu.AddToClassList("hidden");
                Later(OpenSaves);
            };
            _continueButton = root.Q<Button>("continueButton");
            _continueButton.clicked += () => Later(() => LoadFrom(AutosaveName));
            _startLoadButton = root.Q<Button>("startLoadButton");
            _startLoadButton.clicked += () => Later(OpenSaves);
        }

        /// <summary>Кнопки «Продолжить» и «Загрузить» на экране выбора — если есть что загружать.</summary>
        void RefreshStartSaves(bool atLaunch)
        {
            var auto = atLaunch ? ReadHeader(AutosaveName) : null;
            _continueButton.EnableInClassList("hidden", auto == null);
            if (auto != null) _continueButton.text = $"Продолжить партию — ход {auto.Turn}";
            _startLoadButton.EnableInClassList("hidden", !SaveNames().Any());
        }

        // ------------------------------------------------------------------
        // Запись
        // ------------------------------------------------------------------

        void SaveByHand()
        {
            if (!_playing || _game.Bankrupt) return;
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (Write(name)) Log($"Партия сохранена: ход {_game.Turn}.");
        }

        /// <summary>Автосохранение после хода: раз в несколько ходов.</summary>
        void AutosaveAfterTurn()
        {
            if (_game.Turn % AutosaveEveryTurns == 0) Autosave();
        }

        void Autosave()
        {
            if (!_playing) return;
            // После банкротства продолжать нечего: старое автосохранение тоже ни к чему.
            if (_game.Bankrupt)
            {
                TryDelete(AutosaveName);
                return;
            }
            Write(AutosaveName);
        }

        void OnApplicationQuit()
        {
            Autosave();
            ClosePlaytestLog();
        }

        bool Write(string name)
        {
            try
            {
                var me = _game.Corporations[0];
                var file = new SaveFile
                {
                    Title = $"{me.Character?.Name ?? "Директор"}, ход {_game.Turn}",
                    Turn = _game.Turn,
                    Character = me.Character?.Name,
                    Capital = _game.Capital,
                    SavedAt = DateTime.Now.Ticks,
                    Game = _game.Save(),
                    Log = _logEntries.Select(e => new SavedLog { Turn = e.Turn, Text = e.Text, Kind = e.Kind }).ToList(),
                    Prices = _priceHistory.Select(kv => new SavedPrices { Resource = kv.Key.ToString(), Values = kv.Value.ToList() }).ToList(),
                };
                Directory.CreateDirectory(SavesDir);
                // Сначала во временный файл, потом подменить: оборванная запись не испортит прежнее сохранение.
                string path = SavePath(name), temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(file));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("Не удалось сохранить партию: " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        static IEnumerable<string> SaveNames() =>
            Directory.Exists(SavesDir)
                ? Directory.GetFiles(SavesDir, "*.json").Select(Path.GetFileNameWithoutExtension)
                : Enumerable.Empty<string>();

        /// <summary>Сведения о сохранении для списка; null — файла нет или он испорчен.</summary>
        static SaveFile ReadHeader(string name)
        {
            try
            {
                string path = SavePath(name);
                return File.Exists(path) ? JsonUtility.FromJson<SaveFile>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        void LoadFrom(string name)
        {
            var file = ReadHeader(name);
            if (file == null || string.IsNullOrEmpty(file.Game))
            {
                Log("Сохранение не читается: файл испорчен.");
                return;
            }
            Core.Game game;
            try
            {
                game = Core.Game.Load(file.Game, new Balance());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log(e.Message);
                return;
            }

            _game = game;
            _playing = true;
            StartPlaytestLog("загружено сохранение");
            _lookSeed = (int)(_game.Seed % int.MaxValue);
            _rivalsTurnStarted = -1;
            _selectedId = _game.PlayerPlanets.Select(p => p.Id).DefaultIfEmpty(-1).First();
            _tab = Tab.Planet;
            _probeMode = false;
            ResetScreen();
            _start.AddToClassList("hidden");
            _saves.AddToClassList("hidden");

            // Журнал и графики цен — как были при сохранении.
            _logEntries.Clear();
            _logEntries.AddRange(file.Log.Select(e => new LogEntry { Turn = e.Turn, Text = e.Text, Kind = e.Kind }));
            foreach (var row in file.Prices)
                if (Enum.TryParse(row.Resource, out Resource r) && row.Values.Count > 0)
                    _priceHistory[r] = row.Values.ToList();
            var selected = Selected;
            if (selected != null) ShowSector(selected.Sector);
            Log($"Партия загружена: ход {_game.Turn}.");
            if (_game.Incident != null) QueuePopup(OpenIncident);
            MarkDirty();
        }

        // ------------------------------------------------------------------
        // Окно со списком сохранений
        // ------------------------------------------------------------------

        void OpenSaves()
        {
            _savesList.Clear();
            var files = SaveNames()
                .Select(n => (Name: n, File: ReadHeader(n)))
                .Where(x => x.File != null)
                .OrderByDescending(x => x.Name == AutosaveName)
                .ThenByDescending(x => x.File.SavedAt)
                .ToList();
            if (files.Count == 0)
                _savesList.Add(new Label("Сохранений пока нет. «Сохранить» — в меню под логотипом; раз в пять ходов игра сохраняется сама.").WithClass("small"));
            foreach (var (name, file) in files)
                _savesList.Add(SaveRow(name, file));
            _saves.RemoveFromClassList("hidden");
        }

        VisualElement SaveRow(string name, SaveFile file)
        {
            var row = new VisualElement();
            row.AddToClassList("save-row");
            var text = new VisualElement();
            text.AddToClassList("save-text");
            string title = name == AutosaveName ? "Автосохранение" : file.Character ?? "Партия";
            text.Add(new Label($"{title} · ход {file.Turn}").WithClass("save-title"));
            text.Add(new Label($"Капитал {Fmt.Money(file.Capital)} · {When(new DateTime(file.SavedAt))}").WithClass("small"));
            row.Add(text);
            row.Add(new VisualElement().WithClass("spacer"));

            var load = new Button(() => Later(() => LoadFrom(name))) { text = "Загрузить", focusable = false };
            load.AddToClassList("button");
            row.Add(load);
            var delete = new Button { focusable = false, tooltip = "Удалить сохранение" };
            delete.AddToClassList("small-button");
            delete.Add(Icons.Make("close", "icon"));
            delete.clicked += () => Later(() =>
            {
                TryDelete(name);
                OpenSaves();
                if (StartOpen) RefreshStartSaves(!_playing);
            });
            row.Add(delete);
            return row;
        }

        /// <summary>Для автоснимка: сыграть turns пустых ходов (с автосохранением), сохранить вручную и открыть список.</summary>
        internal void SavesForShot(int turns) => Later(() =>
        {
            for (int i = 0; i < turns; i++)
            {
                EndTurn();
                RecordPrices();
                AutosaveAfterTurn();
            }
            SaveByHand();
            OpenSaves();
        });

        /// <summary>Для автоснимка: «Продолжить партию» с экрана выбора.</summary>
        internal void ContinueForShot() => Later(() => LoadFrom(AutosaveName));

        static readonly string[] Months =
            { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

        /// <summary>«14 октября, 23:43» — по-русски при любом языке системы.</summary>
        static string When(DateTime t) => $"{t.Day} {Months[t.Month - 1]}, {t:HH:mm}";

        static void TryDelete(string name)
        {
            try
            {
                if (File.Exists(SavePath(name))) File.Delete(SavePath(name));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
