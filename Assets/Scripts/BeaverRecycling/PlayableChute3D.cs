using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using RestoreCastle3D;

namespace BeaverRecycling
{
    /// <summary>원본 분리수거 chute의 외형과 투입 위치를 W12 하천 플레이에 연결한다.</summary>
    public sealed class PlayableChute3D : MonoBehaviour
    {
        [Header("Chute")]
        [SerializeField] private int _category;
        [SerializeField] private Transform _intakePoint;
        [SerializeField] private Transform _interactionPoint;
        [SerializeField] private Text _label;
        [SerializeField] private GameObject _cover;
        [SerializeField, Min(0.1f)] private float _reach = 6f;
        private MeshRenderer[] _outlineRenderers;

        public int Category => _category;
        public Vector3 IntakePosition => _intakePoint.position;
        public Vector3 InteractionPosition => _interactionPoint.position;

        /// <summary>비버의 월드 위치와 투입 위치 사이의 수평 거리가 허용 범위인지 확인한다.</summary>
        public bool CanUse(Vector3 beaverPosition)
        {
            Vector3 delta = beaverPosition - _interactionPoint.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= _reach * _reach;
        }

        /// <summary>같은 재질을 운반 중인 비버에게 이 투입구를 노란 아웃라인으로 안내한다.</summary>
        public void SetHighlighted(bool enabled)
        {
            if (_outlineRenderers == null)
                _outlineRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in _outlineRenderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~30u) |
                    (enabled ? 4u : 0u);
        }

        /// <summary>최대 6개의 운반 묶음을 W11 재활용 점수 공식으로 한 번에 정산한다.</summary>
        public int Submit(IReadOnlyList<CastlePiece3D> pieces)
        {
            return CastleCreekScore3D.Active.SubmitRecycling(pieces, _category,
                _intakePoint.position);
        }

        private void LateUpdate()
        {
            if (_label != null) _label.canvas.transform.rotation = Camera.main.transform.rotation;
        }
    }
}
