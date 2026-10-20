using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Шоу «Риелтор года» (цель партии: задания сезона и рейтинг против магнатов), случаи с выбором и «Лот недели».
    /// Окна после хода идут по очереди: сначала «Важное за ход», потом случай.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        VisualElement _show, _incident;
        ScrollView _showBody;
        Label _showTitle;
        VisualElement _incidentBody;
        Button _incidentChip;
        Label _incidentChipText;

        bool ShowOpen => _show != null && !_show.ClassListContains("hidden");
        bool IncidentOpen => _incident != null && !_incident.ClassListContains("hidden");
        bool PopupOpen => AlertsOpen || ShowOpen || IncidentOpen;

        readonly Queue<Action> _popups = new Queue<Action>();

        static readonly Color ShowGold = new Color(0.94f, 0.77f, 0.35f);

        const string ShowIntro =
            "Брэд Квазар: «Добро пожаловать в „Риелтора года“ — самое безжалостное реалити галактики! Участники — вы и магнаты сектора. " +
            "Сезон длится 40 ходов, в нём три задания. Выполняйте их, набирайте очки, обгоняйте магнатов — и кубок ваш! " +
            "За первые места — деньги и слава. Проиграли? Не беда: сезоны идут один за другим, а эфир бесконечен!»";

        void CreateShow(VisualElement root)
        {
            _show = root.Q("show");
            _showBody = root.Q<ScrollView>("showBody");
            _showTitle = root.Q<Label>("showTitle");
            root.Q<Button>("showOk").clicked += CloseShow;
            _show.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _show) CloseShow();
            });
            _incident = root.Q("incident");
            _incidentBody = root.Q("incidentBody");
            _incidentChip = root.Q<Button>("incidentChip");
            _incidentChipText = root.Q<Label>("incidentChipText");
            _incidentChip.clicked += () => Later(OpenIncident);

            // Место в шоу — в верхней панели; клик открывает окно шоу.
            var stats = root.Q("stats");
            AddStat(stats, "show", "crown", "Риелтор года");
            var stat = stats[stats.childCount - 1];
            stat.Query<VisualElement>().ForEach(e => { if (e != stat) e.pickingMode = PickingMode.Ignore; });
            stat.AddToClassList("clickable");
            stat.RegisterCallback<ClickEvent>(_ => Later(() => OpenShow(null)));
        }

        // ------------------------------------------------------------------
        // Очередь окон
        // ------------------------------------------------------------------

        void QueuePopup(Action open)
        {
            _popups.Enqueue(open);
            NextPopup();
        }

        void NextPopup()
        {
            while (!PopupOpen && _popups.Count > 0) _popups.Dequeue()();
        }

        void RefreshShowStat()
        {
            var g = _game;
            var s = g.Season;
            _statValues["show"].text = s == null ? "—" : $"{g.ShowPlace}-е место";
            bool pending = g.Incident != null && !IncidentOpen;
            _incidentChip.EnableInClassList("hidden", !pending);
            if (pending) _incidentChipText.text = "Случай: " + g.Incident.Def.Title;
        }

        // ------------------------------------------------------------------
        // Окно шоу
        // ------------------------------------------------------------------

        /// <summary>Окно шоу: вступление ведущего (intro), задания сезона с прогрессом, рейтинг, кубки.</summary>
        void OpenShow(string intro)
        {
            var g = _game;
            var s = g.Season;
            if (s == null) return;
            HideTooltip();
            int left = s.EndTurn - g.Turn + 1;
            _showTitle.text = $"«Риелтор года» · сезон {s.Number}";
            _showBody.Clear();

            var host = new VisualElement().WithClass("show-host");
            host.Add(Icons.Make("crown", "show-host-icon"));
            string about = intro ?? $"Сезон {s.Number}: ходы {s.StartTurn}–{s.EndTurn}, " + (left <= 1 ? "итоги в конце этого хода." : $"до итогов {Words.Count(left, "ход", "хода", "ходов")}.");
            host.Add(new Label(about).WithClass("show-host-text"));
            _showBody.Add(host);

            _showBody.Add(new Label("Задания сезона").WithClass("show-section"));
            var me = s.Entries.FirstOrDefault(e => e.CorpId == g.Current.Id);
            for (int i = 0; i < s.Tasks.Count; i++)
            {
                var t = s.Tasks[i];
                bool done = me != null && me.Done.Contains(i);
                double progress = me == null ? 0 : Math.Min(t.Target, g.Progress(me, t));
                var row = new VisualElement().WithClass("show-task");
                row.EnableInClassList("done", done);
                row.Add(Icons.Make(done ? "check" : "target", "show-task-icon"));
                var text = new VisualElement().WithClass("show-task-text");
                text.Add(new Label(t.Spec.Title).WithClass("show-task-title"));
                text.Add(new Label(Core.Game.TaskText(t)).WithClass("small"));
                var bar = new VisualElement().WithClass("show-task-bar");
                var fill = new VisualElement().WithClass("show-task-fill");
                fill.style.width = Length.Percent((float)(t.Target <= 0 ? 0 : progress / t.Target * 100));
                bar.Add(fill);
                text.Add(bar);
                row.Add(text);
                row.Add(new VisualElement().WithClass("spacer"));
                string amount = t.Spec.Kind == ShowTaskKind.SellOnExchange ? $"{Fmt.Money(progress)} / {Fmt.Money(t.Target)}"
                    : t.Spec.Kind == ShowTaskKind.GrowCapital ? $"{progress:0}% / {t.Target:0}%" : $"{progress:0} / {t.Target:0}";
                row.Add(new Label(done ? "готово" : amount).WithClass("show-task-amount"));
                row.Add(new Label($"+{t.Spec.Points}").WithClass("show-task-points"));
                _showBody.Add(row);
            }

            _showBody.Add(new Label("Рейтинг").WithClass("show-section"));
            var ranking = g.Ranking();
            for (int place = 0; place < ranking.Count; place++)
            {
                var e = ranking[place];
                var row = new VisualElement().WithClass("show-rank");
                row.EnableInClassList("mine", e.CorpId == g.Current.Id);
                row.Add(new Label($"{place + 1}").WithClass("show-rank-place"));
                if (place == 0) row.Add(Icons.Make("crown", "show-rank-crown"));
                var name = new Label(e.IsPlayer && e.CorpId == g.Current.Id ? "Вы" : e.Name).WithClass("show-rank-name");
                name.style.color = e.IsPlayer ? new Color(0.4f, 0.88f, 0.52f) : MagnateColor(e.MagnateId);
                row.Add(name);
                row.Add(new VisualElement().WithClass("spacer"));
                row.Add(new Label($"заданий {e.Done.Count} из {s.Tasks.Count}" + (e.BonusPoints > 0 ? $" · эфиры +{e.BonusPoints:0}" : "")).WithClass("show-rank-done"));
                row.Add(new Label($"{e.Points:0}").WithClass("show-rank-points"));
                _showBody.Add(row);
            }

            var b = g.Balance;
            double scale = g.LevelScale(g.Level);
            string prizes = string.Join(", ", b.ShowPrize.Select((p, i) => $"{i + 1}-е — {Fmt.Money(p * scale)}"));
            _showBody.Add(new Label($"Призы сезона: {prizes}; за первое место — кубок. Ваших кубков: {g.Cups}. " +
                                    "Очки — за задания и эфиры (случаи со съёмками). Магнаты тоже выполняют задания — следите за рейтингом.").WithClass("show-note"));
            _showBody.scrollOffset = Vector2.zero;
            _show.RemoveFromClassList("hidden");
        }

        void CloseShow()
        {
            _show.AddToClassList("hidden");
            _showBody.Clear();
            Later(NextPopup);
        }

        // ------------------------------------------------------------------
        // Случай
        // ------------------------------------------------------------------

        void OpenIncident()
        {
            var g = _game;
            var a = g.Incident;
            if (a == null) return;
            HideTooltip();
            _incidentBody.Clear();
            var head = new VisualElement().WithClass("incident-head");
            var m = a.MagnateId >= 0 ? g.GetMagnate(a.MagnateId) : null;
            if (m != null) head.Add(MagnatePortrait(m, 70));
            else
            {
                var badge = new VisualElement().WithClass("alert-badge");
                badge.Add(Icons.Make(Icons.Has(a.Def.Icon) ? a.Def.Icon : "info", "alert-badge-icon").WithClass("incident-icon"));
                head.Add(badge);
            }
            var text = new VisualElement().WithClass("alert-body");
            text.Add(new Label("Случай").WithClass("incident-kicker"));
            text.Add(new Label(a.Def.Title).WithClass("title").WithClass("incident-title"));
            text.Add(new Label(g.IncidentText(a, a.Def.Text)).WithClass("alert-text").WithClass("incident-text"));
            head.Add(text);
            _incidentBody.Add(head);

            var choices = new VisualElement().WithClass("incident-choices");
            for (int i = 0; i < a.Def.Choices.Length; i++)
            {
                var c = a.Def.Choices[i];
                int index = i;
                double cost = g.IncidentCost(a, c);
                var button = new Button { focusable = false }.WithClass("incident-choice");
                button.Add(new Label(c.Label).WithClass("incident-choice-label"));
                button.Add(new VisualElement { pickingMode = PickingMode.Ignore }.WithClass("spacer"));
                string note = (cost > 0 ? Fmt.Money(cost) : "бесплатно") + (c.Chance < 1 ? $" · шанс {c.Chance * 100:0}%" : "");
                button.Add(new Label(note).WithClass("incident-choice-cost"));
                button.Query<VisualElement>().ForEach(x => { if (x != button) x.pickingMode = PickingMode.Ignore; });
                button.SetEnabled(g.Money >= cost);
                button.clicked += () => Later(() => AnswerIncident(index));
                choices.Add(button);
            }
            _incidentBody.Add(choices);

            var footer = new VisualElement().WithClass("alerts-footer");
            footer.Add(new Label($"Не ответите до конца хода — будет «{a.Def.Choices.Last().Label.ToLowerInvariant()}».").WithClass("alerts-note"));
            var p = a.PlanetId >= 0 ? g.GetPlanet(a.PlanetId) : null;
            if (p != null)
            {
                int id = p.Id;
                var go = new Button(() => Later(() => { CloseIncident(); _tab = Tab.Planet; Select(id); })) { text = "К планете", focusable = false }.WithClass("small-button");
                go.style.marginRight = 8;
                footer.Add(go);
            }
            var later = new Button(() => Later(CloseIncident)) { text = "Подумать", focusable = false }.WithClass("small-button");
            footer.Add(later);
            _incidentBody.Add(footer);
            _incident.RemoveFromClassList("hidden");
            MarkDirty();
        }

        void AnswerIncident(int choice)
        {
            var g = _game;
            var a = g.Incident;
            if (a == null) return;
            string title = a.Def.Title, label = a.Def.Choices[choice].Label;
            if (!Do(g.ResolveIncident(choice, out var result))) return;
            Log($"Случай «{title}»: {label.ToLowerInvariant()}. {result}");
            _incidentBody.Clear();
            var box = new VisualElement().WithClass("incident-result");
            box.Add(new Label($"{title}: {label.ToLowerInvariant()}").WithClass("incident-kicker"));
            box.Add(new Label(result).WithClass("title").WithClass("incident-title"));
            _incidentBody.Add(box);
            var footer = new VisualElement().WithClass("alerts-footer");
            footer.Add(new VisualElement().WithClass("spacer"));
            footer.Add(new Button(() => Later(CloseIncident)) { text = "Понятно", focusable = false }.WithClass("alerts-ok"));
            _incidentBody.Add(footer);
            MarkDirty();
        }

        void CloseIncident()
        {
            _incident.AddToClassList("hidden");
            _incidentBody.Clear();
            MarkDirty();
            Later(NextPopup);
        }

        // ------------------------------------------------------------------
        // Лот недели (на панели планеты)
        // ------------------------------------------------------------------

        void LotActions(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var lot = g.Lot;
            var b = g.Balance;
            int left = lot.ResolveTurn - g.Turn + 1;
            string legend = b.Legends.FirstOrDefault(l => l.Name == lot.Legend)?.Description;
            ui.Section("Лот недели");
            using (ui.Card())
            {
                ui.Small($"<color=#f0c45a><b>«{lot.Legend}»</b></color> — {legend}");
                ui.Small($"Торги в конце хода {lot.ResolveTurn} ({(left <= 1 ? "в этот ход" : "через " + Words.Count(left, "ход", "хода", "ходов"))}). " +
                         $"Ставки закрытые, стартовая цена {Fmt.Money(lot.StartPrice)}. Деньги спишутся только при победе — держите их до торгов, иначе ставка сгорит.");
                string who = lot.Interested.Count == 0 ? "Магнаты к лоту интереса не проявили."
                    : "Придут на торги: " + string.Join(", ", lot.Interested.Select(id => g.GetMagnate(id)?.Name).Where(n => n != null)) + ".";
                ui.Small(who);
                var mine = g.MyLotBid;
                if (mine != null) ui.Small($"Ваша ставка: <b>{Fmt.Money(mine.Amount)}</b>. Поднять можно ещё одним действием.");
            }
            if (!g.HasLicense(p.Sector)) return;
            foreach (double f in b.LotBidFactors)
            {
                double price = g.LotBidPrice(f);
                bool higher = g.MyLotBid == null || price > g.MyLotBid.Amount;
                ui.ActionRow("tag", $"Ставка {Fmt.Money(price)}", $"×{f:0.##} от стартовой цены", "1 действие", higher && CanAct(price),
                    () => Act(() => g.PlaceLotBid(f), $"Ставка на «{lot.Legend}»: {Fmt.Money(price)}. Торги в конце хода {lot.ResolveTurn}."), accent: f == b.LotBidFactors[0]);
            }
        }

        /// <summary>Для автоснимка: сыграть пустые ходы до «Лота недели», закрыть окна и выбрать легендарную планету.</summary>
        internal void LotForShot() => Later(() =>
        {
            for (int i = 0; i < 40 && _game.Lot == null; i++) _game.EndTurn();
            _popups.Clear();
            _show.AddToClassList("hidden");
            if (_game.Lot == null) return;
            _game.Current.Money += _game.LotBidPrice(2.0);
            _tab = Tab.Planet;
            Select(_game.Lot.PlanetId);
        });

        /// <summary>Для автоснимка: окно шоу (со вступлением) или текущий случай.</summary>
        internal void ShowForShot(bool intro) => Later(() => OpenShow(intro ? ShowIntro : null));

        internal void IncidentForShot(string id) => Later(() =>
        {
            var g = _game;
            _popups.Clear();
            _show.AddToClassList("hidden");
            var def = g.Balance.Incidents.FirstOrDefault(x => x.Id == id) ?? g.Balance.Incidents[0];
            g.Current.Incident = new ActiveIncident
            {
                Def = def, Turn = g.Turn, Scale = g.LevelScale(g.Level),
                PlanetId = g.PlayerPlanets.First().Id, MagnateId = g.Magnates.First().Id,
            };
            OpenIncident();
        });
    }
}
