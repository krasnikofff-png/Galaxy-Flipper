using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Окно «Важное за ход»: после хода соперников — то, что серьёзно касается игрока: отняли планету, магнат подкупает
    /// жителей или судится, картель, бунт, банкротство, новый уровень. Что считать важным, решает ядро (<see cref="Alert"/>).
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        VisualElement _alerts;
        ScrollView _alertsList;
        Label _alertsTitle;

        bool AlertsOpen => _alerts != null && !_alerts.ClassListContains("hidden");

        /// <summary>Вид карточки: значок и класс цвета. Порядок в окне — от худшего к хорошему.</summary>
        static readonly Dictionary<AlertKind, (string Icon, string Look, int Order)> AlertLooks = new Dictionary<AlertKind, (string, string, int)>
        {
            [AlertKind.Bankrupt] = ("coins", "lost", 0),
            [AlertKind.PlanetLost] = ("flag", "lost", 1),
            [AlertKind.Threat] = ("warning", "threat", 2),
            [AlertKind.Disaster] = ("flame", "disaster", 3),
            [AlertKind.Trick] = ("tophat", "trick", 4),
            [AlertKind.Good] = ("star", "good", 5),
            [AlertKind.Show] = ("crown", "show", 6),
        };

        void CreateAlerts(VisualElement root)
        {
            _alerts = root.Q("alerts");
            _alertsList = root.Q<ScrollView>("alertsList");
            _alertsTitle = root.Q<Label>("alertsTitle");
            root.Q<Button>("alertsOk").clicked += CloseAlerts;
            _alerts.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _alerts) CloseAlerts();
            });
        }

        void ShowAlerts(IReadOnlyList<Alert> alerts, int turn)
        {
            if (alerts.Count == 0) return;
            HideTooltip();
            _alertsTitle.text = $"Важное за ход {turn}";
            _alertsList.Clear();
            foreach (var a in alerts.OrderBy(a => AlertLooks[a.Kind].Order)) _alertsList.Add(AlertCard(a));
            _alertsList.scrollOffset = Vector2.zero;
            _alerts.RemoveFromClassList("hidden");
        }

        void CloseAlerts()
        {
            _alerts.AddToClassList("hidden");
            _alertsList.Clear();
            Later(NextPopup);
        }

        /// <summary>Карточка события: портрет виноватого магната (или значок), заголовок, подробности, «Показать планету».</summary>
        VisualElement AlertCard(Alert a)
        {
            var (icon, look, _) = AlertLooks[a.Kind];
            var card = new VisualElement();
            card.AddToClassList("alert-card");
            card.AddToClassList("alert-" + look);

            var m = a.MagnateId >= 0 ? _game.GetMagnate(a.MagnateId) : null;
            if (m != null) card.Add(MagnatePortrait(m, 70));
            else
            {
                var badge = new VisualElement();
                badge.AddToClassList("alert-badge");
                badge.Add(Icons.Make(icon, "alert-badge-icon"));
                card.Add(badge);
            }

            var body = new VisualElement();
            body.AddToClassList("alert-body");
            var head = new VisualElement();
            head.AddToClassList("alert-head");
            head.Add(Icons.Make(icon, "alert-kind-icon"));
            head.Add(new Label(a.Title).WithClass("alert-title"));
            if (m != null) head.Add(new Label(m.Name).WithClass("alert-who"));
            body.Add(head);
            body.Add(new Label(a.Text).WithClass("alert-text"));

            var p = a.PlanetId >= 0 ? _game.GetPlanet(a.PlanetId) : null;
            if (p != null)
            {
                int id = p.Id;
                var go = new Button { focusable = false };
                go.AddToClassList("small-button");
                go.AddToClassList("alert-go");
                go.Add(new Label(a.Kind == AlertKind.Threat ? "К планете — ответить" : "Показать планету") { pickingMode = PickingMode.Ignore });
                go.Add(Icons.Make("arrow"));
                go.clicked += () => Later(() =>
                {
                    CloseAlerts();
                    _tab = Tab.Planet;
                    Select(id);
                });
                body.Add(go);
            }
            card.Add(body);
            return card;
        }

        /// <summary>Для автоснимка: окно с примерами всех видов событий на настоящих планетах и магнатах.</summary>
        internal void AlertsForShot() => Later(() =>
        {
            var mine = _game.PlayerPlanets.First();
            var m = _game.Magnates.First();
            var other = _game.AllPlanets.First(x => !_game.IsMine(x) && x.OwnerCorp != null);
            ShowAlerts(new[]
            {
                new Alert { Kind = AlertKind.Good, Title = "Новый уровень: Контора", Text = "Ваша корпорация доросла до уровня «Контора»: второй менеджер." },
                new Alert { Kind = AlertKind.PlanetLost, Title = "Планету отсудили", Text = $"Реестр отдал {other.Type.Name} #{other.Id} магнату {m.Name}. Компенсация — 1450.", PlanetId = other.Id, MagnateId = m.Id },
                new Alert { Kind = AlertKind.Threat, Title = "Магнат подкупает жителей", Text = $"{m.Name} подкупает жителей {mine.Type.Name} #{mine.Id}! Через 3 хода они уйдут, если лояльность не поднимется до 50. Помогут праздник или встречный подкуп.", PlanetId = mine.Id, MagnateId = m.Id },
                new Alert { Kind = AlertKind.Disaster, Title = "Бунт", Text = $"Бунт на {mine.Type.Name} #{mine.Id}: жители разнесли шахту. Страховка бунты не покрывает.", PlanetId = mine.Id },
                new Alert { Kind = AlertKind.Trick, Title = "Картель против вас", Text = "Магнаты сговорились со скупщиками против вас: 8 ходов биржа платит на 15% меньше. Репутация от 70 смягчила бы удар вдвое." },
            }, _game.Turn - 1);
        });
    }
}
