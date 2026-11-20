using System.Collections.Generic;

using UnityEngine;

using RestoreCastle3D;

namespace BeaverRecycling
{
    /// <summary>W11 dash-gauge의 프리팹 분포와 RecyclingRules 점수 공식을 W12에서도 동일하게 계산한다.</summary>
    public static class PlayableRecyclingRules3D
    {
        public const int BASE_SCORE = 3;
        public const int WRONG_PENALTY = 4;
        public const int ESCAPE_PENALTY = 2;

        private static readonly string[,] PREFAB_IDS =
        {
            { "plastic_bottle", "plastic_box" },
            { "glass_bottle", "glass_jar" },
            { "film_bag", "film_wrapper" },
            { "metal_can", "metal_bucket" }
        };

        /// <summary>W11 3일차 이상 분포: 목재 12%, 나머지 4개 재질 균등, 목재는 나뭇가지 3:통나무 1로 선택한다.</summary>
        public static string DrawPrefabId()
        {
            if (Random.value < 0.12f)
                return Random.Range(0, 4) < 3 ? "wood_branch" : "wood_log";
            return PREFAB_IDS[Random.Range(0, 4), Random.Range(0, 2)];
        }

        /// <summary>이식된 재활용 기물 ID인지 반환한다.</summary>
        public static bool IsRecycling(string id) => TryGetCategory(id, out _);

        /// <summary>기물 ID에서 플라스틱0·유리1·비닐2·금속3·목재4의 재질 분류를 찾는다.</summary>
        public static bool TryGetCategory(string id, out int category)
        {
            category = -1;
            if (string.IsNullOrEmpty(id)) return false;
            if (id.StartsWith("plastic_")) category = 0;
            else if (id.StartsWith("glass_")) category = 1;
            else if (id.StartsWith("film_")) category = 2;
            else if (id.StartsWith("metal_")) category = 3;
            else if (id.StartsWith("wood_")) category = 4;
            return category >= 0;
        }

        /// <summary>W11 RecyclingRules.BuildSubmission을 그대로 적용해 정분류·오분류·순수 묶음 보너스를 계산한다.</summary>
        public static int Evaluate(IReadOnlyList<CastlePiece3D> pieces, int chuteCategory,
            out int correct, out int wrong, out int bonus)
        {
            correct = 0;
            wrong = 0;
            bool pureBundle = pieces.Count > 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] == null || !TryGetCategory(pieces[i].MaterialId, out int category))
                {
                    pureBundle = false;
                    continue;
                }
                if (category == 4)
                {
                    pureBundle = false;
                    continue;
                }
                if (category == chuteCategory) correct++;
                else
                {
                    wrong++;
                    pureBundle = false;
                }
            }
            float multiplier = correct >= 6 ? 0.75f : correct >= 4 ? 0.5f :
                correct >= 2 ? 0.25f : 0f;
            bonus = pureBundle ? Mathf.FloorToInt(correct * BASE_SCORE * multiplier) : 0;
            return correct * BASE_SCORE + bonus - wrong * WRONG_PENALTY;
        }

        /// <summary>W11 강 이탈 규칙에 따라 목재는 +3점, 분류 전 쓰레기는 -2점을 반환한다.</summary>
        public static int ExitScore(string id) =>
            TryGetCategory(id, out int category)
                ? category == 4 ? BASE_SCORE : -ESCAPE_PENALTY : 0;
    }
}
