using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PlanetTrader.Core;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Журнал плейтеста: что происходило в партии — по ходам и событиям, со временем — в текстовый файл.
    /// Тестер присылает его вместе с отзывом; кнопка «Логи для отзыва» в меню открывает папку.
    /// По времени на ход видно, где игроку стало скучно или непонятно.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        static string PlaytestDir => Path.Combine(Application.persistentDataPath, "playtest");
        static readonly Regex RichTags = new Regex("<[^>]+>");

        StreamWriter _playLog;
        float _sessionStart, _turnStart;

        /// <summary>Новый файл журнала: при начале партии и при загрузке.</summary>
        void StartPlaytestLog(string how)
        {
            try
            {
                ClosePlaytestLog();
                Directory.CreateDirectory(PlaytestDir);
                string path = Path.Combine(PlaytestDir, $"session-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
                _playLog = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                _sessionStart = _turnStart = Time.realtimeSinceStartup;
                var me = _game.Corporations[0];
                PlaytestWrite($"Planet Trader {Application.version}, {DateTime.Now:yyyy-MM-dd HH:mm}, {how}");
                PlaytestWrite($"Директор: {me.Character?.Name ?? "средний"}, зерно партии {_game.Seed}, ход {_game.Turn}, экран {Screen.width}×{Screen.height}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _playLog = null;
            }
        }

        /// <summary>Строка журнала игры — и в файл плейтеста.</summary>
        void PlaytestEvent(string text)
        {
            if (_playLog == null || !_playing) return;
            PlaytestWrite($"  ход {_game.Turn}: {RichTags.Replace(text, "")}");
        }

        /// <summary>Итог хода: сколько думал, сколько действий потратил, где оказался.</summary>
        void PlaytestTurn(int turn, int actionsUsed, int actionsPerTurn)
        {
            if (_playLog == null || !_playing) return;
            var g = _game;
            float now = Time.realtimeSinceStartup;
            float thinking = now - _turnStart;
            _turnStart = now;
            PlaytestWrite($"ХОД {turn} закончен: думал {thinking:0} с, действий {actionsUsed} из {actionsPerTurn} · деньги {g.Money:0} · долг {g.Debt:0} · " +
                          $"капитал {g.Capital:0} · планет {g.PlayerPlanetCount} · уровень «{g.LevelInfo.Name}» · шоу: {g.ShowPlace}-е место, кубков {g.Cups}");
        }

        void PlaytestWrite(string line)
        {
            if (_playLog == null) return;
            try
            {
                var t = TimeSpan.FromSeconds(Time.realtimeSinceStartup - _sessionStart);
                _playLog.WriteLine($"[{(int)t.TotalMinutes:00}:{t.Seconds:00}] {line}");
            }
            catch (Exception)
            {
                _playLog = null;
            }
        }

        void ClosePlaytestLog()
        {
            if (_playLog == null) return;
            PlaytestWrite($"Сессия закончена на ходу {_game?.Turn}.");
            try
            {
                _playLog.Dispose();
            }
            catch (Exception)
            {
                // Файл уже закрыт — ничего страшного.
            }
            _playLog = null;
        }

        /// <summary>Кнопка меню: открыть папку с журналами (и сохранениями рядом) в Finder или Проводнике.</summary>
        void OpenPlaytestFolder()
        {
            Directory.CreateDirectory(PlaytestDir);
            Application.OpenURL("file://" + PlaytestDir.Replace(" ", "%20"));
            Log($"Журналы для отзыва — в папке {PlaytestDir}. Пришлите файлы session-….txt вместе с ответами.");
        }
    }
}
