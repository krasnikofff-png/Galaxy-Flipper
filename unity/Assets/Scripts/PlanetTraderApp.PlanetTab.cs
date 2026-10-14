using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Вкладка «Планета». Сверху — окно планеты и тревоги, сразу под ними — все действия одной карточкой,
    /// по группам, компактными строками; ниже — сводка, пояснения — в сворачиваемых блоках «Подробнее».
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        /// <summary>Планета, продажу которой нажали один раз: второе нажатие продаёт.</summary>
        int _sellConfirmId = -1;

        void PlanetTab(PanelBuilder ui)
        {
            var p = Selected;
            if (p == null)
            {
                ui.Text("Выберите планету на карте.");
                return;
            }
            if (_sellConfirmId != p.Id) _sellConfirmId = -1;

            var g = _game;
            bool silhouette = p.Knowledge == Knowledge.Silhouette;
            bool exact = p.Owner == Owner.Player || p.Knowledge == Knowledge.Explored;

            PlanetHead(ui, p, silhouette);
            ui.Add(_portraitBox);
            PlanetAlerts(ui, p);

            using (ui.Box("actions-card"))
            {
                var head = ui.Add(new VisualElement()).WithClass("actions-head");
                head.Add(new Label("Действия").WithClass("actions-title"));
                head.Add(new VisualElement().WithClass("spacer"));
                head.Add(new Label($"осталось {g.ActionsLeft} из {g.ActionsPerTurn}").WithClass("actions-left"));
                switch (p.Owner)
                {
                    case Owner.Registry: RegistryActions(ui, p, silhouette, exact); break;
                    case Owner.Player: OwnActions(ui, p, exact); break;
                    default: RivalActions(ui, p, silhouette); break;
                }
            }

            PlanetSummary(ui, p, silhouette, exact);
            PlanetDetails(ui, p, silhouette, exact);
        }

        string Cost(double money) => $"{Fmt.Money(money)} · 1 действие";

        bool CanAct(double money) => _game.ActionsLeft > 0 && _game.Money >= money;

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------
        // Что это за планета
        // ------------------------------------------------------------------

        /// <summary>Заголовок: имя планеты, плашка владельца, координаты в секторе.</summary>
        void PlanetHead(PanelBuilder ui, Planet p, bool silhouette)
        {
            var head = ui.Add(new VisualElement()).WithClass("planet-head");
            head.Add(new Label(silhouette ? $"Силуэт #{p.Id}" : $"{p.Type.Name} #{p.Id}").WithClass("planet-title"));
            string pill = p.Owner == Owner.Player ? "Ваша планета"
                : p.Owner == Owner.Ai ? "Планета " + (_game.GetMagnate(p.MagnateId)?.Name.Split(' ')[0] ?? "магната")
                : silhouette ? "Силуэт" : p.FoundByProbe ? "Нашёл ваш зонд" : "Свободна";
            var tag = new Label(pill).WithClass("pill");
            tag.AddToClassList(p.Owner == Owner.Player ? "mine" : p.Owner == Owner.Ai ? "rival" : p.FoundByProbe ? "found" : "free");
            head.Add(tag);
            head.Add(new VisualElement { pickingMode = PickingMode.Ignore }.WithClass("spacer"));
            var coords = new VisualElement().WithClass("coords");
            coords.Add(new Label(_game.Sectors[p.Sector].Name).WithClass("label-text"));
            coords.Add(new Label($"X: {p.MapX * 1000:0}   Y: {p.MapY * 1000:0}").WithClass("label-text"));
            head.Add(coords);
        }

        /// <summary>Что требует внимания прямо сейчас: иски, подкупы, забастовка, эпидемия, авария.</summary>
        void PlanetAlerts(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            if (p.Owner != Owner.Player) return;
            foreach (var x in g.Claims.Where(c => c.PlanetId == p.Id).ToList()) Claim(ui, x, p);
            foreach (var x in g.Bribes.Where(c => c.PlanetId == p.Id).ToList()) Bribe(ui, x, p);
            if (p.Inhabited && p.OnStrike)
                using (ui.Card(alert: true))
                    ui.Small($"<color=#ff8a7a><b>Забастовка.</b></color> Ни добычи, ни налогов, пока лояльность не поднимется до {Fmt.Amount(b.StrikeEndsAt)}. Помогут праздник, зарплаты, школа.");
            if (p.EpidemicTurnsLeft > 0)
                using (ui.Card(alert: true))
                    ui.Small($"<color=#ff8a7a><b>Эпидемия</b></color> ещё {p.EpidemicTurnsLeft} х.: добыча вполовину.");
            if (p.Mine == MineState.Working && p.AccidentTurnsLeft > 0)
                using (ui.Card(alert: true))
                    ui.Small($"<color=#ff8a7a><b>Авария на шахте</b></color> ещё {p.AccidentTurnsLeft} х.: добыча стоит.");
        }

        /// <summary>Сводка в два столбца: что за планета и что она приносит.</summary>
        void PlanetSummary(PanelBuilder ui, Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            const string unknown = "?";
            var card = ui.Add(new VisualElement()).WithClass("summary");
            var left = new VisualElement().WithClass("summary-col");
            left.AddToClassList("first");
            var right = new VisualElement().WithClass("summary-col");
            card.Add(left);
            card.Add(right);

            double known = silhouette ? 0 : g.KnownReserve(p) ?? 0;
            Fact(left, "planet", "Тип", silhouette ? "Неизвестен" : p.Type.Name);
            Fact(left, silhouette ? "cube" : ResourceIcon(p.Resource), "Ресурс", silhouette ? unknown : p.Resource.RuName(),
                silhouette ? (Color?)null : Fmt.ResourceColor(p.Resource));
            if (silhouette) Fact(left, "layers", "Запас", unknown);
            else if (exact) Fact(left, "layers", "Запас", $"{Fmt.Amount(p.Reserve)} / {Fmt.Amount(p.Capacity)}", null, p.Capacity > 0 ? (float)(p.Reserve / p.Capacity) : 0);
            else Fact(left, "layers", "Запас", $"≈ {Fmt.Amount(p.EstimatedReserve)}");
            Fact(left, "pickaxe", "Добыча", silhouette ? unknown
                : $"{Fmt.Amount(p.Type.OutputPerTurn)} / ход, на {(exact ? "" : "≈")}{Fmt.Amount(known / p.Type.OutputPerTurn)} х.");
            Fact(left, "tag", "Цена за ед.", silhouette ? unknown : Fmt.Money(g.Market.Price(p.Resource)));
            Fact(left, "station", "До биржи", $"{Fmt.Amount(g.DistanceToExchange(p) * 10)} св. лет, груз {g.ShipTurns(p)} х.");

            Fact(right, "flag", "Владелец", p.Owner == Owner.Player ? "Вы" : p.Owner == Owner.Ai ? (_game.GetMagnate(p.MagnateId)?.Name.Split(' ')[0] ?? "Магнат") : "Реестр",
                RingColor(p));
            Fact(right, "eye", "Знание", silhouette ? "Силуэт" : exact ? "Разведана" : "Скан", new Color(0.37f, 0.78f, 1f));
            Fact(right, "coins", exact ? "Стоимость" : "Стоимость (оценка)", silhouette ? unknown : Fmt.Money(g.KnownValue(p) ?? 0));
            Fact(right, "gear", "Содержание", silhouette ? unknown
                : $"{Fmt.Money(p.Owner == Owner.Player ? g.PlanetUpkeep(p) : p.Type.Upkeep)} / ход");
            Fact(right, "cube", "Шахта", silhouette ? unknown : Cap(Fmt.MineName(p)));
            string people = silhouette ? unknown : !p.Inhabited ? "Нет" : p.Owner != Owner.Player ? "Есть"
                : $"лояльность {Mathf.RoundToInt((float)p.Loyalty)}, {Signed(g.LoyaltyChangePerTurn(p))}/ход";
            Fact(right, "people", "Жители", people);
        }

        static string Signed(double x) => x >= 0 ? $"+{Fmt.Amount(x)}" : Fmt.Amount(x);

        static void Fact(VisualElement col, string icon, string label, string value, Color? iconColor = null, float? share = null)
        {
            var row = new VisualElement().WithClass("fact");
            var main = new VisualElement().WithClass("fact-main");
            var i = Icons.Make(icon, "fact-icon");
            // Цвет значка — цвет ресурса или владельца: данные, а не оформление.
            if (iconColor.HasValue) i.style.color = iconColor.Value;
            main.Add(i);
            main.Add(new Label(label).WithClass("fact-label"));
            main.Add(new Label(value).WithClass("fact-value"));
            row.Add(main);
            if (share.HasValue)
                row.Add(PanelBuilder.MakeBar(share.Value, Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.35f, 0.84f, 0.55f), share.Value)).WithClass("fact-bar"));
            col.Add(row);
        }

        // ------------------------------------------------------------------
        // Действия
        // ------------------------------------------------------------------

        static readonly double[] OfferShares = { 0.8, 0.9, 1.0, 1.1 };

        /// <summary>Свободная планета: разведка зондом и покупка — у реестра, на аукционе или у жителей.</summary>
        void RegistryActions(PanelBuilder ui, Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            var b = g.Balance;
            if (p.IsDepleted)
            {
                ui.InfoRow("warning", "Планета выработана", "покупать её незачем");
                return;
            }
            if (!g.HasLicense(p.Sector))
            {
                var spec = g.Sectors[p.Sector];
                ui.InfoRow("lock", $"Нужна лицензия на сектор «{spec.Name}»",
                    $"с уровня «{b.CorporationLevels[spec.LicenseLevel].Name}», {Fmt.Money(spec.LicenseCost)}; купить — на вкладке сектора над картой");
                return;
            }
            // С жителями без разведки не поговорить — тогда разведка и есть главное действие.
            if (p.Knowledge != Knowledge.Explored) ProbeRows(ui, p, accent: !g.ExploredEnoughToBuy(p));

            ui.Section("Покупка");
            double price = g.PurchasePrice(p);
            if (p.Inhabited)
            {
                if (!g.ExploredEnoughToBuy(p))
                    ui.InfoRow("people", "Жители не продают", "сначала разведайте планету зондом");
                else if (g.Turn < p.NoTalksUntilTurn)
                    ui.InfoRow("people", "Жители обиделись", $"не разговаривают до хода {p.NoTalksUntilTurn}");
                else
                {
                    var offers = OfferShares.Select(x => new Offer(x)).ToList();
                    offers.Add(new Offer(1, withSchool: true));
                    foreach (var o in offers)
                    {
                        double cost = g.OfferCost(p, o);
                        var offer = o;
                        int id = p.Id;
                        ui.ActionRow("people", o.WithSchool ? "Сделка «за школу»" : $"Предложить {Fmt.Percent(o.PriceShare)} цены",
                            $"согласятся с шансом {Fmt.Percent(g.OfferChance(p, o))}" + (o.WithSchool ? ", школа жителям в подарок" : ""),
                            Cost(cost), CanAct(cost), () =>
                            {
                                if (Do(g.MakeOffer(id, offer, out bool accepted)) && accepted)
                                    Log($"Сделка с жителями #{id}: планета ваша за {Fmt.Money(g.OfferPrice(g.GetPlanet(id), offer))}.");
                            }, accent: o.WithSchool);
                    }
                    ui.Small("Чем щедрее предложение, тем охотнее согласятся и тем выше будет их лояльность. Отказ тратит действие.");
                }
                return;
            }

            if (p.SoldAtAuction)
            {
                foreach (var f in b.BidFactors)
                {
                    double bid = g.BidPrice(p, f);
                    int id = p.Id;
                    double factor = f;
                    ui.ActionRow("coins", $"Ставка {Fmt.Percent(f)}", $"выиграете с шансом {Fmt.Percent(g.AuctionWinChance(p, f))}",
                        Cost(bid), CanAct(bid), () =>
                        {
                            if (Do(g.PlaceBid(id, factor, out bool won)) && won)
                                Log($"Аукцион выигран: планета #{id} ваша за {Fmt.Money(bid)}.");
                        });
                }
                ui.Small("Аукцион реестра: магнаты тоже ставят. Проиграете — планета уйдёт к ним, действие потрачено." +
                         (silhouette ? " Это силуэт: ставка вслепую." : exact ? "" : " Ставка по оценке реестра."));
            }
            else
            {
                string blind = silhouette ? " вслепую" : exact ? "" : " по оценке";
                ui.ActionRow("coins", $"Купить{blind}", silhouette ? "тип и запас неизвестны" : exact ? "точный запас известен" : "реестр может завышать запас",
                    Cost(price), CanAct(price), () => Act(() => g.BuyPlanet(p.Id), $"Куплена планета {p.Type.Name} #{p.Id} за {Fmt.Money(price)}."), accent: true);
            }
            if (p.Mine == MineState.None)
                ui.Small($"После покупки нужна шахта: {Fmt.Money(g.MineCost(p))} и {b.MineBuildTurns} хода.");
        }

        /// <summary>Своя планета: добыча, жители, правила, сделки.</summary>
        void OwnActions(PanelBuilder ui, Planet p, bool exact)
        {
            var g = _game;
            var b = g.Balance;

            ui.Section("Добыча");
            if (p.IsDepleted)
                ui.InfoRow("layers", "Месторождение восстанавливается", $"ещё {p.RegenTurnsLeft} х." + (p.Inhabited && p.Education > 0 ? " Пока шахта стоит, все жители заняты умственным трудом." : ""));
            else if (p.Mine == MineState.None)
            {
                double mine = g.MineCost(p);
                double turns = (g.KnownReserve(p) ?? 0) / p.Type.OutputPerTurn;
                ui.ActionRow("pickaxe", "Построить шахту", $"{Fmt.Amount(p.Type.OutputPerTurn)} {p.Resource.RuName().ToLowerInvariant()} за ход на {Fmt.Amount(turns)} х.; строится {b.MineBuildTurns} хода",
                    Cost(mine), CanAct(mine), () => Act(() => g.BuildMine(p.Id), $"Начата постройка шахты на {p.Type.Name} #{p.Id}."), accent: true);
            }
            else if (p.Mine == MineState.Building)
                ui.InfoRow("pickaxe", "Шахта строится", $"ещё {p.BuildTurnsLeft} х.");
            else
                ui.InfoRow("pickaxe", "Шахта работает", $"{Fmt.Amount(p.Type.OutputPerTurn * (1 + g.OutputBonus))} за ход");
            for (int i = 0; i < p.ExtraDeposits.Count; i++)
            {
                var d = p.ExtraDeposits[i];
                if (d.HasExtractor) continue;
                int index = i;
                double cost = g.ExtractorCost(d);
                if (!g.IsUnlocked(d.Resource))
                {
                    var tech = b.Technologies.FirstOrDefault(t => t.UnlocksResource == d.Resource);
                    ui.InfoRow("gear", $"Экстрактор: {d.Resource.RuName().ToLowerInvariant()}", $"нужна технология «{tech?.Name}» (вкладка «Развитие»)");
                }
                else
                    ui.ActionRow("gear", $"Экстрактор: {d.Resource.RuName().ToLowerInvariant()}", $"+{Fmt.Amount(d.OutputPerTurn)} за ход, запас {Fmt.Amount(d.Capacity)}",
                        Cost(cost), CanAct(cost), () => Act(() => g.BuildExtractor(p.Id, index), $"Экстрактор на {p.Type.Name} #{p.Id}: {d.Resource.RuName()}."));
            }

            if (p.Inhabited) PeopleActions(ui, p);
            else
            {
                ui.Section("Колонисты");
                ui.ActionRow("people", "Заселить колонистов", $"налоги и умственный труд; лояльность {Fmt.Amount(b.ColonyLoyalty)}, шахта её снижает",
                    Cost(g.ColonyCost), CanAct(g.ColonyCost), () => Act(() => g.SettleColony(p.Id), $"На {p.Type.Name} #{p.Id} прибыли колонисты."));
            }

            ui.Section("Сделки");
            double sale = g.SellPlanetPrice(p);
            double hype = g.CurrentEvent?.Event.PlanetSale(p.Resource) ?? 1;
            string hypeNote = hype != 1 ? $"; событие «{g.CurrentEvent.Event.Name}»: ×{hype:0.##}" : "";
            int pid = p.Id;
            if (_sellConfirmId == p.Id)
            {
                ui.ActionRow("tag", $"Точно продать за {Fmt.Money(sale)}?", "планета уйдёт в реестр, жители и постройки — с ней", "1 действие", g.ActionsLeft > 0, () =>
                {
                    _sellConfirmId = -1;
                    Act(() => g.SellPlanet(pid), $"Продана планета {p.Type.Name} #{pid} за {Fmt.Money(sale)}.");
                }, danger: true);
                ui.ActionRow("close", "Не продавать", null, null, true, () => _sellConfirmId = -1);
            }
            else
                ui.ActionRow("tag", "Продать планету", $"получите {Fmt.Money(sale)} (комиссия {Fmt.Percent(b.ResaleCommission)}){hypeNote}", "1 действие",
                    g.ActionsLeft > 0, () => _sellConfirmId = pid).AddToClassList("quiet");
        }

        /// <summary>Жители своей планеты: просьбы, праздник, школа, учёба и правила — налог, зарплаты, охрана.</summary>
        void PeopleActions(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            ui.Section("Жители");
            foreach (var r in g.Requests.Where(r => r.PlanetId == p.Id).ToList())
            {
                int left = r.DeadlineTurn - g.Turn;
                string reward = $"осталось {left} х.: выполните — +{Fmt.Amount(b.RequestLoyaltyReward)} лояльности, нет — −{Fmt.Amount(b.RequestLoyaltyPenalty)}";
                if (r.Kind == RequestKind.School)
                    ui.ActionRow("people", "Просьба: построить школу", reward, Cost(g.SchoolCost), CanAct(g.SchoolCost),
                        () => Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}."), accent: true);
                else
                {
                    double have = g.Stock(r.Resource);
                    int id = r.Id;
                    ui.ActionRow("cube", $"Просьба: {Fmt.Amount(r.Amount)} {r.Resource.RuName().ToLowerInvariant()} со склада", $"{reward}; на складе {Fmt.Amount(have)}",
                        "без действия", have >= r.Amount, () => Act(() => g.FulfillRequest(id), "Просьба жителей выполнена."), accent: true);
                }
            }
            bool worried = p.Loyalty < 45 || p.OnStrike || g.Bribes.Any(x => x.PlanetId == p.Id);
            ui.ActionRow("spark", "Праздник", $"лояльность +{Fmt.Amount(g.FestivalLoyalty)} сразу", Cost(b.FestivalCost), CanAct(b.FestivalCost),
                () => Act(() => g.HoldFestival(p.Id), $"Праздник на {p.Type.Name} #{p.Id}."), accent: worried);
            if (!p.HasSchool && !g.Requests.Any(r => r.PlanetId == p.Id && r.Kind == RequestKind.School))
                ui.ActionRow("journal", "Построить школу", $"+{Fmt.Amount(b.SchoolLoyalty)} лояльности каждый ход", Cost(g.SchoolCost), CanAct(g.SchoolCost),
                    () => Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}."));
            EducationActions(ui, p);

            ui.Section("Правила планеты");
            using (ui.Box("rule-row"))
            {
                ui.Label("Налог", "rule-label");
                var taxNames = new[]
                {
                    "нет",
                    $"низкий +{Fmt.Money(b.TaxIncome[1])} ({Fmt.Amount(b.TaxLoyalty[1])})",
                    $"высокий +{Fmt.Money(b.TaxIncome[2])} ({Fmt.Amount(b.TaxLoyalty[2])})",
                };
                ui.Choice(taxNames, (int)p.Tax, tax => Do(g.SetTax(p.Id, (TaxLevel)tax))).AddToClassList("rule-choice");
            }
            ui.Check(p.PaysWages, $"Зарплаты жителям: {Fmt.Money(b.WagesCost)} за ход, лояльность +{Fmt.Amount(b.WagesLoyalty)} за ход",
                wages => Do(g.SetWages(p.Id, wages)));
            ui.Check(p.Guarded, $"Охрана: {Fmt.Money(b.GuardsCost)} за ход, магнаты подкупают охраняемых, только если лояльность ниже {Fmt.Amount(b.GuardedBribeLoyalty)}",
                guards => Do(g.SetGuards(p.Id, guards)));
        }

        /// <summary>Учёба жителей: склонная специальность — сразу, остальные — в сворачиваемом блоке.</summary>
        void EducationActions(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            int max = b.EducationCost.Length - 1;
            if (p.IsStudying)
            {
                ui.InfoRow("flask", "Жители учатся", $"уровень {p.Education + 1} через {p.EducationTurnsLeft} х.");
                return;
            }
            if (p.Education >= max) return;
            int next = p.Education + 1;
            double cost = g.EducationCost(p);
            g.CanEducate(p, out string reason);
            // Учить пока нельзя не из-за денег (нет школы и т. п.) — одна строка с причиной вместо недоступных кнопок.
            if (reason != null && reason != "Не хватает денег")
            {
                ui.InfoRow("flask", "Учёба жителей", reason);
                return;
            }
            bool canClick = reason == null && g.ActionsLeft > 0;
            string income = $"+{Fmt.Money(b.IntellectIncome[next])} за ход умственным трудом, учатся {b.EducationTurns[next]} х.";
            if (p.Education > 0)
            {
                ui.ActionRow("flask", $"Учить дальше: уровень {next}", income, Cost(cost), canClick,
                    () => Act(() => g.Educate(p.Id), $"Жители {p.Type.Name} #{p.Id} учатся дальше."));
            }
            else
            {
                var natural = Specialties.Natural(p.Resource);
                ui.ActionRow("flask", $"Учить: {natural.RuName()} (склонны)", $"{natural.BonusText()}; {income}", Cost(cost), canClick,
                    () => Act(() => g.Educate(p.Id, natural), $"Жители {p.Type.Name} #{p.Id} учатся: {natural.RuName()}."));
                using (ui.Fold("Другие специальности", "education-other"))
                    foreach (var sp in Specialties.All.Where(x => x != natural))
                    {
                        var choice = sp;
                        ui.ActionRow("flask", $"Учить: {sp.RuName()}", $"{sp.BonusText()}; {income}", Cost(cost), canClick,
                            () => Act(() => g.Educate(p.Id, choice), $"Жители {p.Type.Name} #{p.Id} учатся: {choice.RuName()}."));
                    }
            }
        }

        /// <summary>Чужая планета: разведка и подкуп жителей.</summary>
        void RivalActions(PanelBuilder ui, Planet p, bool silhouette)
        {
            var g = _game;
            var b = g.Balance;
            if (p.Knowledge != Knowledge.Explored) ProbeRows(ui, p, accent: false);
            ui.Section("Магнат");
            ui.Small($"Планета магната {g.GetMagnate(p.MagnateId)?.Name ?? "ИИ"}. Купить её можно, когда он выставит её на аукцион.");
            if (p.Inhabited && !silhouette && !p.IsDepleted)
            {
                double bribe = g.MagnateBribeCost(p);
                ui.ActionRow("people", "Подкупить жителей",
                    $"шанс {Fmt.Percent(b.PlayerBribeChance)}, репутация {Fmt.Amount(b.PlayerBribeReputation)}; реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)})",
                    Cost(bribe), CanAct(bribe), () => Do(g.BribeMagnateResidents(p.Id, out _)));
            }
        }

        /// <summary>Самый дешёвый зонд, который долетит до планеты, или null.</summary>
        ProbeType CheapestProbe(Planet p) =>
            _game.UnlockedProbes.Where(t => _game.InRange(t, p)).OrderBy(t => t.Cost).FirstOrDefault();

        /// <summary>Сколько лететь до планеты и откуда: от ближайшей вашей планеты или от биржи.</summary>
        string ProbeDistance(Planet p)
        {
            var g = _game;
            var origin = g.LaunchOrigin(p);
            bool fromExchange = origin.x == g.Balance.ExchangeX && origin.y == g.Balance.ExchangeY;
            return $"до планеты {Fmt.Amount(origin.distance * 10)} св. лет {(fromExchange ? "от биржи" : "от вашей планеты")}";
        }

        /// <summary>
        /// Разведка планеты одной строкой: самый дешёвый зонд, который долетит, расстояние и срок.
        /// Зонды подороже нужны только дальним планетам — их и предлагаем, когда дешёвый не долетает.
        /// </summary>
        void ProbeRows(PanelBuilder ui, Planet p, bool accent)
        {
            var g = _game;
            ui.Section("Разведка");
            var flying = g.Probes.FirstOrDefault(x => x.TargetPlanetId == p.Id);
            if (flying != null)
            {
                ui.InfoRow("rocket", "Зонд в пути", $"разведка через {flying.ResolveTurn - g.Turn + 1} х.");
                return;
            }
            var probe = CheapestProbe(p);
            if (probe == null)
            {
                // Долетит зонд, который ещё не по уровню корпорации, — скажем, когда он откроется.
                var later = g.Balance.ProbeTypes.Where(t => !g.ProbeUnlocked(t) && g.InRange(t, p)).OrderBy(t => t.MinLevel).FirstOrDefault();
                if (later != null)
                {
                    var level = g.Balance.CorporationLevels[later.MinLevel];
                    ui.InfoRow("rocket", $"Сюда долетит только «{later.Name}»",
                        $"{ProbeDistance(p)}; зонд откроется на уровне «{level.Name}» — при капитале от {Fmt.Money(level.Capital)}");
                    return;
                }
                double farthest = g.Balance.ProbeTypes.Max(t => t.Range);
                ui.InfoRow("rocket", "Зонды сюда не долетают", $"{ProbeDistance(p)}, а самый дальний летает на {Fmt.Amount(farthest * 10)}");
                return;
            }
            // Почему не дешевле: какие зонды не долетают.
            var tooShort = g.UnlockedProbes.Where(t => t.Cost < probe.Cost).OrderBy(t => t.Cost).ToList();
            string why = tooShort.Count == 0 ? "" : "; " + string.Join(", ", tooShort.Select(t => $"«{t.Name}» летает только на {Fmt.Amount(t.Range * 10)}"));
            int id = p.Id;
            ui.ActionRow("rocket", $"Разведать зондом «{probe.Name}»",
                $"{ProbeDistance(p)}{why}; результат через {g.ProbeTurns(probe, p)} х.",
                Cost(probe.Cost), CanAct(probe.Cost),
                () => Act(() => g.LaunchExplore(probe, id), $"Зонд «{probe.Name}» отправлен на разведку планеты #{id}."), accent: accent);
        }

        // ------------------------------------------------------------------
        // Подробнее
        // ------------------------------------------------------------------

        /// <summary>Пояснения, которые нужны не каждый ход: месторождения, жители, образование, зонды.</summary>
        void PlanetDetails(PanelBuilder ui, Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            var b = g.Balance;
            if (!silhouette)
                using (ui.Fold("Месторождения и сюрпризы", "deposits"))
                {
                    string main = p.IsRegenerating
                        ? $"восстанавливается, ещё {p.RegenTurnsLeft} из {p.RegenTotal} х."
                        : $"добыча {Fmt.Amount(p.Type.OutputPerTurn * (p.Owner == Owner.Player ? 1 + g.OutputBonus : 1))} за ход";
                    ui.Line($"{p.Resource.RuName()} (основное)", $"{main}; после истощения восстанавливается за {b.RegenTurns[(int)p.Resource]} х.");
                    if (!exact) ui.Small("Дополнительные месторождения и сюрпризы покажет полная разведка.");
                    else
                    {
                        if (p.ExtraDeposits.Count == 0) ui.Small("Других месторождений нет.");
                        foreach (var d in p.ExtraDeposits)
                        {
                            string state = d.IsRegenerating ? $"восстанавливается, ещё {d.RegenTurnsLeft} х."
                                : d.HasExtractor ? (g.IsUnlocked(d.Resource) ? $"добывается, запас {Fmt.Amount(d.Reserve)} из {Fmt.Amount(d.Capacity)}" : "экстрактор есть, нет технологии")
                                : $"запас {Fmt.Amount(d.Capacity)}";
                            ui.Line($"{d.Resource.RuName()} (доп.)", $"{Fmt.Amount(d.OutputPerTurn)} за ход; {state}; восстановление {b.RegenTurns[(int)d.Resource]} х.");
                        }
                        var surprises = new List<string>();
                        if (p.HasUnlootedRuins) surprises.Add($"руины с артефактами на {Fmt.Money(p.RuinsValue)}");
                        if (p.RuinsLooted) surprises.Add("руины разграблены");
                        if (p.Reserve < 0.6 * p.EstimatedReserve) surprises.Add("подвох: реестр завысил запас");
                        ui.Line("Сюрпризы", surprises.Count == 0 ? "нет" : string.Join(", ", surprises));
                    }
                }

            if (p.Owner == Owner.Player && p.Inhabited)
            {
                using (ui.Fold("Жители и лояльность", "people"))
                {
                    ui.Line("Кто", Residents.SpeciesName(Residents.SpeciesOf(Describe(p).Seed)));
                    ui.Line("Лояльность", $"{Mathf.RoundToInt((float)p.Loyalty)} из 100, за ход {Signed(g.LoyaltyChangePerTurn(p))}");
                    ui.Bar((float)(p.Loyalty / 100));
                    ui.Small($"Ниже {Fmt.Amount(b.StrikeBelow)} — забастовка, она кончается, когда лояльность поднимется до {Fmt.Amount(b.StrikeEndsAt)}. " +
                             $"Шахта снижает лояльность на {Fmt.Amount(b.LoyaltyMiningDrain)} за ход, школа добавляет {Fmt.Amount(b.SchoolLoyalty)}.");
                }
                using (ui.Fold("Образование", "education"))
                {
                    int max = b.EducationCost.Length - 1;
                    if (p.Education > 0)
                    {
                        string natural = p.NaturalSpecialty ? $", склонны к этому: +{Fmt.Percent(b.NaturalSpecialtyBonus)} к доходу" : "";
                        ui.Line("Специальность", $"{p.Specialty.RuName()}, уровень {p.Education} из {max}{natural}");
                        string mining = p.Mine == MineState.Working && !p.IsDepleted ? $" (пока шахта работает, умом занято {Fmt.Percent(b.IntellectWhileMining)} жителей)" : "";
                        ui.Line("Умственный труд", $"+{Fmt.Money(g.IntellectIncome(p))} за ход{mining}");
                        ui.Line("Бонус корпорации", p.Specialty.BonusText());
                    }
                    ui.Small("Образованные жители зарабатывают умственным трудом: меньше, чем шахта, зато всегда, даже пока месторождение восстанавливается. " +
                             "Каждый уровень даёт и бонус всей корпорации. Доход растёт с лояльностью.");
                }
            }

            if (p.Knowledge != Knowledge.Explored)
                using (ui.Fold("О зондах", "probes"))
                {
                    ui.Small("Полная разведка показывает точный запас, подвохи и руины. Зонды летят от ближайшей вашей планеты или от биржи. " +
                             "Дороже — дальше и быстрее летает, шире сканирует область и чаще находит скрытые планеты.");
                    foreach (var t in g.Balance.ProbeTypes)
                        ui.Line($"«{t.Name}», {Fmt.Money(t.Cost)}", ProbeDescription(t) +
                            (g.ProbeUnlocked(t) ? "" : $" Откроется на уровне корпорации «{g.Balance.CorporationLevels[t.MinLevel].Name}»."));
                }
        }

        /// <summary>Чем зонд отличается от других: дальность, скорость, скан, шанс найти скрытую планету. Расстояния — в световых годах, как «До биржи».</summary>
        static string ProbeDescription(ProbeType t) =>
            $"Дальность {Fmt.Amount(t.Range * 10)} св. лет, скорость {Fmt.Amount(t.Speed * 10)} св. лет за ход, " +
            $"сканирует область радиусом {Fmt.Amount(t.ScanRadius * 10)} св. лет и находит в ней скрытую планету с шансом {Fmt.Percent(t.FindChance)}.";
    }
}
