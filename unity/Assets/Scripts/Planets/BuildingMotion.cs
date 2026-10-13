using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Движение подвижной части постройки: качалка качается, радар крутится, кран поворачивает стрелу.
    /// Часть — дочерний объект модели с началом координат в точке вращения (так её выгружает
    /// tools/buildings/make_buildings.py), какое у неё движение — по имени части, таблица ниже.
    /// Оси — в пространстве части: Y — вверх (Z в Blender), Z — ось Y в Blender.
    /// </summary>
    public sealed class BuildingMotion : MonoBehaviour
    {
        enum Kind { Spin, Rock, Bob }

        sealed class Spec
        {
            public Kind Kind;
            public Vector3 Axis;
            /// <summary>Градусы (качание) или единицы модели (вверх-вниз).</summary>
            public float Amplitude;
            /// <summary>Секунд на полный цикл.</summary>
            public float Period;
            public float Phase;
        }

        /// <summary>Период качалки: балансир и кривошип движутся в такт.</summary>
        const float PumpPeriod = 3.6f;

        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["pumpjack_beam"] = new Spec { Kind = Kind.Rock, Axis = Vector3.forward, Amplitude = 13, Period = PumpPeriod },
            ["pumpjack_crank"] = new Spec { Kind = Kind.Spin, Axis = Vector3.forward, Period = PumpPeriod, Phase = 0.25f },
            ["radar"] = new Spec { Kind = Kind.Spin, Axis = Vector3.up, Period = 4 },
            ["searchlight"] = new Spec { Kind = Kind.Rock, Axis = Vector3.up, Amplitude = 50, Period = 9 },
            ["crane_top"] = new Spec { Kind = Kind.Rock, Axis = Vector3.up, Amplitude = 55, Period = 22 },
            ["travel_block"] = new Spec { Kind = Kind.Bob, Axis = Vector3.up, Amplitude = 0.12f, Period = 5 },
            ["dish"] = new Spec { Kind = Kind.Rock, Axis = Vector3.up, Amplitude = 35, Period = 14 },
        };

        Spec _spec;
        Vector3 _restPosition;
        Quaternion _restRotation;
        float _offset;

        /// <summary>
        /// Оживить подвижные части модели. offset — сдвиг по времени, чтобы одинаковые постройки
        /// не двигались синхронно; одна постройка — одно значение, чтобы её части шли в такт.
        /// </summary>
        public static void Attach(GameObject model, float offset)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                var key = PartName(t.name);
                if (key == null || !Specs.TryGetValue(key, out var spec)) continue;
                var m = t.gameObject.AddComponent<BuildingMotion>();
                m._spec = spec;
                m._restPosition = t.localPosition;
                m._restRotation = t.localRotation;
                m._offset = offset;
            }
        }

        /// <summary>«extractor__pumpjack_beam.001» → «pumpjack_beam».</summary>
        static string PartName(string name)
        {
            int i = name.IndexOf("__", System.StringComparison.Ordinal);
            if (i < 0) return null;
            var part = name.Substring(i + 2);
            int dot = part.IndexOf('.');
            return dot >= 0 ? part.Substring(0, dot) : part;
        }

        void Update()
        {
            float cycle = (Time.time + _offset * _spec.Period) / _spec.Period + _spec.Phase;
            switch (_spec.Kind)
            {
                case Kind.Spin:
                    transform.localRotation = _restRotation * Quaternion.AngleAxis(cycle * 360, _spec.Axis);
                    break;
                case Kind.Rock:
                    transform.localRotation = _restRotation * Quaternion.AngleAxis(Mathf.Sin(cycle * 2 * Mathf.PI) * _spec.Amplitude, _spec.Axis);
                    break;
                case Kind.Bob:
                    transform.localPosition = _restPosition + _spec.Axis * (Mathf.Sin(cycle * 2 * Mathf.PI) * _spec.Amplitude);
                    break;
            }
        }
    }
}
