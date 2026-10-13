namespace PlanetTrader.Core
{
    /// <summary>
    /// Что магнаты говорят о поступках игрока с жителями. Жадный хвалит жёсткость и высмеивает доброту,
    /// осторожная — наоборот, хаотичная — что-то своё. {p} — название планеты.
    /// Тексты — черновик: автор перепишет под свой тон.
    /// </summary>
    public static class Remarks
    {
        public static string For(Personality who, DeedKind kind)
        {
            switch (who)
            {
                case Personality.Greedy: return Greedy(kind);
                case Personality.Cautious: return Cautious(kind);
                default: return Chaotic(kind);
            }
        }

        static string Greedy(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.Festival: return "Праздник на {p}? Я в ваши годы раздавал жителям только квитанции.";
                case DeedKind.School: return "Школа на {p}? Грамотные жители начинают читать договоры. Опасный путь.";
                case DeedKind.Wages: return "Платите жителям {p} зарплату? Может, ещё и отпуск им дадите?";
                case DeedKind.RequestDone: return "Исполняете капризы жителей {p}? Сегодня лёд, завтра луну с неба.";
                case DeedKind.RequestFailed: return "Проигнорировали жителей {p}? Вот это хватка. Узнаю себя в молодости.";
                case DeedKind.HighTax: return "Высокий налог на {p} — музыка для моих ушей. Жители созданы, чтобы платить.";
                case DeedKind.Strike: return "Забастовка на {p}? Слабаки. У меня бастуют только роботы, и те молча.";
                case DeedKind.Riot: return "Бунт на {p}. Шахту жалко, жителей — нет.";
                case DeedKind.LowballDeal: return "Выторговали {p} за бесценок? Браво. Торговаться с жителями — моё любимое хобби.";
                case DeedKind.GenerousDeal: return "Переплатили жителям за {p}? Щедрость — болезнь, и вы больны.";
                case DeedKind.BribedRivalResidents: return "Увели жителей у конкурента? Красиво. Почти как я.";
                case DeedKind.CounterBribe: return "Перекупили собственных жителей на {p}? Дорого обходится любовь народа.";
                default: return null;
            }
        }

        static string Cautious(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.Festival: return "Праздник на {p} — правильно. Довольные жители и копают лучше.";
                case DeedKind.School: return "Школа на {p} — вложение на десятилетия. Уважаю.";
                case DeedKind.Wages: return "Зарплаты на {p}. Наконец-то кто-то в этом секторе ведёт себя по-человечески.";
                case DeedKind.RequestDone: return "Вы выполнили просьбу жителей {p}. Они это запомнят. И я тоже.";
                case DeedKind.RequestFailed: return "Жители {p} просили, а вы не услышали. Плохая примета для бизнеса.";
                case DeedKind.HighTax: return "Высокий налог на {p}... Жители — не дойная комета.";
                case DeedKind.Strike: return "На {p} бастуют. Довели людей, поздравляю.";
                case DeedKind.Riot: return "Бунт на {p}. Это уже не бизнес, это беда.";
                case DeedKind.LowballDeal: return "Купили {p} за копейки у её же жителей. Они это запомнят.";
                case DeedKind.GenerousDeal: return "Честная цена за {p}. Жители вам поверили, не подведите их.";
                case DeedKind.BribedRivalResidents: return "Подкуп жителей... Вы ничем не лучше Гуго.";
                case DeedKind.CounterBribe: return "Вы не отдали жителей {p} без боя. Достойно.";
                default: return null;
            }
        }

        static string Chaotic(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.Festival: return "Праздник на {p}! Надеюсь, был торт в форме астероида. Если нет — я обижена.";
                case DeedKind.School: return "Школа на {p}... А гравитацию там преподают? Я её так и не поняла.";
                case DeedKind.Wages: return "Зарплата на {p}! Я тоже хочу. Можно я поработаю у вас один день?";
                case DeedKind.RequestDone: return "Жители {p} довольны — значит, скоро попросят ещё. Обожаю цепные реакции.";
                case DeedKind.RequestFailed: return "Жители {p} обиделись. Я бы тоже обиделась, но не помню, на что.";
                case DeedKind.HighTax: return "Налоги на {p} подняли? А я свои вообще не плачу. Шучу. Реестр, это шутка.";
                case DeedKind.Strike: return "Забастовка на {p}! Можно я принесу им плакаты? У меня есть лишние.";
                case DeedKind.Riot: return "На {p} разнесли шахту! Красиво хоть было? Пришлите запись.";
                case DeedKind.LowballDeal: return "Взяли {p} со скидкой? Я однажды купила планету за пуговицу. Потом выяснилось, что пуговица была планетой.";
                case DeedKind.GenerousDeal: return "Заплатили за {p} больше, чем просили? Вы мне нравитесь. Или пугаете. Ещё не решила.";
                case DeedKind.BribedRivalResidents: return "Подкупили чужих жителей! А меня подкупить не хотите? Я недорого.";
                case DeedKind.CounterBribe: return "Подкуп против подкупа! Жители {p} теперь самые богатые в секторе.";
                default: return null;
            }
        }

        /// <summary>Общее мнение о том, как игрок обходится с жителями, когда свежих поступков нет.</summary>
        public static string Overall(Personality who, int kindness)
        {
            bool kind = kindness > 0;
            switch (who)
            {
                case Personality.Greedy:
                    return kind ? "Вы слишком добры к жителям. Это лечится, обращайтесь." : "Мне нравится ваш стиль: жители пусть терпят.";
                case Personality.Cautious:
                    return kind ? "О ваших планетах хорошо говорят. В нашем секторе это редкость." : "О ваших планетах ходят нехорошие слухи. И не я их распускаю.";
                default:
                    return kind ? "Ваши жители улыбаются. Подозрительно." : "Ваши жители хмурятся. Может, им просто холодно?";
            }
        }
    }
}
