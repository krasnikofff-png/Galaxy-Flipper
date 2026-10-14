namespace PlanetTrader.Core
{
    /// <summary>
    /// Уровень корпорации (этап 8). Растёт вместе с капиталом и никогда не падает: прогресс не сбрасывается.
    /// Каждый уровень что-то открывает: больше менеджеров, лучшие зонды, дальше — лицензии на новые секторы и постройки.
    /// </summary>
    public sealed class CorporationLevel
    {
        public string Name;
        /// <summary>С какого капитала открывается.</summary>
        public double Capital;
        /// <summary>Сколько менеджеров можно держать, не считая директора.</summary>
        public int Managers;

        public CorporationLevel(string name, double capital, int managers)
        {
            Name = name;
            Capital = capital;
            Managers = managers;
        }
    }

    public static class Words
    {
        /// <summary>Число со словом в нужной форме: 1 менеджер, 2 менеджера, 5 менеджеров.</summary>
        public static string Count(int n, string one, string few, string many)
        {
            int last2 = n % 100, last = n % 10;
            string word = last2 >= 11 && last2 <= 14 ? many : last == 1 ? one : last >= 2 && last <= 4 ? few : many;
            return $"{n} {word}";
        }
    }
}
