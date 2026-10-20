using System.Collections.Generic;

namespace PlanetTrader.Core
{
    // ------------------------------------------------------------------
    // Шоу «Риелтор года»: цель партии. Сезон — несколько десятков ходов и несколько заданий на очки;
    // участники — игроки и самые богатые магнаты их секторов. В конце сезона — рейтинг, кубок и призы.
    // Сезоны идут один за другим, пока идёт партия.
    // ------------------------------------------------------------------

    public enum ShowTaskKind
    {
        BuyPlanets,
        /// <summary>Продать планету, которая принесла больше, чем в неё вложено.</summary>
        FlipPlanet,
        /// <summary>Вырастить капитал на столько-то процентов за сезон.</summary>
        GrowCapital,
        WinLot,
        SettleColony,
        /// <summary>Сколько уровней образования получат жители.</summary>
        Educate,
        FindHidden,
        ExplorePlanets,
        FulfillOrders,
        Research,
        Festivals,
        /// <summary>Выручка от продажи ресурсов на бирже.</summary>
        SellOnExchange,
    }

    /// <summary>Задание шоу из баланса: что сделать, сколько очков и как цель растёт с уровнем корпорации.</summary>
    public sealed class ShowTaskSpec
    {
        public ShowTaskKind Kind;
        public string Title;
        /// <summary>Что сделать; {0} — цель (числом или числом со словом из Noun).</summary>
        public string Text;
        /// <summary>Слово для цели: одна планета, две планеты, пять планет; null — просто число.</summary>
        public string[] Noun;
        public double Target;
        /// <summary>Прибавка к цели за каждый уровень корпорации (может быть и меньше нуля).</summary>
        public double TargetPerLevel;
        public int Points;
        public double Weight = 1;
    }

    /// <summary>Счётчики корпорации за всю партию: по ним считается, что сделано за сезон.</summary>
    public sealed class ShowStats
    {
        public int PlanetsBought;
        public int ProfitableSales;
        public int LotsWon;
        public int Colonies;
        public int EducationLevels;
        public int HiddenFound;
        public int PlanetsExplored;
        public int OrdersFulfilled;
        public int ResearchDone;
        public int Festivals;
        public double SoldOnExchange;
        public int IncidentsSeen;

        public ShowStats Copy() => (ShowStats)MemberwiseClone();
    }

    /// <summary>Задание текущего сезона: из баланса и с целью под уровень участников.</summary>
    public sealed class ShowTask
    {
        public ShowTaskSpec Spec;
        public double Target;
    }

    /// <summary>Участник сезона: игрок (CorpId) или магнат (MagnateId).</summary>
    public sealed class ShowEntry
    {
        public int CorpId = -1;
        public int MagnateId = -1;
        public string Name;
        public double Points;
        /// <summary>Очки не за задания: эфиры из случаев.</summary>
        public double BonusPoints;
        public double StartCapital;
        /// <summary>Счётчики на начало сезона.</summary>
        public ShowStats Start;
        /// <summary>Номера выполненных заданий сезона.</summary>
        public List<int> Done = new List<int>();

        public bool IsPlayer => CorpId >= 0;
    }

    public sealed class ShowSeason
    {
        public int Number;
        public int StartTurn;
        /// <summary>Последний ход сезона: в его конце — итоги.</summary>
        public int EndTurn;
        public List<ShowTask> Tasks = new List<ShowTask>();
        public List<ShowEntry> Entries = new List<ShowEntry>();
    }

    // ------------------------------------------------------------------
    // Случаи: раз в несколько ходов к игроку приходит гость, сделка или беда с вариантами ответа.
    // ------------------------------------------------------------------

    /// <summary>О ком случай: ни о ком, о вашей планете (любой, с шахтой, обитаемой), о магнате или о планете и магнате.</summary>
    public enum IncidentTarget
    {
        None,
        MyPlanet,
        MyMine,
        MyInhabited,
        Magnate,
        MyPlanetAndMagnate,
    }

    /// <summary>Чем кончился выбор. Деньги умножаются на масштаб уровня корпорации, остальное — как есть.</summary>
    public sealed class IncidentOutcome
    {
        public string Text;
        public double Money;
        public double Reputation;
        /// <summary>Лояльность жителей планеты случая (если они есть).</summary>
        public double Loyalty;
        /// <summary>Ресурс на склад биржи (или со склада, если меньше нуля).</summary>
        public double Stock;
        /// <summary>Какой ресурс; null — ресурс планеты случая.</summary>
        public Resource? Resource;
        /// <summary>Доля ёмкости месторождения, которая прибавится к запасу.</summary>
        public double ReserveShare;
        public int AccidentTurns;
        public double ShowPoints;
        /// <summary>Магнат случая столько ходов не пакостит.</summary>
        public int QuietTurns;
        /// <summary>Продать планету случая магнату случая за рыночную стоимость × SalePremium.</summary>
        public double SalePremium;
    }

    public sealed class IncidentChoice
    {
        public string Label;
        /// <summary>Сколько стоит (с масштабом уровня); без денег вариант недоступен.</summary>
        public double Cost;
        /// <summary>Шанс на Success; иначе — Failure.</summary>
        public double Chance = 1;
        public IncidentOutcome Success = new IncidentOutcome();
        public IncidentOutcome Failure;
    }

    /// <summary>Случай из баланса. Последний вариант — «ничего не делать»: его выбирают за игрока, если он промолчал.</summary>
    public sealed class Incident
    {
        public string Id;
        public string Title;
        /// <summary>Текст; {planet}, {magnate}, {resource}, {price} подставляются.</summary>
        public string Text;
        /// <summary>Значок карточки.</summary>
        public string Icon = "info";
        public IncidentTarget Target;
        public IncidentChoice[] Choices;
        public double Weight = 1;
        public int MinTurn;
    }

    /// <summary>Случай, который ждёт ответа игрока.</summary>
    public sealed class ActiveIncident
    {
        public Incident Def;
        public int PlanetId = -1;
        public int MagnateId = -1;
        public int Turn;
        /// <summary>Множитель денег: растёт с уровнем корпорации.</summary>
        public double Scale = 1;
    }

    // ------------------------------------------------------------------
    // Лот недели: легендарная планета на закрытых торгах против всех магнатов сектора.
    // ------------------------------------------------------------------

    public sealed class LegendaryPlanet
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Ресурс; null — любой ресурс сектора.</summary>
        public Resource? Resource;
        /// <summary>Только в этом секторе; −1 — в любом.</summary>
        public int OnlySector = -1;
        public double ReserveFactor = 1;
        public bool Inhabited;
        public double Loyalty = 50;
        public int Education;
        public Specialty Specialty;
        public bool HasSchool;
        public double Ruins;
        public int ExtraDeposits;
        public bool MineBuilt;
    }

    public sealed class LotBid
    {
        public int CorpId;
        public double Amount;
        /// <summary>Вес ставки: переговорщик убедительнее, хотя платит названную сумму.</summary>
        public double Strength;
    }

    public sealed class LotAuction
    {
        public int PlanetId;
        public string Legend;
        public int AnnounceTurn;
        /// <summary>В конце этого хода — торги.</summary>
        public int ResolveTurn;
        public double StartPrice;
        /// <summary>Магнаты, которые придут на торги (их видно заранее, ставки — нет).</summary>
        public List<int> Interested = new List<int>();
        public List<LotBid> Bids = new List<LotBid>();
    }
}
