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
                case Personality.Upstart: return Upstart(kind);
                case Personality.Heir: return Heir(kind);
                // Шейх о чужих делах не говорит.
                case Personality.Sheikh: return null;
                default: return Chaotic(kind);
            }
        }

        static string Upstart(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.Festival: return "Праздник на {p}? Ой, тоже мне баре. Я на свои праздники гостей не зову — дорого.";
                case DeedKind.School: return "Школу на {p} отгрохали? Я вот без школ разбогатела, и ничего, живу не тужу.";
                case DeedKind.Wages: return "Зарплату жителям {p}? Балуете! Им дай палец — они всю руку с кольцами отхватят.";
                case DeedKind.RequestDone: return "Бегаете за жителями {p} с подносиком? Ну-ну. У меня они сами бегают.";
                case DeedKind.RequestFailed: return "Плюнули на просьбу жителей {p}? Вот это по-нашему, по-деревенски!";
                case DeedKind.HighTax: return "Налог на {p} подняли? Правильно, нечего жировать. Я своих вообще стригу под ноль.";
                case DeedKind.Strike: return "На {p} бастуют? Да я б их всех на картошку отправила!";
                case DeedKind.Riot: return "Разнесли шахту на {p}? Хе-хе. А у меня шахты крепкие, я на них не экономлю. Почти.";
                case DeedKind.LowballDeal: return "Сбили цену за {p}? Уважаю! Торговаться я тоже люблю — громко и до слёз.";
                case DeedKind.GenerousDeal: return "Переплатили за {p}? Ну вы и простофиля. Дайте и мне так переплатить.";
                case DeedKind.BribedRivalResidents: return "Увели чужих жителей! Ух, бойкий. Только моих не трожь, понял?";
                case DeedKind.CounterBribe: return "Перекупили своих же жителей на {p}? Деньги девать некуда — отдайте мне.";
                case DeedKind.Education: return "Учите жителей {p}? Умничать начнут. Мои вот считать не умеют, и слава космосу.";
                case DeedKind.Colony: return "Колонистов на {p} завезли? Ну, хоть кто-то будет на огороде работать.";
                default: return null;
            }
        }

        static string Heir(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.Festival: return "Праздник на {p}? Мило. Мой последний приём обошёлся дороже вашей планеты.";
                case DeedKind.School: return "Школа на {p}? Я учился в Академии Туманности Андромеды. Впрочем, вам не понять.";
                case DeedKind.Wages: return "Зарплаты на {p}... Папа говорил, что жителям надо платить ровно столько, чтобы не ушли. Папа был прав. Как и я.";
                case DeedKind.RequestDone: return "Исполняете просьбы жителей {p}? Мои жители просят только автограф.";
                case DeedKind.RequestFailed: return "Не выполнили просьбу на {p}? Бывает у тех, кто всего добился сам. То есть не у меня, у меня всё получается.";
                case DeedKind.HighTax: return "Высокий налог на {p}? Как вульгарно. Я просто получаю дивиденды.";
                case DeedKind.Strike: return "Забастовка на {p}? У меня такого не бывает. Мои жители меня обожают. Я спрашивал.";
                case DeedKind.Riot: return "Бунт на {p}. Позвоните моим юристам — они и не такое улаживали. Хотя нет, они заняты моими делами.";
                case DeedKind.LowballDeal: return "Купили {p} со скидкой? Я никогда не смотрю на ценник. Это для бедных.";
                case DeedKind.GenerousDeal: return "Заплатили за {p} щедро? Наконец-то хоть кто-то ведёт себя как я.";
                case DeedKind.BribedRivalResidents: return "Подкупили чужих жителей? Грубовато. Я предпочитаю, чтобы они сами ко мне просились.";
                case DeedKind.CounterBribe: return "Перекупили жителей на {p}? Мило. Я бы купил всю планету вместе с покупателем.";
                case DeedKind.Education: return "Учите жителей {p}? Похвально. Пусть хоть кто-то из них однажды станет похож на меня.";
                case DeedKind.Colony: return "Колонисты на {p}? Отец основал сорок колоний. Я пока ни одной, но у меня впереди вся жизнь.";
                default: return null;
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
                case DeedKind.Education: return "Учите жителей {p}? Умный работник — дорогой работник. И язвительный.";
                case DeedKind.Colony: return "Заселили {p}? Новые рты, новые налоги. Одобряю, если налоги высокие.";
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
                case DeedKind.Education: return "Жители {p} учатся — значит, планета переживёт свои шахты. Мудро.";
                case DeedKind.Colony: return "Колонисты на {p}. Берегите их, первым поселенцам всегда труднее всех.";
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
                case DeedKind.Education: return "На {p} открыли академию? Запишите меня! Хочу на факультет чёрных дыр, там тихо.";
                case DeedKind.Colony: return "Колонисты на {p}! Надеюсь, они взяли с собой зонтики. Там же есть дождь? Ну хоть что-нибудь?";
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
                case Personality.Upstart:
                    return kind ? "Добренький вы. Добреньких у нас в деревне первыми и обдирали." : "Жители у вас хмурые, как мои соседи, когда я разбогатела. Красота!";
                case Personality.Heir:
                    return kind ? "Ваши жители довольны. Почти как мои. Почти." : "О ваших планетах говорят плохо. Обо мне говорят только хорошее — я плачу за это.";
                case Personality.Sheikh:
                    return kind ? "(молча кивает в вашу сторону)" : "(долго смотрит на вас и ничего не говорит)";
                default:
                    return kind ? "Ваши жители улыбаются. Подозрительно." : "Ваши жители хмурятся. Может, им просто холодно?";
            }
        }
    }
}
