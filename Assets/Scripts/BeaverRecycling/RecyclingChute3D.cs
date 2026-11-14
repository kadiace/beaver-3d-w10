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
        private MeshRenderer[] _outlineRenderers;
        private LineRenderer _dockBorder;
        private Material _dockBorderMaterial;
        private bool _available;

        public RecyclingCategory Category => _category;
        public Vector3 IntakePosition => _intakePoint.position;
        public Bounds DockBounds
        {
            get
            {
                if (_dock == null) _dock = transform.Find("Dock").GetComponent<Renderer>();
                return _dock.bounds;
            }
        }

        void OnDestroy()
        {
            Destroy(_dockBorderMaterial);
        }

        /// <summary>현재 일차와 비버 위치를 받아 투입구 활성 여부와 제출 가능 거리를 반환한다.</summary>
        public bool CanUse(int day, Vector3 beaverPosition)
        {
            _available = IsAvailable(day);
            Vector3 delta = beaverPosition - _interactionPoint.position;
            delta.y = 0f;
            return _available && delta.sqrMagnitude <= _reach * _reach;
        }

        /// <summary>카메라 투영 판정에 사용할 Sorting box의 월드 바운드를 반환한다.</summary>
        public Bounds SortingBoxBounds
        {
            get
            {
                if (_sortingBox == null) _sortingBox = transform.Find("Sorting box").GetComponent<Renderer>();
                return _sortingBox.bounds;
            }
        }

        /// <summary>현재 날짜에 이 재질의 투입구가 열려 있는지 반환한다.</summary>
        public bool IsAvailable(int day) =>
            (_category != RecyclingCategory.Glass || day >= 2) &&
            (_category != RecyclingCategory.Film || day >= 3);

        /// <summary>반납 가능 여부에 따라 chute의 메시 렌더러에 외곽선 레이어를 적용하거나 해제한다.</summary>
        private void SetOutline(bool enabled)
        {
            if (_outlineRenderers == null) _outlineRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in _outlineRenderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~30u) | (enabled ? 4u : 0u);
            if (enabled && _dockBorder == null) CreateDockBorder();
            if (_dockBorder != null) _dockBorder.enabled = enabled;
        }

        /// <summary>Dock 상단의 네 변을 따라 기존 접촉 외곽선 색의 선을 만들어 측면 외곽선도 보이게 한다.</summary>
        private void CreateDockBorder()
        {
            if (_dock == null) _dock = transform.Find("Dock").GetComponent<Renderer>();
            Bounds bounds = _dock.bounds;
            GameObject border = new GameObject("Dock Outline");
            border.layer = gameObject.layer;
            border.transform.SetParent(transform, false);
            _dockBorder = border.AddComponent<LineRenderer>();
            _dockBorderMaterial = new Material(Shader.Find("RestoreCastle3D/Outline"));
            _dockBorderMaterial.SetFloat("_Outline_Thickness", 0f);
            _dockBorderMaterial.SetColor("_Outline_Color", new Color(1f, 0.58f, 0.18f));
            _dockBorder.sharedMaterial = _dockBorderMaterial;
            _dockBorder.useWorldSpace = false;
            _dockBorder.alignment = LineAlignment.View;
            _dockBorder.widthMultiplier = 0.12f;
            _dockBorder.numCornerVertices = 3;
            _dockBorder.loop = true;
            _dockBorder.positionCount = 4;
            float top = bounds.max.y + 0.03f;
            _dockBorder.SetPosition(0, transform.InverseTransformPoint(new Vector3(bounds.min.x, top, bounds.min.z)));
            _dockBorder.SetPosition(1, transform.InverseTransformPoint(new Vector3(bounds.max.x, top, bounds.min.z)));
            _dockBorder.SetPosition(2, transform.InverseTransformPoint(new Vector3(bounds.max.x, top, bounds.max.z)));
            _dockBorder.SetPosition(3, transform.InverseTransformPoint(new Vector3(bounds.min.x, top, bounds.max.z)));
        }

        /// <summary>현재 일차와 제출 상태를 받아 투입구 활성 여부와 표지를 갱신한다.</summary>
        public void Refresh(int day, bool selected, bool hasBundle, RecyclingSubmission preview, Camera camera)
        {
            bool available = CanUse(day, _interactionPoint.position);
            SetOutline(available && selected && hasBundle);
            if (gameObject.activeSelf != available) gameObject.SetActive(available);
            if (!available) return;

            _cover.SetActive(false);
            string suffix = selected && hasBundle
                ? "\n" + (Managers.Input.GamePadConnected ? "South" : "Z") + " 투입  " +
                  (preview.Score >= 0 ? "+" : "") + preview.Score + "점"
                : "";
            _label.text = RecyclingRules.Name(_category) + suffix;
            _label.color = selected && hasBundle && preview.Wrong > 0
                ? new Color(1f, 0.45f, 0.35f) : RecyclingRules.Color(_category);
            _label.canvas.transform.rotation = camera.transform.rotation;
        }
    }
}
