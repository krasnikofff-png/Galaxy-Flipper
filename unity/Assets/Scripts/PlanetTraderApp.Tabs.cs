using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>Вкладки правой панели: планета, биржа, заказы, финансы, команда, развитие, магнаты.</summary>
    public sealed partial class PlanetTraderApp
    {
        // ------------------------------------------------------------------
        // Планета
        // ------------------------------------------------------------------

        void PlanetTab(PanelBuilder ui)
        {
            var p = Selected;
            if (p == null)
            {
                ui.Text("Выберите планету на карте.");
                return;
            }

            var g = _game;
            var b = g.Balance;
            bool silhouette = p.Knowledge == Knowledge.Silhouette;
            bool exact = p.Owner == Owner.Player || p.Knowledge == Knowledge.Explored;

            ui.Title(silhouette ? $"Силуэт #{p.Id}" : $"{p.Type.Name} #{p.Id}");
            ui.Add(_portraitBox);
            // Главные действия — сразу под окном планеты, чтобы не искать их под характеристиками.
            MainActions(ui, p, silhouette, exact);
            ui.Space("m");
            ui.Line("Владелец", Fmt.OwnerName(p.Owner, p.FoundByProbe));
            ui.Line("Знание", silhouette ? "силуэт: тип и запас неизвестны" : exact ? "полная разведка" : "скан: тип и оценка реестра");
            ui.Line("До биржи", $"{Fmt.Amount(g.DistanceToExchange(p) * 10)} св. лет, груз идёт {g.ShipTurns(p)} х.");

            if (!silhouette)
            {
                ui.Line("Ресурс", $"{p.Resource.RuName()}, на бирже сейчас {Fmt.Money(g.Market.Price(p.Resource))} за ед.");
                if (exact)
                {
                    string aged = p.Capacity < p.InitialReserve - 0.5 ? $" (было {Fmt.Amount(p.InitialReserve)}: месторождение стареет)" : "";
                    ui.Line("Запас", $"{Fmt.Amount(p.Reserve)} из {Fmt.Amount(p.Capacity)}{aged}");
                    ui.Bar(p.Capacity > 0 ? (float)(p.Reserve / p.Capacity) : 0);
                }
                else
                    ui.Line("Запас", $"≈ {Fmt.Amount(p.EstimatedReserve)} по оценке реестра (может врать)");
                double known = g.KnownReserve(p) ?? 0;
                ui.Line("Добыча", $"{Fmt.Amount(p.Type.OutputPerTurn)} за ход, хватит на {(exact ? "" : "≈ ")}{Fmt.Amount(known / p.Type.OutputPerTurn)} х.");
                ui.Line("Шахта", Fmt.MineName(p) + (p.EpidemicTurnsLeft > 0 ? $", эпидемия ещё {p.EpidemicTurnsLeft} х.: добыча вполовину" : ""));
                if (p.Owner == Owner.Player)
                    ui.Line("Содержание", $"{Fmt.Money(g.PlanetUpkeep(p))} за ход ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за число планет)");
                else
                    ui.Line("Содержание", $"{Fmt.Money(p.Type.Upkeep)} за ход, без множителя");
                ui.Line(exact ? "Рыночная стоимость" : "Стоимость по оценке", Fmt.Money(g.KnownValue(p) ?? 0));
                Deposits(ui, p, exact);
                if (p.Inhabited) ui.Line("Жители", p.Owner == Owner.Player ? "есть, см. ниже" : "есть: планету продают сами жители");
                if (p.Knowledge == Knowledge.Explored || p.Owner == Owner.Player)
                {
                    var surprises = new List<string>();
                    if (p.HasUnlootedRuins) surprises.Add($"руины с артефактами на {Fmt.Money(p.RuinsValue)}");
                    if (p.RuinsLooted) surprises.Add("руины разграблены");
                    if (p.Reserve < 0.6 * p.EstimatedReserve) surprises.Add("подвох: реестр завысил запас");
                    ui.Line("Сюрпризы", surprises.Count == 0 ? "нет" : string.Join(", ", surprises));
                }
            }
            ui.Space("l");

            // Остальное: разведка, жители, охрана; продажа — в самом конце, чтобы не нажать случайно.
            switch (p.Owner)
            {
                case Owner.Registry:
                    if (!p.IsDepleted) ExploreButtons(ui, p);
                    break;

                case Owner.Player:
                    if (p.Inhabited) People(ui, p);
                    else
                    {
                        ui.Small($"Планета необитаема. Колонисты будут платить налоги, а выучившись — зарабатывать умом, даже когда месторождение выработано. " +
                                 $"Но шахта их раздражает: −{Fmt.Amount(b.LoyaltyMiningDrain)} лояльности за ход.");
                        ui.Button($"Заселить колонистов за {Fmt.Money(g.ColonyCost)}: лояльность {Fmt.Amount(b.ColonyLoyalty)} · {Doer(Skill.People)}",
                            g.ActionsLeft > 0 && g.Money >= g.ColonyCost,
                            () => Act(() => g.SettleColony(p.Id), $"На {p.Type.Name} #{p.Id} прибыли колонисты."));
                    }
                    Guards(ui, p);
                    ui.Space("m");
                    double sale = g.SellPlanetPrice(p);
                    double hype = g.CurrentEvent?.Event.PlanetSale(p.Resource) ?? 1;
                    if (hype != 1)
                        ui.Text($"<color=#ffd27a>Событие «{g.CurrentEvent.Event.Name}»: такие планеты сейчас продаются ×{hype:0.##}.</color>");
                    ui.Button($"Продать за {Fmt.Money(sale)} (комиссия {Fmt.Percent(b.ResaleCommission)}) · {Doer(Skill.Negotiation)}", g.ActionsLeft > 0,
                        () => Act(() => g.SellPlanet(p.Id), $"Продана планета {p.Type.Name} #{p.Id} за {Fmt.Money(sale)}."));
                    break;

                case Owner.Ai:
                    ExploreButtons(ui, p);
                    break;
            }
        }

        /// <summary>Главное, что можно сделать с планетой: купить, построить шахту, отбить иск, подкупить жителей магната.</summary>
        void MainActions(PanelBuilder ui, Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            var b = g.Balance;
            switch (p.Owner)
            {
                case Owner.Registry:
                    if (p.IsDepleted)
                    {
                        ui.Text("Планета выработана, покупать её незачем.");
                        break;
                    }
                    double price = g.PurchasePrice(p);
                    string note = p.FoundByProbe ? "без надбавки: её нашёл ваш зонд" : $"с надбавкой аукциона {Fmt.Percent(b.AuctionMarkup)}";
                    if (p.Mine == MineState.Working) note += ", шахта уже стоит";
                    ui.Text($"Цена {Fmt.Money(price)} ({note}). Реестр считает цену по своей оценке запаса.");
                    if (p.Mine == MineState.None)
                        ui.Small($"Потом шахта: {Fmt.Money(g.MineCost(p))} и {b.MineBuildTurns} хода постройки.");
                    if (p.Inhabited && !silhouette)
                        Negotiation(ui, p);
                    else if (p.SoldAtAuction)
                        Auction(ui, p, silhouette, exact);
                    else
                    {
                        string blind = silhouette ? " вслепую" : exact ? "" : " по оценке";
                        ui.Button($"Купить{blind} за {Fmt.Money(price)} · {Doer(Skill.Negotiation)}", g.ActionsLeft > 0 && g.Money >= price,
                            () => Act(() => g.BuyPlanet(p.Id), $"Куплена планета {p.Type.Name} #{p.Id} за {Fmt.Money(price)}."));
                    }
                    break;

                case Owner.Player:
                    foreach (var x in g.Claims.Where(c => c.PlanetId == p.Id).ToList())
                        Claim(ui, x, p);
                    if (p.Mine == MineState.None && !p.IsDepleted)
                    {
                        double mine = g.MineCost(p);
                        ui.Button($"Построить шахту за {Fmt.Money(mine)}, {b.MineBuildTurns} хода · {Doer(Skill.Geology)}", g.ActionsLeft > 0 && g.Money >= mine,
                            () => Act(() => g.BuildMine(p.Id), $"Начата постройка шахты на {p.Type.Name} #{p.Id}."));
                    }
                    if (p.IsDepleted)
                        ui.Text(p.Inhabited && p.Education > 0
                            ? "Основное месторождение восстанавливается. Пока шахта стоит, все жители заняты умственным трудом."
                            : "Основное месторождение восстанавливается, а содержание идёт. Жители с образованием зарабатывали бы и сейчас.");
                    break;

                case Owner.Ai:
                    var owner = g.GetMagnate(p.MagnateId);
                    ui.Text($"Планета магната {owner?.Name ?? "ИИ"}. Купить её можно, когда он выставит её на аукцион.");
                    if (p.Inhabited && !silhouette && !p.IsDepleted)
                    {
                        double bribe = g.MagnateBribeCost(p);
                        ui.Small($"Можно подкупить жителей: шанс {Fmt.Percent(b.PlayerBribeChance)}, репутация {Fmt.Amount(b.PlayerBribeReputation)}, " +
                                 $"Галактический реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)}).");
                        ui.Button($"Подкупить жителей за {Fmt.Money(bribe)} · 1 действие", g.ActionsLeft > 0 && g.Money >= bribe,
                            () => Do(g.BribeMagnateResidents(p.Id, out _)));
                    }
                    break;
            }
        }

        static readonly double[] OfferShares = { 0.8, 0.9, 1.0, 1.1 };

        /// <summary>Торг с жителями: несколько цен с шансом согласия и сделка «за школу».</summary>
        void Negotiation(PanelBuilder ui, Planet p)
        {
            var g = _game;
            if (g.Turn < p.NoTalksUntilTurn)
            {
                ui.Text($"Жители обиделись после отказа и не разговаривают до хода {p.NoTalksUntilTurn}.");
                return;
            }
            ui.Small("Планету продают сами жители. Чем щедрее предложение, тем охотнее согласятся и тем выше будет их лояльность. Отказ тратит действие.");
            var offers = OfferShares.Select(x => new Offer(x)).ToList();
            offers.Add(new Offer(1, withSchool: true));
            foreach (var o in offers)
            {
                double cost = g.OfferCost(p, o);
                string what = o.WithSchool
                    ? $"Сделка «за школу»: {Fmt.Money(g.OfferPrice(p, o))} + школа {Fmt.Money(g.Balance.SchoolCost)}"
                    : $"Предложить {Fmt.Percent(o.PriceShare)} цены: {Fmt.Money(cost)}";
                var offer = o;
                int id = p.Id;
                ui.Button($"{what} · согласятся с шансом {Fmt.Percent(g.OfferChance(p, o))}", g.ActionsLeft > 0 && g.Money >= cost, () =>
                {
                    if (Do(g.MakeOffer(id, offer, out bool accepted)) && accepted)
                        Log($"Сделка с жителями #{id}: планета ваша за {Fmt.Money(g.OfferPrice(g.GetPlanet(id), offer))}.");
                });
            }
        }

        /// <summary>Лояльность, налог, зарплаты, праздник, школа и просьбы жителей своей планеты.</summary>
        void People(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            ui.Space("s");
            ui.Header("Жители");
            ui.Line("Кто", Residents.SpeciesName(Residents.SpeciesOf(Describe(p).Seed)));
            double delta = g.LoyaltyChangePerTurn(p);
            string trend = delta >= 0 ? $"<color=#7be38b>+{Fmt.Amount(delta)}</color>" : $"<color=#ff8a7a>{Fmt.Amount(delta)}</color>";
            ui.Line("Лояльность", $"{Mathf.RoundToInt((float)p.Loyalty)} из 100, за ход {trend}");
            ui.Bar((float)(p.Loyalty / 100));
            if (p.OnStrike)
                ui.Text($"<color=#ff8a7a>Забастовка: ни добычи, ни налогов, пока лояльность не поднимется до {Fmt.Amount(b.StrikeEndsAt)}.</color>");
            else
                ui.Small($"Ниже {Fmt.Amount(b.StrikeBelow)} — забастовка. Шахта снижает лояльность на {Fmt.Amount(b.LoyaltyMiningDrain)} за ход.");

            using (ui.Row())
            {
                ui.Label("Налог", "tax-label");
                var taxNames = new[]
                {
                    "нет",
                    $"низкий +{Fmt.Money(b.TaxIncome[1])} ({Fmt.Amount(b.TaxLoyalty[1])})",
                    $"высокий +{Fmt.Money(b.TaxIncome[2])} ({Fmt.Amount(b.TaxLoyalty[2])})",
                };
                ui.Choice(taxNames, (int)p.Tax, tax => Do(g.SetTax(p.Id, (TaxLevel)tax))).AddToClassList("tax-choice");
            }

            ui.Check(p.PaysWages, $"Платить зарплаты: {Fmt.Money(b.WagesCost)} за ход, лояльность +{Fmt.Amount(b.WagesLoyalty)} за ход",
                wages => Do(g.SetWages(p.Id, wages)));

            ui.Button($"Праздник за {Fmt.Money(b.FestivalCost)}: лояльность +{Fmt.Amount(g.FestivalLoyalty)} · {Doer(Skill.People)}", g.ActionsLeft > 0 && g.Money >= b.FestivalCost,
                () => Act(() => g.HoldFestival(p.Id), $"Праздник на {p.Type.Name} #{p.Id}."));
            if (!p.HasSchool)
                ui.Button($"Построить школу за {Fmt.Money(g.SchoolCost)}: +{Fmt.Amount(b.SchoolLoyalty)} лояльности каждый ход · {Doer(Skill.People)}",
                    g.ActionsLeft > 0 && g.Money >= g.SchoolCost,
                    () => Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}."));
            else
                ui.Small("Школа построена.");
            Education(ui, p);

            foreach (var r in g.Requests.Where(r => r.PlanetId == p.Id).ToList())
                Request(ui, r, p);
            foreach (var x in g.Bribes.Where(x => x.PlanetId == p.Id).ToList())
                Bribe(ui, x, p);
        }

        /// <summary>Образование жителей: специальность, уровень, доход от умственного труда.</summary>
        void Education(PanelBuilder ui, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            ui.Space("s");
            ui.Header("Образование");
            int max = b.EducationCost.Length - 1;
            if (p.Education > 0)
            {
                string natural = p.NaturalSpecialty ? $", склонны к этому: +{Fmt.Percent(b.NaturalSpecialtyBonus)} к доходу" : "";
                ui.Line("Специальность", $"{p.Specialty.RuName()}, уровень {p.Education} из {max}{natural}");
                string mining = p.Mine == MineState.Working && !p.IsDepleted ? $" (пока шахта работает, умом занято {Fmt.Percent(b.IntellectWhileMining)} жителей)" : "";
                ui.Line("Умственный труд", $"+{Fmt.Money(g.IntellectIncome(p))} за ход{mining}");
                ui.Line("Бонус корпорации", p.Specialty.BonusText());
            }
            else
                ui.Small("Образованные жители зарабатывают умственным трудом: меньше, чем шахта, зато всегда, даже пока месторождение восстанавливается. " +
                         "Каждый уровень даёт и бонус всей корпорации. Доход растёт с лояльностью.");

            if (p.IsStudying)
            {
                ui.Text($"Жители учатся: уровень {p.Education + 1} через {p.EducationTurnsLeft} х.");
                return;
            }
            if (p.Education >= max) return;

            int next = p.Education + 1;
            double cost = g.EducationCost(p);
            g.CanEducate(p, out string reason);
            bool canClick = reason == null && g.ActionsLeft > 0;
            if (reason != null && reason != "Не хватает денег")
                ui.Small(reason + ".");
            string income = $"+{Fmt.Money(b.IntellectIncome[next])} за ход при лояльности 50";
            if (p.Education == 0)
            {
                var natural = Specialties.Natural(p.Resource);
                foreach (var sp in Specialties.All)
                {
                    var choice = sp;
                    string tag = sp == natural ? " · склонны" : "";
                    ui.Button($"Учить: {sp.RuName()}{tag} ({sp.BonusText()}) — {Fmt.Money(cost)}, {b.EducationTurns[next]} х. · 1 действие", canClick,
                        () => Act(() => g.Educate(p.Id, choice), $"Жители {p.Type.Name} #{p.Id} учатся: {choice.RuName()}."));
                }
                ui.Small($"Первый уровень: {income}.");
            }
            else
                ui.Button($"Уровень {next}: {income} — {Fmt.Money(cost)}, {b.EducationTurns[next]} х. · 1 действие", canClick,
                    () => Act(() => g.Educate(p.Id), $"Жители {p.Type.Name} #{p.Id} учатся дальше."));
        }

        /// <summary>Подкуп магната: предупреждение и встречный подкуп.</summary>
        void Bribe(PanelBuilder ui, BribeAttempt x, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            var m = g.GetMagnate(x.MagnateId);
            int left = x.ResolveTurn - g.Turn + 1;
            using (ui.Card(alert: true))
            {
                ui.Small($"<color=#ff8a7a><b>{m?.Name} подкупает жителей {p.Type.Name} #{p.Id}!</b></color> Через {left} х. планета уйдёт к магнату, " +
                         $"если лояльность ({Mathf.RoundToInt((float)p.Loyalty)}) не поднимется до {Fmt.Amount(b.BribeSuccessLoyalty)}. Помогут праздник или встречный подкуп.");
                int id = x.Id;
                ui.Button($"Встречный подкуп за {Fmt.Money(b.CounterBribeCost)}: лояльность +{Fmt.Amount(b.CounterBribeLoyalty)}, без действия", g.Money >= b.CounterBribeCost,
                    () => Act(() => g.CounterBribe(id), $"Встречный подкуп на {p.Type.Name} #{p.Id}: жители остаются с вами."));
            }
        }

        /// <summary>Рейдерский иск: срок и кнопка защиты.</summary>
        void Claim(PanelBuilder ui, HostileClaim x, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            var m = g.GetMagnate(x.MagnateId);
            int left = x.ResolveTurn - g.Turn + 1;
            double cost = g.ClaimDefenseCost(p);
            using (ui.Card(alert: true))
            {
                ui.Small($"<color=#ff8a7a><b>{m?.Name} оспаривает в реестре {p.Type.Name} #{p.Id}!</b></color> Через {left} х. планета уйдёт к магнату " +
                         $"за {Fmt.Percent(b.ClaimCompensationShare)} стоимости, если не оплатить юристов.");
                int id = x.Id;
                ui.Button($"Оплатить защиту: {Fmt.Money(cost)}, без действия", g.Money >= cost,
                    () => Act(() => g.DefendClaim(id), $"Иск против {p.Type.Name} #{p.Id} отклонён."));
            }
        }

        void Guards(PanelBuilder ui, Planet p)
        {
            if (!p.Inhabited) return;
            var g = _game;
            var b = g.Balance;
            ui.Check(p.Guarded,
                $"Охрана: {Fmt.Money(b.GuardsCost)} за ход. Магнаты подкупают охраняемых жителей, только если лояльность ниже {Fmt.Amount(b.GuardedBribeLoyalty)}",
                guards => Do(g.SetGuards(p.Id, guards)));
        }

        /// <summary>Ставки на аукционе с шансом выиграть.</summary>
        void Auction(PanelBuilder ui, Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            ui.Small("Аукцион реестра: магнаты тоже ставят. Проиграете — планета уйдёт к ним, действие потрачено." +
                     (silhouette ? " Это силуэт: ставка вслепую." : exact ? "" : " Ставка по оценке реестра."));
            foreach (var f in g.Balance.BidFactors)
            {
                double bid = g.BidPrice(p, f);
                int id = p.Id;
                double factor = f;
                ui.Button($"Ставка {Fmt.Percent(f)}: {Fmt.Money(bid)} · выиграете с шансом {Fmt.Percent(g.AuctionWinChance(p, f))}", g.ActionsLeft > 0 && g.Money >= bid, () =>
                {
                    if (Do(g.PlaceBid(id, factor, out bool won)) && won)
                        Log($"Аукцион выигран: планета #{id} ваша за {Fmt.Money(bid)}.");
                });
            }
        }

        /// <summary>Основное и дополнительные месторождения планеты: что добывается, что восстанавливается.</summary>
        void Deposits(PanelBuilder ui, Planet p, bool exact)
        {
            var g = _game;
            var b = g.Balance;
            ui.Space("s");
            ui.Header("Месторождения");
            string main = p.IsRegenerating
                ? $"восстанавливается, ещё {p.RegenTurnsLeft} из {p.RegenTotal} х."
                : $"добыча {Fmt.Amount(p.Type.OutputPerTurn * (p.Owner == Owner.Player ? 1 + g.OutputBonus : 1))} за ход";
            ui.Line($"{p.Resource.RuName()} (основное)", $"{main}; после истощения восстанавливается за {b.RegenTurns[(int)p.Resource]} х.");
            if (!exact)
            {
                ui.Small("Дополнительные месторождения покажет полная разведка.");
                return;
            }
            if (p.ExtraDeposits.Count == 0)
            {
                ui.Small("Других месторождений нет.");
                return;
            }
            for (int i = 0; i < p.ExtraDeposits.Count; i++)
            {
                var d = p.ExtraDeposits[i];
                string state = d.IsRegenerating ? $"восстанавливается, ещё {d.RegenTurnsLeft} х."
                    : d.HasExtractor ? (g.IsUnlocked(d.Resource) ? $"добывается, запас {Fmt.Amount(d.Reserve)} из {Fmt.Amount(d.Capacity)}" : "экстрактор есть, нет технологии")
                    : $"запас {Fmt.Amount(d.Capacity)}";
                ui.Line($"{d.Resource.RuName()} (доп.)", $"{Fmt.Amount(d.OutputPerTurn)} за ход; {state}; восстановление {b.RegenTurns[(int)d.Resource]} х.");
                if (p.Owner == Owner.Player && !d.HasExtractor)
                {
                    int index = i;
                    double cost = g.ExtractorCost(d);
                    if (!g.IsUnlocked(d.Resource))
                    {
                        var tech = b.Technologies.FirstOrDefault(t => t.UnlocksResource == d.Resource);
                        ui.Small($"Чтобы добывать, нужна технология «{tech?.Name}» (вкладка «Развитие»).");
                    }
                    else
                        ui.Button($"Поставить экстрактор за {Fmt.Money(cost)} · 1 действие", g.ActionsLeft > 0 && g.Money >= cost,
                            () => Act(() => g.BuildExtractor(p.Id, index), $"Экстрактор на {p.Type.Name} #{p.Id}: {d.Resource.RuName()}."));
                }
            }
        }

        void Request(PanelBuilder ui, ResidentRequest r, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            int left = r.DeadlineTurn - g.Turn;
            using (ui.Card())
            {
                if (r.Kind == RequestKind.School)
                {
                    ui.Small($"<b>{p.Type.Name} #{p.Id}</b>: жители просят школу. Осталось ходов: {left}. " +
                             $"Выполните — лояльность +{Fmt.Amount(b.RequestLoyaltyReward)}, нет — −{Fmt.Amount(b.RequestLoyaltyPenalty)}.");
                    ui.Button($"Построить школу за {Fmt.Money(g.SchoolCost)} · {Doer(Skill.People)}", g.ActionsLeft > 0 && g.Money >= g.SchoolCost,
                        () => Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}."));
                }
                else
                {
                    double have = g.Stock(r.Resource);
                    ui.Small($"<b>{p.Type.Name} #{p.Id}</b>: жители просят {Fmt.Amount(r.Amount)} ({r.Resource.RuName()}). Осталось ходов: {left}. " +
                             $"На складе {Fmt.Amount(have)}. Выполните — лояльность +{Fmt.Amount(b.RequestLoyaltyReward)}, нет — −{Fmt.Amount(b.RequestLoyaltyPenalty)}.");
                    int id = r.Id;
                    ui.Button("Отдать со склада биржи", have >= r.Amount, () => Act(() => g.FulfillRequest(id), "Просьба жителей выполнена."));
                }
            }
        }

        /// <summary>Кнопки полной разведки — по одной на тип зонда, с ценой и сроком.</summary>
        void ExploreButtons(PanelBuilder ui, Planet p)
        {
            if (p.Knowledge == Knowledge.Explored) return;
            var g = _game;
            ui.Space("m");
            if (g.ProbeHeadingTo(p.Id))
            {
                ui.Small("К планете уже летит зонд.");
                return;
            }
            ui.Small("Полная разведка покажет точный запас, подвохи и руины:");
            foreach (var t in g.Balance.ProbeTypes)
            {
                bool reach = g.InRange(t, p.MapX, p.MapY);
                string text = reach
                    ? $"Разведать зондом «{t.Name}» за {Fmt.Money(t.Cost)} · {g.ProbeTurns(t, p.MapX, p.MapY, ProbeMission.Explore)} х., 1 действие"
                    : $"«{t.Name}» не долетит";
                var probe = t;
                int id = p.Id;
                ui.Button(text, reach && g.ActionsLeft > 0 && g.Money >= t.Cost,
                    () => Act(() => g.LaunchExplore(probe, id), $"Зонд «{probe.Name}» отправлен на разведку планеты #{id}."));
            }
        }

        // ------------------------------------------------------------------
        // Команда и развитие
        // ------------------------------------------------------------------

        /// <summary>Команда: директор и менеджеры, кто действует, кандидаты на работу.</summary>
        void TeamTab(PanelBuilder ui)
        {
            var g = _game;
            var b = g.Balance;
            ui.Header("Команда");
            ui.Small("Каждое действие выполняет конкретный человек. Любого можно отправить на любое дело, но результат зависит от навыка (1–5, средний 3): " +
                     "переговоры — покупка, ставки, сделки с жителями, продажа планет; геология — шахты, экстракторы, зонды; " +
                     "грязные дела — слухи и подкуп; работа с людьми — праздники, школы, учёба, колонисты.");
            var performer = g.Staff.FirstOrDefault(m => m.Id == g.Current.PerformerId);
            ui.Line("Действует", performer == null ? "подбирается под каждое дело по способностям" : performer.Name);
            if (performer == null)
            {
                // Кто возьмётся за дело сейчас: сильнейший в нём, но специалиста бережём для его собственного дела.
                foreach (var skill in Skills.All)
                {
                    var who = g.WhoActs(skill);
                    ui.Line("   " + skill.RuName(), who == null ? "действий не осталось" : $"{(who.IsDirector ? "вы" : who.Name)}, навык {who.Level(skill)}");
                }
            }
            else
                ui.Button("Подбирать под дело по способностям", true, () => Do(g.SetPerformer(-1)));
            ui.Space("s");

            foreach (var m in g.Staff.ToList())
            {
                using (ui.Card())
                {
                    string salary = m.IsDirector ? "" : $", зарплата {Fmt.Money(m.Salary)}";
                    ui.Text($"<b>{m.Name}</b> — действий {m.ActionsLeft}/{m.ActionsPerTurn}{salary}");
                    ui.Small(SkillsLine(m));
                    using (ui.Row())
                    {
                        int id = m.Id;
                        if (performer != m)
                            ui.Button("Поручать дела ему", m.ActionsLeft > 0, () => Do(g.SetPerformer(id)));
                        if (!m.IsDirector)
                            ui.Button("Уволить", true, () => Act(() => g.FireManager(id), $"{m.Name} уволен(а)."));
                    }
                }
            }

            ui.Space("m");
            ui.Header($"Кандидаты (нанято {g.Managers} из {b.MaxManagers})");
            ui.Small($"Новые кандидаты — раз в {b.CandidateRefreshTurns} ходов. Нанятый работает со следующего хода. Магнаты переманивают лучших.");
            for (int i = 0; i < g.Candidates.Count; i++)
            {
                var c = g.Candidates[i];
                using (ui.Card())
                {
                    ui.Text($"<b>{c.Name}</b> — зарплата {Fmt.Money(c.Salary)} за ход, сильная сторона: {c.BestSkill.RuName()}");
                    ui.Small(SkillsLine(c));
                    int index = i;
                    ui.Button("Нанять · 1 действие", g.ActionsLeft > 0 && g.Managers < b.MaxManagers,
                        () => Act(() => g.HireManager(index), $"Нанят(а) {c.Name}."));
                }
            }
        }

        /// <summary>Дерево технологий: что изучено, что идёт, что можно начать.</summary>
        void DevelopmentTab(PanelBuilder ui)
        {
            var g = _game;
            ui.Header("Исследования");
            if (g.CurrentResearch != null)
            {
                var r = g.CurrentResearch;
                ui.Line("Идёт", $"«{r.Technology.Name}», ещё {r.TurnsLeft} х.");
                ui.Bar(1 - r.TurnsLeft / (float)r.Technology.Turns);
            }
            else
                ui.Small("Сейчас ничего не исследуется. Одно исследование за раз, начало стоит действие.");
            ui.Space("s");

            foreach (var t in g.Balance.Technologies)
            {
                using (ui.Card())
                {
                    string state = g.IsResearched(t.Id) ? "<color=#7be38b>изучено</color>"
                        : g.CurrentResearch?.Technology == t ? "<color=#ffd27a>исследуется</color>"
                        : "";
                    ui.Text($"<b>{t.Name}</b> {state}");
                    ui.Small(t.Description);
                    if (!g.IsResearched(t.Id) && g.CurrentResearch?.Technology != t)
                    {
                        var missing = t.Requires.Where(x => !g.IsResearched(x)).Select(x => g.Balance.Technologies.First(y => y.Id == x).Name).ToList();
                        if (missing.Count > 0)
                            ui.Small("Сначала: " + string.Join(", ", missing));
                        else
                        {
                            string id = t.Id;
                            double cost = g.ResearchCost(t);
                            ui.Button($"Исследовать за {Fmt.Money(cost)}, {t.Turns} х. · 1 действие",
                                g.CurrentResearch == null && g.ActionsLeft > 0 && g.Money >= cost,
                                () => Act(() => g.StartResearch(id), $"Начато исследование «{t.Name}»."));
                        }
                    }
                }
            }

            ui.Space("l");
            ui.Header("Специальности жителей");
            ui.Small("Каждый уровень образования на ваших планетах усиливает бонус своей специальности. Учить жителей можно в окне планеты.");
            var b = g.Balance;
            foreach (var sp in Specialties.All)
            {
                int levels = g.SpecialtyLevels(sp);
                double bonus = g.SpecialtyBonus(sp);
                string value = sp == Specialty.Biologists ? $"+{Fmt.Amount(bonus)} лояльности за ход" : Fmt.Percent(bonus);
                ui.Line(sp.RuName(), $"{sp.BonusText()}: {value} (уровней {levels}, предел {(sp == Specialty.Biologists ? "+" + Fmt.Amount(b.SpecialtyBonusMax[(int)sp]) : Fmt.Percent(b.SpecialtyBonusMax[(int)sp]))})");
            }
            ui.Line("Умственный труд", $"+{Fmt.Money(g.IntellectPerTurn)} за ход");
        }

        // ------------------------------------------------------------------
        // Магнаты
        // ------------------------------------------------------------------

        void MagnatesTab(PanelBuilder ui)
        {
            var g = _game;
            var b = g.Balance;
            ui.Header("Репутация");
            ui.Line("Ваша репутация", $"{Mathf.RoundToInt((float)g.Reputation)} из 100");
            ui.Bar((float)(g.Reputation / 100));
            ui.Small("Грязные дела её портят, выполненные заказы чинят, со временем она возвращается к 50. " +
                     "От репутации зависят согласие жителей и число заказов.");
            if (g.FinesPaid > 0) ui.Line("Штрафы реестра", Fmt.Money(g.FinesPaid));
            int kindness = g.Kindness;
            ui.Line("Как вы обходитесь с жителями", kindness > 0 ? $"<color=#7be38b>добряк ({kindness})</color>" : kindness < 0 ? $"<color=#ff8a7a>живодёр ({kindness})</color>" : "пока никак");
            ui.Small("Магнаты помнят ваши поступки с жителями. Матильда не подкупает жителей у заботливых, Гуго щадит «родственную душу».");
            ui.Line("Внимание магнатов", $"{Fmt.Percent(g.MagnateAttention)}: чем больше у вас планет, тем чаще пакости");
            if (g.Rivalry > 1)
                ui.Small($"<color=#ffd27a>Вы богаче среднего магната в {g.Rivalry:0.#} раза — лидера травят все, пакостей больше.</color>");
            ui.Small($"Жители с лояльностью от {Fmt.Amount(b.PoachScientistsBelowLoyalty)} не дают переманить своих учёных. " +
                     "Обитаемые планеты не оспорить в реестре: жители своих не отдают.");
            if (g.CartelActive)
                ui.Text($"<color=#ff8a7a>Картель магнатов против вас до хода {g.Current.CartelUntilTurn}: биржа платит на {Fmt.Percent(g.CartelCut)} меньше.</color>");
            else if (g.Rivalry >= b.CartelRivalry)
                ui.Small($"<color=#ffd27a>Вы так богаты, что магнаты могут сговориться против вас (картель).</color> Репутация от {Fmt.Amount(b.CartelReputationShield)} смягчит удар вдвое.");
            ui.Space("m");

            foreach (var m in g.Magnates)
            {
                using (ui.Card())
                {
                    int planets = g.MagnatePlanetCount(m);
                    ui.Text($"<b>{m.Name}</b>, {m.PersonalityName}. Планет: {planets}.");
                    string income = m.LastIncome >= 0 ? $"+{Fmt.Money(m.LastIncome)}" : Fmt.Money(m.LastIncome);
                    ui.Small($"Казна {Fmt.Money(m.Money)} ({income} за ход), состояние {Fmt.Money(g.MagnateWealth(m))}.");
                    ui.Small(m.Personality == Personality.Greedy
                        ? "Скупает выгодные планеты, щедро ставит на аукционах, подкупает жителей с низкой лояльностью. Хвалит жёсткость с жителями."
                        : m.Personality == Personality.Chaotic
                            ? "Непредсказуема: ставки вразброс, распускает слухи, переманивает заказчиков и менеджеров."
                            : "Осторожна: ставит скромно, пакостит редко, переманивает заказчиков. Ценит заботу о жителях.");
                    if (g.Turn < m.QuietUntilTurn)
                        ui.Small($"Отбивается от ваших слухов до хода {m.QuietUntilTurn}: не покупает и не пакостит.");
                    var said = g.RecentRemarks.LastOrDefault(x => x.MagnateId == m.Id);
                    if (said != null) ui.Small($"<i>«{said.Text}»</i> (ход {said.Turn})");
                    int id = m.Id;
                    ui.Button($"Распустить слух о магнате за {Fmt.Money(b.PlayerRumorCost)}: {b.PlayerRumorQuietTurns} х. тишины · 1 действие",
                        g.ActionsLeft > 0 && g.Money >= b.PlayerRumorCost, () => Do(g.SpreadRumor(id)));
                    ui.Small($"Грязное дело: репутация {Fmt.Amount(b.PlayerRumorReputation)}, реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)}).");
                }
            }
            Threats(ui);
        }

        /// <summary>Иски и подкупы против игрока — их видно и в «Магнатах», и в «Заказах».</summary>
        void Threats(PanelBuilder ui)
        {
            var g = _game;
            var claims = g.Claims.Where(c => g.IsMine(g.GetPlanet(c.PlanetId))).ToList();
            if (claims.Count > 0)
            {
                ui.Header("Рейдерские иски");
                foreach (var x in claims) Claim(ui, x, g.GetPlanet(x.PlanetId));
            }
            if (g.Bribes.Count > 0)
            {
                ui.Header("Подкупы ваших жителей");
                foreach (var x in g.Bribes.ToList())
                {
                    var p = g.GetPlanet(x.PlanetId);
                    if (p != null) Bribe(ui, x, p);
                }
            }
        }

        // ------------------------------------------------------------------
        // Биржа, заказы, финансы
        // ------------------------------------------------------------------

        void MarketTab(PanelBuilder ui)
        {
            var g = _game;
            if (g.CartelActive)
                ui.Text($"<color=#ff8a7a>Картель магнатов: до хода {g.Current.CartelUntilTurn} вы получаете на {Fmt.Percent(g.CartelCut)} меньше за всё проданное.</color>");
            using (ui.Row())
            {
                ui.Label("Ресурс", "header").AddToClassList("cell-res");
                ui.Label("Склад", "header").AddToClassList("cell-stock");
                ui.Label("Цена", "header").AddToClassList("cell-price");
                ui.Label("Продать", "header");
            }

            foreach (var r in ResourceTypes.All)
            {
                double stock = g.Stock(r);
                double price = g.Market.Price(r);
                double toBase = price / g.Balance.BasePrice(r) - 1;
                string trend = toBase >= 0 ? $"<color=#7be38b>+{Fmt.Percent(toBase)}</color>" : $"<color=#ff8a7a>{Fmt.Percent(toBase)}</color>";
                double transit = g.InTransit(r);
                var resource = r;
                using (ui.Row())
                {
                    ui.Label(r.RuName(), "cell-res");
                    ui.Label(transit > 0 ? $"{Fmt.Amount(stock)} <color=#8a93a6>+{Fmt.Amount(transit)}</color>" : Fmt.Amount(stock), "cell-stock");
                    ui.Label($"{Fmt.Money(price)} {trend}", "cell-price");
                    double half = Math.Floor(stock / 2);
                    ui.Button($"½ ≈ {Fmt.Money(g.Quote(r, half))}", half >= 1,
                        () => Act(() => g.SellResource(resource, half), $"Продано {Fmt.Amount(half)} ({resource.RuName()}).")).AddToClassList("market-button");
                    double revenue = g.Quote(r, stock);
                    ui.Button($"всё ≈ {Fmt.Money(revenue)}", stock > 0,
                        () => Act(() => g.SellResource(resource, stock), $"Продано {Fmt.Amount(stock)} ({resource.RuName()}) за {Fmt.Money(revenue)}.")).AddToClassList("market-button");
                }
            }

            ui.Space("l");
            var b = g.Balance;
            ui.Small($"Каждая продажа роняет цену: на {Fmt.Percent(b.SalesImpact)}, если продать столько, сколько составляет глубина рынка. " +
                     $"За ход цена возвращается к базовой на {Fmt.Percent(b.PriceRecovery)} разрыва. " +
                     "Глубина: " + string.Join(", ", ResourceTypes.All.Select(r => $"{r.RuName()} {Fmt.Amount(b.Depth(r))}")) + ". " +
                     "Серым после склада — груз в пути с дальних планет.");
        }

        void OrdersTab(PanelBuilder ui)
        {
            var g = _game;
            Threats(ui);
            if (g.Bribes.Count > 0) ui.Space("m");
            if (g.Requests.Count > 0)
            {
                ui.Header("Просьбы жителей");
                foreach (var r in g.Requests.ToList())
                {
                    var p = g.GetPlanet(r.PlanetId);
                    if (p != null) Request(ui, r, p);
                }
                ui.Space("m");
                ui.Header("Заказы");
            }
            if (g.Orders.Count == 0)
            {
                ui.Text("Заказов пока нет. Новые появляются примерно каждые два хода.");
                return;
            }

            foreach (var o in g.Orders.ToList())
            {
                double have = g.Stock(o.Resource);
                int turnsLeft = o.DeadlineTurn - g.Turn;
                bool reachable = g.OrderReachable(o);
                using (ui.Card())
                {
                    ui.Text($"<b>{o.Customer}</b>: {Fmt.Amount(o.Amount)} ({o.Resource.RuName()})");
                    ui.Small($"Срок через {turnsLeft} х., доставка {g.DeliveryTurns(o)} х. за {Fmt.Money(g.DeliveryCost(o))}. " +
                             $"Заплатят ≈ {Fmt.Money(o.GrossPayout(g.Market, g.Balance))} (×{g.Balance.OrderMarkup} к бирже), " +
                             $"на руки ≈ {Fmt.Money(g.OrderNetPayout(o))}. На складе {Fmt.Amount(have)}.");
                    var order = o;
                    ui.Button(reachable ? "Отправить" : "Не успеть к сроку", reachable && have >= o.Amount, () =>
                    {
                        double payout = g.OrderNetPayout(order);
                        if (Do(g.FulfillOrder(order.Id)))
                            Log($"Заказ отправлен: {order.Customer} заплатил {Fmt.Money(payout)} за вычетом доставки.");
                    });
                }
            }
        }

        void FinanceTab(PanelBuilder ui)
        {
            var g = _game;
            var b = g.Balance;

            ui.Header("Кредит");
            ui.Line("Долг", $"{Fmt.Money(g.Debt)}, {Fmt.Percent(b.InterestPerTurn)} за ход");
            ui.Line("Лимит", $"{Fmt.Money(g.CreditLimit)} ({Fmt.Percent(b.CreditLimitShare)} стоимости планет)");
            ui.Line("Можно занять", Fmt.Money(g.AvailableCredit));
            using (ui.Row())
            {
                ui.Button("Взять 500", g.AvailableCredit >= 500, () => Act(() => g.TakeLoan(500), "Взято в долг 500."));
                double all = Math.Floor(g.AvailableCredit);
                ui.Button($"Взять {Fmt.Money(all)}", all >= 1, () => Act(() => g.TakeLoan(all), $"Взято в долг {Fmt.Money(all)}."));
            }
            using (ui.Row())
            {
                double repay = Math.Min(500, g.Debt);
                ui.Button("Погасить 500", g.Debt > 0 && g.Money >= repay, () => Act(() => g.RepayLoan(repay), $"Погашено {Fmt.Money(repay)}."));
                double repayAll = Math.Min(g.Debt, Math.Floor(g.Money));
                ui.Button($"Погасить {Fmt.Money(repayAll)}", repayAll >= 1, () => Act(() => g.RepayLoan(repayAll), $"Погашено {Fmt.Money(repayAll)}."));
            }
            ui.Small("Если денег на расходы не хватит, недостача уйдёт в долг. Когда долг выше лимита, коллекторы забирают самые дорогие планеты.");

            ui.Space("l");
            ui.Header("Страховка");
            double insurance = b.InsuranceRate * g.PlanetsValue;
            ui.Check(g.Insured,
                $"Страховать планеты: {Fmt.Money(insurance)} за ход ({Fmt.Percent(b.InsuranceRate)} стоимости). Возмещает добычу при авариях и эпидемиях и шахту, разбитую метеоритом. Бунты не покрывает.",
                insured =>
                {
                    g.SetInsurance(insured);
                    Log(insured ? "Страховка включена." : "Страховка отключена.");
                });

            ui.Space("l");
            ui.Header("Менеджеры");
            ui.Line("Нанято", $"{g.Managers} из {b.MaxManagers}, зарплаты {Fmt.Money(g.SalariesPerTurn)} за ход — подробнее во вкладке «Команда»");

            ui.Space("l");
            ui.Header("Расходы за ход");
            ui.Line("Содержание планет", $"{Fmt.Money(g.UpkeepPerTurn)} ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за {g.UpkeepCurvePlanets} раб. планет)");
            ui.Line("Зарплаты", Fmt.Money(g.SalariesPerTurn));
            ui.Line("Проценты", Fmt.Money(g.Debt * b.InterestPerTurn));
            ui.Line("Страховка", Fmt.Money(g.InsurancePerTurn));
            ui.Line("Доставка на биржу", Fmt.Money(g.ShippingPerTurn));
            ui.Line("Зарплаты жителям", Fmt.Money(g.WagesPerTurn));
            ui.Line("Охрана", Fmt.Money(g.GuardsPerTurn));
            ui.Line("<b>Итого</b>", $"<b>{Fmt.Money(g.CostsPerTurn)}</b>");
            ui.Line("Налоги с жителей", $"+{Fmt.Money(g.TaxesPerTurn)} за ход");
            ui.Line("Умственный труд", $"+{Fmt.Money(g.IntellectPerTurn)} за ход");
        }
    }
}
