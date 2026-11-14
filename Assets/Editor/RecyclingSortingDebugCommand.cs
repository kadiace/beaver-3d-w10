#if UNITY_EDITOR
using BeaverRecycling;
using UnityEditor;
using UnityEngine;

namespace BeaverRecycling.Editor
{
    /// <summary>Play Mode에서만 실행할 수 있는 가상 분류 미니게임 디버그 명령.</summary>
    internal static class RecyclingSortingDebugCommand
    {
        private const string MenuPath = "Tools/Beaver Recycling/Random Sorting Mini-game %#m";

        [MenuItem(MenuPath, false, 50)]
        private static void Run()
        {
            RecyclingGame3D game = Object.FindFirstObjectByType<RecyclingGame3D>();
            if (game != null) game.DebugStartRandomSorting();
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            return EditorApplication.isPlaying && Object.FindFirstObjectByType<RecyclingGame3D>() != null;
        }
    }
}
#endif
