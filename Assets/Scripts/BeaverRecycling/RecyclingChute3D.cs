using UnityEngine;
using UnityEngine.UI;

namespace BeaverRecycling
{
    public sealed class RecyclingChute3D : MonoBehaviour
    {
        [SerializeField] private RecyclingCategory _category;
        [SerializeField] private Transform _intakePoint;
        [SerializeField] private Transform _interactionPoint;
        [SerializeField] private Text _label;
        [SerializeField] private GameObject _cover;
        [SerializeField, Min(0.1f)] private float _reach = 1.8f;
        private bool _available;

        public RecyclingCategory Category => _category;
        public Vector3 IntakePosition => _intakePoint.position;

        /// <summary>현재 일차와 비버 위치를 받아 투입구 활성 여부와 제출 가능 거리를 반환한다.</summary>
        public bool CanUse(int day, Vector3 beaverPosition)
        {
            _available = _category != RecyclingCategory.Glass || day >= 2;
            _available &= _category != RecyclingCategory.Film || day >= 3;
            Vector3 delta = beaverPosition - _interactionPoint.position;
            delta.y = 0f;
            return _available && delta.sqrMagnitude <= _reach * _reach;
        }

        /// <summary>현재 일차·선택·예상 점수를 받아 월드 표지와 잠금 덮개를 갱신한다.</summary>
        public void Refresh(int day, bool selected, bool hasBundle, RecyclingSubmission preview, Camera camera)
        {
            CanUse(day, _interactionPoint.position);
            _cover.SetActive(!_available);
            string suffix = !_available ? "\n" + (_category == RecyclingCategory.Glass ? "2일차 개방" : "3일차 개방") :
                selected && hasBundle ? "\nE 제출  " + (preview.Score >= 0 ? "+" : "") + preview.Score + "점" : "";
            _label.text = RecyclingRules.Name(_category) + suffix;
            _label.color = selected && hasBundle && preview.Wrong > 0
                ? new Color(1f, 0.45f, 0.35f) : _available ? RecyclingRules.Color(_category) : Color.gray;
            _label.canvas.transform.rotation = camera.transform.rotation;
        }
    }
}
