using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RestoreCastle3D
{
    /// <summary>Feedback and settlement for the endless creek mode, independent of the legacy stage HUD.</summary>
    public sealed class CastleCreekScore3D : MonoBehaviour
    {
        public static CastleCreekScore3D Active { get; private set; }

        private readonly HashSet<CastlePiece3D> _registered = new HashSet<CastlePiece3D>();
        private readonly Dictionary<CastlePiece3D, int> _lastPoints = new Dictionary<CastlePiece3D, int>();
        private readonly HashSet<CastlePiece3D> _outlined = new HashSet<CastlePiece3D>();
        private CastleCatalog3D _catalog;
        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _rect;
        private CastleFloatingText3D _floating;
        private TextMeshProUGUI _scoreText;
        private TextMeshProUGUI _hoverText;
        private TextMeshProUGUI _edgeText;
        private TMP_FontAsset _font;
        private float _displayedScore;
        private int _score;
        private float _flashUntil;
        private float _edgeUntil;
        private float _edgeLastTime;
        private int _edgeCombined;
        private int _edgeEvents;
        private Vector3 _edgePosition;
        private bool _batching;
        private CastlePiece3D _hovered;

        public int TotalScore => _score;

        public void Initialize(Camera camera, CastleCatalog3D catalog)
        {
            Active = this;
            _camera = camera;
            _catalog = catalog;
            var canvasObject = new GameObject("Creek Score Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 15;
            _rect = (RectTransform)canvasObject.transform;
            Font sourceFont = Resources.Load<Font>("Fonts & Materials/NotoSansKR");
            _font = sourceFont != null ? TMP_FontAsset.CreateFontAsset(sourceFont) : TMP_Settings.defaultFontAsset;
            if (_font != null) _font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            _scoreText = CreateLabel("Total Score", 36, TextAlignmentOptions.TopRight);
            _scoreText.rectTransform.anchorMin = new Vector2(1, 1);
            _scoreText.rectTransform.anchorMax = new Vector2(1, 1);
            _scoreText.rectTransform.pivot = new Vector2(1, 1);
            _scoreText.rectTransform.anchoredPosition = new Vector2(-26, -18);
            _scoreText.rectTransform.sizeDelta = new Vector2(400, 76);
            _scoreText.text = "점수 0";

            _hoverText = CreateLabel("Synergy Details", 24, TextAlignmentOptions.TopLeft);
            _hoverText.rectTransform.pivot = new Vector2(0, 1);
            _hoverText.rectTransform.sizeDelta = new Vector2(380, 250);
            _hoverText.gameObject.SetActive(false);
            _edgeText = CreateLabel("Offscreen Score Indicator", 34, TextAlignmentOptions.Center);
            _edgeText.rectTransform.sizeDelta = new Vector2(240, 80);
            _edgeText.gameObject.SetActive(false);
            _floating = canvasObject.AddComponent<CastleFloatingText3D>();
            _floating.Initialize(_camera, _rect);
        }

        private TextMeshProUGUI CreateLabel(string name, int size, TextAlignmentOptions align)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            obj.transform.SetParent(_rect, false);
            var label = obj.GetComponent<TextMeshProUGUI>();
            label.font = _font;
            label.fontSize = size;
            label.alignment = align;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.outlineWidth = 0.2f;
            label.outlineColor = Color.black;
            return label;
        }

        public void Register(CastlePiece3D piece)
        {
            if (piece == null) return;
            _registered.Add(piece);
            _lastPoints[piece] = 0;
        }

        public void Unregister(CastlePiece3D piece)
        {
            _registered.Remove(piece);
            _lastPoints.Remove(piece);
            _outlined.Remove(piece);
            if (_hovered == piece) _hovered = null;
        }

        /// <summary>Recalculate only components touched by an added or removed edge.</summary>
        public void Refresh(CastlePiece3D a, CastlePiece3D b = null)
        {
            if (_catalog == null || _batching) return;
            var visited = new HashSet<CastlePiece3D>();
            foreach (var start in new[] { a, b })
            {
                if (start == null || !start.isActiveAndEnabled || visited.Contains(start)) continue;
                List<CastlePiece3D> group = CastleSynergy3D.Collect(start);
                foreach (var p in group) visited.Add(p);
                var calculated = CastleSynergy3D.Evaluate(group, _catalog);
                foreach (CastlePiece3D p in group)
                {
                    if (!_registered.Contains(p)) continue;
                    int next = calculated.Points.TryGetValue(p, out int score) ? score : 0;
                    _lastPoints.TryGetValue(p, out int old);
                    _lastPoints[p] = next;
                    int delta = next - old;
                    if (delta == 0) continue;
                    _floating?.Show(new CastleChange3D(p, null, CastleChangeKind.Durability,
                        delta, p.transform.position, (delta > 0 ? "+" : "") + delta));
                }
            }
        }

        public void Settle(CastlePiece3D piece)
        {
            if (piece == null || piece.IsSettling) return;
            var group = CastleSynergy3D.Collect(piece);
            if (group.Count == 0) return;
            // Freeze every member before invoking resource pooling callbacks.
            foreach (var member in group) member.MarkSettling();
            int earned = CastleSynergy3D.Evaluate(group, _catalog).Total;
            Vector3 center = Vector3.zero;
            foreach (var member in group) center += member.transform.position;
            center /= group.Count;
            _score += earned;
            _flashUntil = Time.unscaledTime + 0.65f;
            if (group.Count > 1 && earned != 0)
            {
                Vector3 screen = _camera.WorldToViewportPoint(center);
                bool visible = screen.z > 0 && screen.x >= 0.05f && screen.x <= 0.95f &&
                    screen.y >= 0.05f && screen.y <= 0.95f;
                string label = (earned > 0 ? "+" : "") + earned + "점";
                if (visible)
                    _floating.Show(new CastleChange3D(null, null, CastleChangeKind.ScoreAward,
                        earned, center, label + (earned > 0 ? " 획득!" : " 감점!")));
                else ShowEdgeIndicator(center, label);
            }
            foreach (var member in group)
            {
                Unregister(member);
                Managers.Resource.Destroy(member.gameObject);
            }
        }

        public void Detonate(CastlePiece3D barrel)
        {
            if (barrel == null || barrel.IsSettling) return;
            var group = CastleSynergy3D.Collect(barrel);
            if (group.Count < 2) return;
            Vector3 center = Vector3.zero;
            foreach (var p in group) center += p.transform.position;
            center /= group.Count;
            // Batch joint removals to avoid duplicate intermediate score popups.
            _batching = true;
            foreach (var p in group) p.BreakAllAttachments();
            _batching = false;
            foreach (var p in group)
            {
                if (p == null) continue;
                p.BlockReattachment(0.5f);
                if (p == barrel) continue;
                Vector3 direction = p.transform.position - center;
                if (direction.sqrMagnitude < 0.01f)
                    direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + 0.25f;
                p.Body.AddForce(direction.normalized * 18f, ForceMode.Impulse);
            }
            // All members are now isolated; clear cached scores and show each loss once.
            foreach (var p in group) Refresh(p);
            barrel.MarkSettling();
            Unregister(barrel);
            Managers.Resource.Destroy(barrel.gameObject);
        }

        private void ShowEdgeIndicator(Vector3 position, string text)
        {
            // Combine bursty settlements rather than overwrite an unread indicator.
            if (Time.unscaledTime - _edgeLastTime > 0.35f)
            {
                _edgeCombined = 0;
                _edgeEvents = 0;
            }
            int.TryParse(text.Replace("점", ""), out int delta);
            _edgeCombined += delta;
            _edgeEvents++;
            _edgeLastTime = Time.unscaledTime;
            _edgePosition = position;
            _edgeUntil = Time.unscaledTime + 1.5f;
            _edgeText.gameObject.SetActive(true);
            _edgeText.text = (_edgeCombined >= 0 ? "+" : "") + _edgeCombined + "점" +
                (_edgeEvents > 1 ? " (" + _edgeEvents + "회)" : "");
            _edgeText.color = _edgeCombined < 0 ? new Color(0.55f, 0.8f, 1f) :
                new Color(1f, 0.87f, 0.35f);
        }

        private void Update()
        {
            if (_camera == null || _catalog == null) return;
            _displayedScore = Mathf.MoveTowards(_displayedScore, _score,
                Mathf.Max(40, Mathf.Abs(_score - _displayedScore) * 8) * Time.unscaledDeltaTime);
            _scoreText.text = "점수 " + Mathf.RoundToInt(_displayedScore);
            _scoreText.color = Time.unscaledTime < _flashUntil ?
                new Color(1f, 0.87f, 0.35f) : Color.white;
            _scoreText.rectTransform.localScale = Vector3.one *
                (Time.unscaledTime < _flashUntil ? 1.12f : 1f);
            if (Time.unscaledTime < _edgeUntil)
            {
                Vector3 screen = _camera.WorldToScreenPoint(_edgePosition);
                Vector2 delta = new Vector2(screen.x - Screen.width * 0.5f,
                    screen.y - Screen.height * 0.5f);
                if (screen.z < 0) delta = -delta;
                if (delta.sqrMagnitude < 1f) delta = Vector2.up;
                delta.Normalize();
                Vector2 target = new Vector2(Screen.width / 2f, Screen.height / 2f) +
                    new Vector2(delta.x * (Screen.width / 2f - 130f),
                        delta.y * (Screen.height / 2f - 75f));
                target.x = Mathf.Clamp(target.x, 130f, Screen.width - 130f);
                target.y = Mathf.Clamp(target.y, 75f, Screen.height - 75f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect,
                    target, null, out Vector2 local);
                _edgeText.rectTransform.anchoredPosition = local;
                string arrow = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? (delta.x > 0 ? "→" : "←") : (delta.y > 0 ? "↑" : "↓");
                _edgeText.text = (_edgeCombined >= 0 ? "+" : "") + _edgeCombined +
                    "점 " + arrow + (_edgeEvents > 1 ? " (" + _edgeEvents + "회)" : "");
                Color color = _edgeText.color;
                color.a = Mathf.Clamp01((_edgeUntil - Time.unscaledTime) * 2f);
                _edgeText.color = color;
            }
            else if (_edgeText.gameObject.activeSelf) _edgeText.gameObject.SetActive(false);

            UpdateHover();
        }

        private void UpdateHover()
        {
            CastlePiece3D next = null;
            Mouse mouse = Mouse.current;
            Vector2 cursor = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            if (mouse != null && !mouse.rightButton.isPressed &&
                Physics.Raycast(_camera.ScreenPointToRay(cursor), out RaycastHit hit,
                    300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var candidate = hit.collider.GetComponentInParent<CastlePiece3D>();
                if (candidate != null && _registered.Contains(candidate)) next = candidate;
            }
            if (next == _hovered && next == null) return;
            _hovered = next;
            foreach (CastlePiece3D p in _outlined)
                if (p != null && p.isActiveAndEnabled) p.SetOutlineLayer(0);
            _outlined.Clear();
            if (next == null)
            {
                _hoverText.gameObject.SetActive(false);
                return;
            }
            var group = CastleSynergy3D.Collect(next);
            _hoverText.gameObject.SetActive(group.Count > 1);
            var result = CastleSynergy3D.Evaluate(group, _catalog);
            foreach (var p in result.Contributors)
            {
                if (p == null || p == next) continue;
                p.SetOutlineLayer(_catalog.Get(p.MaterialId).category == "쓰레기" ? 8u : 4u);
                _outlined.Add(p);
            }
            next.SetOutlineLayer(2u);
            _outlined.Add(next);
            _hoverText.text = _catalog.Get(next.MaterialId).name + " · 예상 " + result.Total + "점" +
                "\n" + string.Join("\n", group.Take(6).Select(p =>
                    _catalog.Get(p.MaterialId).name + " " +
                    (result.Points.TryGetValue(p, out int n) && n >= 0 ? "+" : "") +
                    (result.Points.TryGetValue(p, out int value) ? value : 0))) +
                "\\n" + string.Join("\\n", _catalog.Get(next.MaterialId).abilities ??
                    new string[0]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect,
                new Vector2(Mathf.Clamp(cursor.x + 20, 0, Screen.width - 400),
                    Mathf.Clamp(cursor.y - 16, 260, Screen.height)), null, out Vector2 local);
            _hoverText.rectTransform.anchoredPosition = local;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (var p in _outlined)
                if (p != null) p.SetOutlineLayer(0);
            _outlined.Clear();
        }
    }
}
