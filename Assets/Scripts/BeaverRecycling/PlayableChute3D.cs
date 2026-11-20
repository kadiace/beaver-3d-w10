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

        /// <summary>투입구에 내려놓은 하천 기물을 기존 점수 정산 경로에 전달한다.</summary>
        public void Submit(CastlePiece3D piece)
        {
            if (piece == null) return;
            CastleCreekScore3D.Active.Settle(piece);
        }

        private void LateUpdate()
        {
            if (_label != null) _label.canvas.transform.rotation = Camera.main.transform.rotation;
        }
    }
}
