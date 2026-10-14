using System;

namespace PlanetTrader.Core
{
    /// <summary>Навыки сотрудников. Любого можно отправить на любое дело, но результат зависит от навыка.</summary>
    public enum Skill
    {
        /// <summary>Покупка планет, ставки, сделки с жителями, продажа планет.</summary>
        Negotiation,
        /// <summary>Шахты, экстракторы, зонды.</summary>
        Geology,
        /// <summary>Слухи о магнатах, подкуп чужих жителей: реже ловят, чаще удаётся.</summary>
        Shady,
        /// <summary>Праздники, школы, учёба, колонисты.</summary>
        People,
    }

    public static class Skills
    {
        public static readonly Skill[] All = { Skill.Negotiation, Skill.Geology, Skill.Shady, Skill.People };

        public static string RuName(this Skill s)
        {
            switch (s)
            {
                case Skill.Negotiation: return "переговоры";
                case Skill.Geology: return "геология";
                case Skill.Shady: return "грязные дела";
                default: return "работа с людьми";
            }
        }
    }

    /// <summary>Сотрудник корпорации: директор (игрок или магнат) или нанятый менеджер.</summary>
    public sealed class Manager
    {
        public const int MinSkill = 1, MaxSkill = 5, AverageSkill = 3;

        public readonly int Id;
        public string Name;
        /// <summary>Навыки 1–5 по порядку enum Skill; 3 — средний, ничего не меняет.</summary>
        public readonly int[] SkillLevels;
        /// <summary>Зарплата за ход; у директора 0.</summary>
        public double Salary;
        /// <summary>Сколько действий за ход: у директора несколько, у менеджера одно.</summary>
        public int ActionsPerTurn;
        public int ActionsLeft;
        public bool IsDirector;

        public Manager(int id, string name, int[] skills, double salary, int actionsPerTurn, bool isDirector = false)
        {
            Id = id;
            Name = name;
            SkillLevels = skills;
            Salary = salary;
            ActionsPerTurn = actionsPerTurn;
            ActionsLeft = actionsPerTurn;
            IsDirector = isDirector;
        }

        public int Level(Skill s) => SkillLevels[(int)s];

        /// <summary>Насколько сотрудник лучше среднего в этом деле: от −2 до +2.</summary>
        public int Edge(Skill s) => Level(s) - AverageSkill;

        public int TotalSkill
        {
            get
            {
                int sum = 0;
                foreach (var x in SkillLevels) sum += x;
                return sum;
            }
        }

        public Skill BestSkill
        {
            get
            {
                var best = Skill.Negotiation;
                foreach (var s in Skills.All)
                    if (Level(s) > Level(best)) best = s;
                return best;
            }
        }

        /// <summary>Директор: навыки персонажа или средние.</summary>
        public static Manager Director(int id, string name, int actions, int[] skills = null) =>
            new Manager(id, name, skills != null ? (int[])skills.Clone() : new[] { AverageSkill, AverageSkill, AverageSkill, AverageSkill }, 0, actions, isDirector: true);

        static readonly string[] FirstNames = { "Аркадий", "Василиса", "Гектор", "Дана", "Евлампий", "Жанна", "Игнат", "Кира", "Лев", "Марта", "Нестор", "Олимпия", "Прохор", "Рада", "Савелий", "Тамара" };
        static readonly string[] LastNames = { "Орбитальный", "Пылевая", "Кольцов", "Туманность", "Вакуумов", "Спиральная", "Кометкин", "Протуберанец", "Сверхновая", "Гравитонов", "Астролябина", "Парсеков" };

        /// <summary>
        /// Кандидат на работу: навыки вокруг среднего, один — сильная сторона. Зарплата растёт с суммой навыков.
        /// </summary>
        public static Manager Candidate(int id, Rng rng, double baseSalary, double salaryPerSkillPoint)
        {
            var skills = new int[Skills.All.Length];
            for (int i = 0; i < skills.Length; i++) skills[i] = rng.Range(MinSkill, MaxSkill); // 1–4
            int strong = rng.Range(0, skills.Length);
            skills[strong] = Math.Min(MaxSkill, skills[strong] + 2);
            string name = FirstNames[rng.Range(0, FirstNames.Length)] + " " + LastNames[rng.Range(0, LastNames.Length)];
            int sum = 0;
            foreach (var x in skills) sum += x;
            double salary = Math.Round(baseSalary * Math.Max(0.4, 1 + salaryPerSkillPoint * (sum - AverageSkill * skills.Length)));
            return new Manager(id, name, skills, salary, 1);
        }

        public override string ToString() => Name;
    }
}
