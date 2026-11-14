using System.Collections.Generic;

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
        private Renderer _dock;
        private Renderer _sortingBox;
        private bool _available;

        public RecyclingCategory Category => _category;
        public Vector3 IntakePosition => _intakePoint.position;

        /// <summary>현재 일차와 비버 위치를 받아 투입구 활성 여부와 제출 가능 거리를 반환한다.</summary>
        public bool CanUse(int day, Vector3 beaverPosition)
        {
            _available = IsAvailable(day);
            Vector3 delta = beaverPosition - _interactionPoint.position;
            delta.y = 0f;
            return _available && delta.sqrMagnitude <= _reach * _reach;
        }

        /// <summary>묶음의 바닥면이 Dock과 Sorting box 상단에 절반 넘게 걸쳐 있고 해당 투입구가 열렸는지 반환한다.</summary>
        public bool CoversMajority(int day, IReadOnlyList<RecyclingItem> items)
        {
            if (!IsAvailable(day) || items.Count == 0) return false;
            if (_dock == null) _dock = transform.Find("Dock").GetComponent<Renderer>();
            if (_sortingBox == null) _sortingBox = transform.Find("Sorting box").GetComponent<Renderer>();
            Bounds dockBounds = _dock.bounds;
            Bounds boxBounds = _sortingBox.bounds;
            Rect dock = Rect.MinMaxRect(dockBounds.min.x, dockBounds.min.z, dockBounds.max.x, dockBounds.max.z);
            Rect box = Rect.MinMaxRect(boxBounds.min.x, boxBounds.min.z, boxBounds.max.x, boxBounds.max.z);
            float totalArea = 0f;
            float coveredArea = 0f;
            foreach (RecyclingItem item in items)
            {
                Bounds bounds = item.Collider.bounds;
                Rect footprint = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                totalArea += footprint.width * footprint.height;
                coveredArea += OverlapArea(footprint, dock) + OverlapArea(footprint, box) -
                    OverlapArea(footprint, dock, box);
            }
            return totalArea > 0f && coveredArea > totalArea * 0.5f;
        }

        /// <summary>현재 날짜에 이 재질의 투입구가 열려 있는지 반환한다.</summary>
        private bool IsAvailable(int day) =>
            (_category != RecyclingCategory.Glass || day >= 2) &&
            (_category != RecyclingCategory.Film || day >= 3);

        /// <summary>두 수평 사각형의 겹치는 면적을 반환한다.</summary>
        private static float OverlapArea(Rect first, Rect second) =>
            Mathf.Max(0f, Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin)) *
            Mathf.Max(0f, Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin));

        /// <summary>물건과 두 상단 영역에 공통으로 겹치는 면적을 반환해 중복 계산을 막는다.</summary>
        private static float OverlapArea(Rect item, Rect dock, Rect box) =>
            Mathf.Max(0f, Mathf.Min(Mathf.Min(item.xMax, dock.xMax), box.xMax) -
                Mathf.Max(Mathf.Max(item.xMin, dock.xMin), box.xMin)) *
            Mathf.Max(0f, Mathf.Min(Mathf.Min(item.yMax, dock.yMax), box.yMax) -
                Mathf.Max(Mathf.Max(item.yMin, dock.yMin), box.yMin));

        /// <summary>현재 일차와 제출 상태를 받아 투입구 활성 여부와 표지를 갱신한다.</summary>
        public void Refresh(int day, bool selected, bool hasBundle, RecyclingSubmission preview, Camera camera)
        {
            bool available = CanUse(day, _interactionPoint.position);
            if (gameObject.activeSelf != available) gameObject.SetActive(available);
            if (!available) return;

            _cover.SetActive(false);
            string suffix = selected && hasBundle
                ? "\nE 투입  " + (preview.Score >= 0 ? "+" : "") + preview.Score + "점"
                : "";
            _label.text = RecyclingRules.Name(_category) + suffix;
            _label.color = selected && hasBundle && preview.Wrong > 0
                ? new Color(1f, 0.45f, 0.35f) : RecyclingRules.Color(_category);
            _label.canvas.transform.rotation = camera.transform.rotation;
        }
    }
}
