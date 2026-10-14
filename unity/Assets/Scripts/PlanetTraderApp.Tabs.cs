using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>Вкладки правой панели: планета, биржа, заказы, финансы, команда, развитие, магнаты.</summary>
    public sealed partial class PlanetTraderApp
    {
        // ------------------------------------------------------------------
        // Угрозы и просьбы (их видно и на планете, и в «Заказах», и в «Магнатах»)
        // ------------------------------------------------------------------

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
            ui.Header($"Кандидаты (нанято {g.Managers} из {g.MaxManagers})");
            ui.Small($"Новые кандидаты — раз в {b.CandidateRefreshTurns} ходов. Нанятый работает со следующего хода. Магнаты переманивают лучших.");
            if (g.NextLevelWith(l => l.Managers > g.MaxManagers) is CorporationLevel more)
                ui.Small($"Сколько менеджеров держать, зависит от уровня корпорации: с уровня «{more.Name}» (капитал от {Fmt.Money(more.Capital)}) — до {more.Managers}.");
            for (int i = 0; i < g.Candidates.Count; i++)
            {
                var c = g.Candidates[i];
                using (ui.Card())
                {
                    ui.Text($"<b>{c.Name}</b> — зарплата {Fmt.Money(c.Salary)} за ход, сильная сторона: {c.BestSkill.RuName()}");
                    ui.Small(SkillsLine(c));
                    int index = i;
                    ui.Button("Нанять · 1 действие", g.ActionsLeft > 0 && g.Managers < g.MaxManagers,
                        () => Act(() => g.HireManager(index), $"Нанят(а) {c.Name}."));
                }
            }
        }

        /// <summary>Уровень корпорации: где мы, сколько до следующего и что он откроет; ниже — все уровни.</summary>
        void LevelSection(PanelBuilder ui)
        {
            var g = _game;
            var levels = g.Balance.CorporationLevels;
            ui.Header("Уровень корпорации");
            using (ui.Card())
            {
                ui.Text($"<b>{g.LevelInfo.Name}</b> — уровень {g.Level + 1} из {levels.Length}");
                var next = g.NextLevel;
                if (next != null)
                {
                    ui.Add(PanelBuilder.MakeBar(LevelProgress(), new Color(0.84f, 0.64f, 0.31f)).WithClass("wide"));
                    ui.Line($"До «{next.Name}»", $"капитал {Fmt.Money(g.Capital)} из {Fmt.Money(next.Capital)}");
                    ui.Line("Откроет", string.Join(", ", g.LevelUnlocks(g.Level + 1)));
                }
                else
                    ui.Small("Выше уровня пока нет.");
                ui.Small("Уровень растёт с капиталом и не падает, даже если дела пойдут хуже. Проверяется в конце хода.");
            }
            using (ui.Fold("Все уровни", "levels"))
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    string state = i < g.Level ? "<color=#7be38b>пройден</color>" : i == g.Level ? "<color=#ffd27a>сейчас</color>" : $"капитал от {Fmt.Money(levels[i].Capital)}";
                    ui.Line($"{i + 1}. {levels[i].Name}", $"{state} — {string.Join(", ", g.LevelUnlocks(i))}");
                }
            }
        }

        /// <summary>Сколько пройдено от порога нынешнего уровня до следующего, 0–1; на последнем — 1.</summary>
        float LevelProgress()
        {
            var g = _game;
            var next = g.NextLevel;
            if (next == null) return 1;
            double from = g.LevelInfo.Capital;
            return Mathf.Clamp01((float)((g.Capital - from) / (next.Capital - from)));
        }

        /// <summary>Дерево технологий: что изучено, что идёт, что можно начать.</summary>
        void DevelopmentTab(PanelBuilder ui)
        {
            var g = _game;
            LevelSection(ui);
            ui.Space("l");
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
            ui.Line("Нанято", $"{g.Managers} из {g.MaxManagers}, зарплаты {Fmt.Money(g.SalariesPerTurn)} за ход — подробнее во вкладке «Команда»");

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
