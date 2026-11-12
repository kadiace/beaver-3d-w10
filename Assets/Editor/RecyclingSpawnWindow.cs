using UnityEngine;

using UnityEditor;

namespace BeaverRecycling.Editor
{
    public sealed class RecyclingSpawnWindow : EditorWindow
    {
        private int _selection;
        private Vector3 _position;
        private bool _positionInitialized;

        /// <summary>에디터 메뉴에서 기물 생성 도구 창을 연다.</summary>
        [MenuItem("Tools/Beaver/Spawn Objects")]
        private static void Open()
        {
            GetWindow<RecyclingSpawnWindow>("Spawn Objects");
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                _positionInitialized = false;
                EditorGUILayout.HelpBox("Play Mode에서 사용할 수 있습니다.", MessageType.Info);
                return;
            }

            RecyclingGame3D game = FindFirstObjectByType<RecyclingGame3D>();
            if (game == null)
            {
                EditorGUILayout.HelpBox("분리수거 게임 씬을 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            if (!_positionInitialized)
            {
                _position = game.DebugBeaverPosition;
                _positionInitialized = true;
            }

            string[] names = new string[game.DebugItems.Count + 1];
            for (int i = 0; i < game.DebugItems.Count; i++) names[i] = game.DebugItems[i].Name;
            names[names.Length - 1] = "폭탄";
            _selection = Mathf.Clamp(_selection, 0, names.Length - 1);
            _selection = EditorGUILayout.Popup("오브젝트", _selection, names);
            _position = EditorGUILayout.Vector3Field("생성 위치", _position);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("비버 앞")) _position = game.DebugBeaverPosition;
                if (GUILayout.Button("상류 공급 지점")) _position = game.DebugSupplyPosition;
            }

            if (!game.IsWorking)
                EditorGUILayout.HelpBox("일과 진행 중에 생성할 수 있습니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!game.IsWorking))
            {
                if (GUILayout.Button("생성"))
                {
                    if (_selection == names.Length - 1) game.DebugSpawnBomb(_position);
                    else game.DebugSpawnItem(_selection, _position);
                }
            }
        }
    }
}
