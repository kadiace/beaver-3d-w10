using System.Collections.Generic;

using UnityEngine;

namespace RestoreCastle3D
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CastleFloodPreview3D : MonoBehaviour
    {
        [Header("Flood preview")]
        [SerializeField, Min(0.02f)] private float _refreshInterval = 0.1f;

        [Header("Preview state")]
        private readonly HashSet<CastlePiece3D> _pieces = new HashSet<CastlePiece3D>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<DrawTarget> _drawTargets = new List<DrawTarget>();
        private CastleGame3D _game;
        private CastleFloodEffect3D _floodEffect;
        private float _nextRefreshTime;
        private bool _isVisible;

        public bool IsVisible => _isVisible;
        internal IReadOnlyList<DrawTarget> DrawTargets => _drawTargets;

        /// <summary>
        /// 게임과 기존 홍수 표시기를 받아 현재 카메라의 홍수 미리보기에 연결한다.
        /// 이후 표시와 판정은 연결된 게임의 공통 홍수 영역을 사용한다.
        /// </summary>
        internal void Initialize(CastleGame3D game, CastleFloodEffect3D floodEffect)
        {
            _game = game;
            _floodEffect = floodEffect;
        }

        void LateUpdate()
        {
            if (!_isVisible) return;
            if (!_game.CanPreviewFlood)
            {
                Hide();
                return;
            }

            if (Time.unscaledTime >= _nextRefreshTime)
                Refresh();
        }

        void OnDisable()
        {
            Hide();
        }

        /// <summary>
        /// 현재 표시 상태를 반전하고 활성화할 때 즉시 홍수와 파괴 예상 대상을 갱신한다.
        /// 비활성화할 때에는 물과 관통 강조 목록을 함께 숨긴다.
        /// </summary>
        public void Toggle()
        {
            if (_isVisible)
            {
                Hide();
                return;
            }

            _isVisible = true;
            Refresh();
        }

        /// <summary>
        /// 다음 갱신 시각을 초기화하여 배치, 제거 또는 부착 이후 최신 상태를 표시한다.
        /// 비활성 상태에서는 표시를 켜지 않는다.
        /// </summary>
        public void RequestRefresh()
        {
            _nextRefreshTime = 0f;
        }

        /// <summary>
        /// 미리보기의 물과 파괴 예상 목록을 숨기고 표시 상태를 해제한다.
        /// 실제 홍수 시작과 재시작 시 같은 표시기를 안전하게 넘겨준다.
        /// </summary>
        public void Hide()
        {
            if (!_isVisible) return;
            _isVisible = false;
            _pieces.Clear();
            _drawTargets.Clear();
            _renderers.Clear();
            _floodEffect.Hide();
        }

        /// <summary>
        /// 현재 홍수 영역 전체를 표시하고 공통 판정으로 파괴될 자재의 렌더러 목록을 만든다.
        /// 다음 갱신 시각은 Inspector의 갱신 간격을 사용하여 저장한다.
        /// </summary>
        private void Refresh()
        {
            CastleFloodVolume3D volume = _game.CaptureFloodVolume();
            _floodEffect.Begin(volume);
            _floodEffect.SetFill(1f);
            _game.CollectFloodPreviewTargets(volume, _pieces);
            _drawTargets.Clear();

            foreach (CastlePiece3D piece in _pieces)
            {
                _renderers.Clear();
                piece.GetComponentsInChildren(false, _renderers);
                foreach (Renderer renderer in _renderers)
                {
                    Mesh mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned)
                        mesh = skinned.sharedMesh;
                    else if (renderer.TryGetComponent(out MeshFilter filter))
                        mesh = filter.sharedMesh;

                    if (mesh != null)
                        _drawTargets.Add(new DrawTarget(renderer, mesh.subMeshCount));
                }
            }
            _renderers.Clear();
            _nextRefreshTime = Time.unscaledTime + _refreshInterval;
        }

        internal readonly struct DrawTarget
        {
            public Renderer Renderer { get; }
            public int SubMeshCount { get; }

            /// <summary>
            /// 자재 렌더러와 서브메시 수를 받아 관통 마스크의 그리기 대상을 저장한다.
            /// 실제 위치와 회전은 그릴 때 렌더러의 현재 변환을 사용한다.
            /// </summary>
            public DrawTarget(Renderer renderer, int subMeshCount)
            {
                Renderer = renderer;
                SubMeshCount = subMeshCount;
            }
        }
    }
}
