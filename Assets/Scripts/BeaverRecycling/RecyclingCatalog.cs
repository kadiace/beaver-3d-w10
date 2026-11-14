using System;
using System.Collections.Generic;

using UnityEngine;

namespace BeaverRecycling
{
    public enum RecyclingCategory { Plastic, Glass, Film, Metal, Wood }

    [Serializable]
    public sealed class RecyclingDefinition
    {
        [SerializeField] private string _id;
        [SerializeField] private string _name;
        [SerializeField] private RecyclingCategory _category;
        [SerializeField] private RecyclingItem _prefab;
        [SerializeField, Min(1)] private int _firstDay = 1;
        [SerializeField, Min(0)] private int _woodYield;
        [SerializeField, Min(1)] private int _weight = 1;

        public string Id => _id;
        public string Name => _name;
        public RecyclingCategory Category => _category;
        public RecyclingItem Prefab => _prefab;
        public int FirstDay => _firstDay;
        public int WoodYield => _woodYield;
        public int Weight => _weight;
        public bool IsWood => _category == RecyclingCategory.Wood;
    }

    [CreateAssetMenu(menuName = "Beaver/Recycling Catalog")]
    public sealed class RecyclingCatalog : ScriptableObject
    {
        [SerializeField] private RecyclingDefinition[] _items;

#if UNITY_EDITOR
        public IReadOnlyList<RecyclingDefinition> DebugItems => _items;
#endif

        /// <summary>일차를 받아 Unity 난수로 목재 12%와 활성 분류의 균등 확률에 따라 기물 정의를 반환한다.</summary>
        public RecyclingDefinition Draw(int day)
        {
            RecyclingCategory category;
            if (UnityEngine.Random.value < 0.12f) category = RecyclingCategory.Wood;
            else
            {
                RecyclingCategory[] active = day == 1
                    ? new[] { RecyclingCategory.Plastic, RecyclingCategory.Metal }
                    : day == 2
                        ? new[] { RecyclingCategory.Plastic, RecyclingCategory.Metal, RecyclingCategory.Glass }
                        : new[] { RecyclingCategory.Plastic, RecyclingCategory.Glass, RecyclingCategory.Film, RecyclingCategory.Metal };
                category = active[UnityEngine.Random.Range(0, active.Length)];
            }
            int total = 0;
            foreach (RecyclingDefinition item in _items)
                if (item.Category == category && item.FirstDay <= day) total += item.Weight;
            int roll = UnityEngine.Random.Range(0, total);
            foreach (RecyclingDefinition item in _items)
            {
                if (item.Category != category || item.FirstDay > day) continue;
                roll -= item.Weight;
                if (roll < 0) return item;
            }
            throw new InvalidOperationException("Recycling catalog has no active item for " + category);
        }
    }

    public readonly struct RecyclingSubmission
    {
        public int Correct { get; }
        public int Wrong { get; }
        public int Bonus { get; }
        public int Score { get; }

        /// <summary>정상 개수·오분류 개수·보너스·점수를 받아 제출 결과를 보관한다.</summary>
        public RecyclingSubmission(int correct, int wrong, int bonus, int score)
        {
            Correct = correct;
            Wrong = wrong;
            Bonus = bonus;
            Score = score;
        }
    }

    public static class RecyclingRules
    {
        public const int BASE_SCORE = 3;
        public const int WRONG_PENALTY = 4;
        public const int ESCAPE_PENALTY = 2;

        /// <summary>제출 묶음과 투입구 종류를 받아 정상 점수·보너스·쓰레기 감점을 계산해 반환한다.</summary>
        public static RecyclingSubmission Evaluate(IReadOnlyList<RecyclingItem> items, RecyclingCategory chute)
        {
            int correct = 0;
            int wrong = 0;
            bool pureBundle = items.Count > 0;
            for (int i = 0; i < items.Count; i++)
            {
                RecyclingDefinition definition = items[i].Definition;
                if (definition.IsWood)
                {
                    pureBundle = false;
                    continue;
                }
                if (definition.Category == chute) correct++;
                else
                {
                    wrong++;
                    pureBundle = false;
                }
            }
            return BuildSubmission(correct, wrong, pureBundle);
        }

        /// <summary>미니게임에 남은 아이템 정의와 투입구 종류를 받아 제외된 아이템 없이 분류 결과를 계산한다.</summary>
        public static RecyclingSubmission EvaluateDefinitions(IReadOnlyList<RecyclingDefinition> definitions, RecyclingCategory chute)
        {
            int correct = 0;
            int wrong = 0;
            bool pureBundle = definitions.Count > 0;
            foreach (RecyclingDefinition definition in definitions)
            {
                if (definition.IsWood)
                {
                    pureBundle = false;
                    continue;
                }
                if (definition.Category == chute) correct++;
                else
                {
                    wrong++;
                    pureBundle = false;
                }
            }
            return BuildSubmission(correct, wrong, pureBundle);
        }

        /// <summary>정상·오분류 개수와 순수 묶음 여부를 받아 공통 점수 및 묶음 보너스를 반환한다.</summary>
        private static RecyclingSubmission BuildSubmission(int correct, int wrong, bool pureBundle)
        {
            float multiplier = correct >= 6 ? 0.75f : correct >= 4 ? 0.5f : correct >= 2 ? 0.25f : 0f;
            int bonus = pureBundle ? Mathf.FloorToInt(correct * BASE_SCORE * multiplier) : 0;
            return new RecyclingSubmission(correct, wrong, bonus,
                correct * BASE_SCORE + bonus - wrong * WRONG_PENALTY);
        }

        /// <summary>분류 값을 받아 UI와 월드 표지에 사용할 짧은 한글 이름을 반환한다.</summary>
        public static string Name(RecyclingCategory category)
        {
            switch (category)
            {
                case RecyclingCategory.Plastic: return "플라스틱";
                case RecyclingCategory.Glass: return "유리";
                case RecyclingCategory.Film: return "비닐";
                case RecyclingCategory.Metal: return "금속";
                default: return "목재";
            }
        }

        /// <summary>분류 값을 받아 기물과 투입구의 구분 색상을 반환한다.</summary>
        public static Color Color(RecyclingCategory category)
        {
            switch (category)
            {
                case RecyclingCategory.Plastic: return new Color(0.28f, 0.68f, 1f);
                case RecyclingCategory.Glass: return new Color(0.35f, 0.88f, 0.62f);
                case RecyclingCategory.Film: return new Color(0.8f, 0.55f, 1f);
                case RecyclingCategory.Metal: return new Color(1f, 0.76f, 0.3f);
                default: return new Color(0.73f, 0.49f, 0.27f);
            }
        }

        /// <summary>일차를 받아 그날 등장하는 분류 이름을 반환한다.</summary>
        public static string DayCategories(int day) => day == 1 ? "플라스틱 · 금속" :
            day == 2 ? "플라스틱 · 금속 · 유리" : "플라스틱 · 금속 · 유리 · 비닐";
    }
}
