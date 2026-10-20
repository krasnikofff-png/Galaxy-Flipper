using System;
using PlanetTrader.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>Экран выбора стартового персонажа: при запуске и из меню «Новая игра».</summary>
    public sealed partial class PlanetTraderApp
    {
        static readonly string[] CharacterIcons = { "pickaxe", "tophat", "heart" };

        VisualElement _start;
        Button _startCancel;
        /// <summary>
        /// Сид партии, которую начнёт выбор. При запуске он совпадает с сидом партии за экраном выбора —
        /// сектор тот же, и карте не нужно заново запекать планеты.
        /// </summary>
        ulong _startSeed;

        bool StartOpen => _start != null && !_start.ClassListContains("hidden");

        void CreateStart(VisualElement root)
        {
            _start = root.Q("start");
            _startCancel = root.Q<Button>("startCancel");
            _startCancel.clicked += () => _start.AddToClassList("hidden");
            var list = root.Q("startCharacters");
            var b = new Balance();
            for (int i = 0; i < b.Characters.Length; i++)
                list.Add(MakeCharacterCard(b, i));
        }

        VisualElement MakeCharacterCard(Balance b, int index)
        {
            var c = b.Characters[index];
            var card = new VisualElement();
            card.AddToClassList("character-card");
            card.RegisterCallback<ClickEvent>(_ => StartAs(index));

            var head = new VisualElement();
            head.AddToClassList("character-head");
            head.Add(Icons.Make(CharacterIcons[index % CharacterIcons.Length], "character-icon"));
            head.Add(new Label(c.Name).WithClass("character-name"));
            card.Add(head);

            foreach (var s in Skills.All)
            {
                var row = new VisualElement();
                row.AddToClassList("skill-row");
                row.Add(new Label(s.RuName()).WithClass("skill-name"));
                for (int level = 1; level <= Manager.MaxSkill; level++)
                {
                    var pip = new VisualElement();
                    pip.AddToClassList("pip");
                    pip.EnableInClassList("on", level <= c.Skills[(int)s]);
                    row.Add(pip);
                }
                card.Add(row);
            }

            card.Add(new Label($"Деньги {Fmt.Money(b.StartMoney + c.ExtraMoney)} · долг {Fmt.Money(b.StartDebt)} · репутация {Mathf.RoundToInt((float)c.Reputation)}").WithClass("character-start"));
            card.Add(new Label("+ " + c.Plus).WithClass("character-plus"));
            card.Add(new Label("− " + c.Minus).WithClass("character-minus"));
            var pick = new Button(() => StartAs(index)) { text = "Начать", focusable = false };
            pick.AddToClassList("character-pick");
            card.Add(pick);
            // Весь текст карточки — часть кнопки: клик по любому месту выбирает персонажа.
            card.Query<VisualElement>().ForEach(e => { if (e != card && e != pick) e.pickingMode = PickingMode.Ignore; });
            return card;
        }

        /// <summary>Показать выбор персонажа; canCancel — можно вернуться к идущей партии.</summary>
        void ShowStart(bool canCancel, ulong seed)
        {
            _startSeed = seed;
            _probeMode = false;
            _menu.AddToClassList("hidden");
            _journal.AddToClassList("hidden");
            _startCancel.EnableInClassList("hidden", !canCancel);
            // «Продолжить» — только при запуске игры; из меню идущую партию и так можно продолжить.
            RefreshStartSaves(!canCancel);
            _start.RemoveFromClassList("hidden");
        }

        /// <summary>Начать партию за персонажа номер index (автоснимок зовёт это сам).</summary>
        internal void StartAs(int index) => Later(() =>
        {
            _start.AddToClassList("hidden");
            NewGame(index, _startSeed);
            _playing = true;
            // Вступление ведущего: зачем всё это — кубок «Риелтора года».
            QueuePopup(() => OpenShow(ShowIntro));
        });

        static ulong NewSeed() => (ulong)DateTime.Now.Ticks;
    }
}
