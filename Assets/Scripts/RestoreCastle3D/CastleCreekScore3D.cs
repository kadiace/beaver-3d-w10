using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RestoreCastle3D
{
    /// <summary>Feedback and settlement for the endless creek mode, independent of the legacy stage HUD.</summary>
    public sealed class CastleCreekScore3D : MonoBehaviour
    {
        public static CastleCreekScore3D Active { get; private set; }
        private const float AwardedGroupLifetime = 2f;
        private const float PreviewRefreshInterval = 0.15f;

        private readonly HashSet<CastlePiece3D> _registered = new HashSet<CastlePiece3D>();
        private readonly Dictionary<CastlePiece3D, int> _lastPoints = new Dictionary<CastlePiece3D, int>();
        private readonly HashSet<CastlePiece3D> _outlined = new HashSet<CastlePiece3D>();
        private readonly Dictionary<CastlePiece3D, int> _previewCandidates =
            new Dictionary<CastlePiece3D, int>();
        private CastleCatalog3D _catalog;
        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _rect;
        private CastleFloatingText3D _floating;
        private TextMeshProUGUI _scoreText;
        private TextMeshProUGUI _hoverText;
        private RectTransform _hoverPanel;
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
        private CastlePiece3D _trackedPiece;
        private CastlePiece3D _heldPiece;
        private float _nextPreviewRefresh;

        public int TotalScore => _score;
        public CastlePiece3D TrackedPiece =>
            _trackedPiece != null && _trackedPiece.isActiveAndEnabled && !_trackedPiece.IsSettling &&
            _registered.Contains(_trackedPiece) ? _trackedPiece : null;

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

            // Stable, readable panel: never attach it to a moving mouse cursor.
            var panelObject = new GameObject("Synergy Details Panel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(_rect, false);
            _hoverPanel = (RectTransform)panelObject.transform;
            _hoverPanel.anchorMin = _hoverPanel.anchorMax = new Vector2(0f, 1f);
            _hoverPanel.pivot = new Vector2(0f, 1f);
            _hoverPanel.anchoredPosition = new Vector2(22f, -22f);
            _hoverPanel.sizeDelta = new Vector2(490f, 390f);
            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.05f, 0.08f, 0.12f, 0.88f);
            background.raycastTarget = false;
            _hoverText = CreateLabel("Synergy Details", 22, TextAlignmentOptions.TopLeft);
            _hoverText.transform.SetParent(_hoverPanel, false);
            _hoverText.rectTransform.anchorMin = Vector2.zero;
            _hoverText.rectTransform.anchorMax = Vector2.one;
            _hoverText.rectTransform.offsetMin = new Vector2(16f, 12f);
            _hoverText.rectTransform.offsetMax = new Vector2(-16f, -14f);
            _hoverText.enableWordWrapping = true;
            _hoverText.overflowMode = TextOverflowModes.Ellipsis;
            _hoverPanel.gameObject.SetActive(false);
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
            if (_hovered == piece || _trackedPiece == piece)
            {
                _hovered = null;
                if (_hoverPanel != null) _hoverPanel.gameObject.SetActive(false);
            }
            if (_trackedPiece == piece) _trackedPiece = null;
            if (_heldPiece == piece) _heldPiece = null;
            _previewCandidates.Remove(piece);
            if (_outlined.Remove(piece) && piece != null && piece.isActiveAndEnabled)
                piece.SetOutlineLayer(0);
        }

        /// <summary>Recalculate only components touched by an added or removed edge.</summary>
        public void Refresh(CastlePiece3D a, CastlePiece3D b = null)
        {
            if (_catalog == null || _batching) return;
            if ((a != null && a.IsSettling) || (b != null && b.IsSettling)) return;
            _nextPreviewRefresh = 0f;
            var visited = new HashSet<CastlePiece3D>();
            foreach (var start in new[] { a, b })
            {
                if (start == null || !start.isActiveAndEnabled || start.IsSettling ||
                    visited.Contains(start)) continue;
                List<CastlePiece3D> group = CastleSynergy3D.Collect(start);
                foreach (var p in group) visited.Add(p);
                var calculated = CastleSynergy3D.Evaluate(group, _catalog);
                foreach (CastlePiece3D p in group)
                {
                    if (!_registered.Contains(p) || p.IsSettling) continue;
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
            // Snapshot score and membership before any joint/collider state changes.
            int earned = CastleSynergy3D.Evaluate(group, _catalog).Total;
            foreach (var member in group)
            {
                member.MarkSettling();
                Unregister(member);
            }
            _nextPreviewRefresh = 0f;
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
            // All members remain visible but inert for two seconds. No second settlement.
            StartCoroutine(ReturnSettledGroup(group));
        }

        private IEnumerator ReturnSettledGroup(List<CastlePiece3D> group)
        {
            yield return new WaitForSeconds(AwardedGroupLifetime);
            foreach (CastlePiece3D member in group)
                if (member != null && member.IsSettling)
                    Managers.Resource.Destroy(member.gameObject);
        }

        /// <summary>Fallen-out objects leave without score; only the Z=-15 boundary awards points.</summary>
        public void Discard(CastlePiece3D piece)
        {
            if (piece == null || piece.IsSettling) return;
            var group = CastleSynergy3D.Collect(piece);
            foreach (var member in group) member.MarkSettling();
            foreach (var member in group)
            {
                Unregister(member);
                Managers.Resource.Destroy(member.gameObject);
            }
        }

        /// <summary>Called by gameplay input when a pickup is held or released.</summary>
        public void SetHeldPiece(CastlePiece3D piece)
        {
            _heldPiece = piece != null && !piece.IsSettling ? piece : null;
            _nextPreviewRefresh = 0f;
            _previewCandidates.Clear();
            if (_heldPiece != null) StopTracking();
            ClearOutlines();
            _hoverPanel.gameObject.SetActive(false);
        }

        /// <summary>Right-click focus; using the same raycast selection criteria as hover.</summary>
        public bool TrackUnderCursor(Vector2 cursor)
        {
            CastlePiece3D candidate = PickAt(cursor);
            _trackedPiece = candidate;
            _hovered = candidate;
            if (candidate == null) _hoverPanel.gameObject.SetActive(false);
            return candidate != null;
        }

        public void StopTracking()
        {
            _trackedPiece = null;
        }

        private CastlePiece3D PickAt(Vector2 position)
        {
            if (_camera == null) return null;
            if (!Physics.Raycast(_camera.ScreenPointToRay(position), out RaycastHit hit, 300f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
            CastlePiece3D candidate = hit.collider.GetComponentInParent<CastlePiece3D>();
            return candidate != null && _registered.Contains(candidate) && !candidate.IsSettling
                ? candidate : null;
        }

        private void ClearOutlines()
        {
            foreach (CastlePiece3D p in _outlined)
                if (p != null && p.isActiveAndEnabled) p.SetOutlineLayer(0);
            _outlined.Clear();
        }

        private void UpdateHeldPreview()
        {
            if (_heldPiece == null || !_heldPiece.isActiveAndEnabled || _heldPiece.IsSettling ||
                !_registered.Contains(_heldPiece))
            {
                SetHeldPiece(null);
                return;
            }

            if (Time.unscaledTime >= _nextPreviewRefresh)
            {
                _previewCandidates.Clear();
                _nextPreviewRefresh = Time.unscaledTime + PreviewRefreshInterval;
                foreach (CastlePiece3D candidate in _registered)
                {
                    if (candidate == null || candidate == _heldPiece || candidate.IsSettling ||
                        !candidate.isActiveAndEnabled)
                        continue;
                    Vector3 vp = _camera.WorldToViewportPoint(candidate.transform.position);
                    if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                        continue;
                    int delta = CastleSynergy3D.PreviewAttachmentDelta(_heldPiece, candidate, _catalog);
                    if (delta != 0) _previewCandidates[candidate] = delta;
                }
            }
            ClearOutlines();
            foreach (var pair in _previewCandidates)
            {
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Key.IsSettling ||
                    !_registered.Contains(pair.Key)) continue;
                pair.Key.SetOutlineLayer(pair.Value < 0 ? 8u : 4u);
                _outlined.Add(pair.Key);
            }
            _heldPiece.SetOutlineLayer(2u);
            _outlined.Add(_heldPiece);
            _hoverPanel.gameObject.SetActive(false);
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
            if (_heldPiece != null)
            {
                UpdateHeldPreview();
                return;
            }

            // Tracking takes priority over the mouse pointer, including while the
            // object moves and the camera follows it. A pooled/settled piece cannot
            // remain tracked because Unregister() clears the reference.
            CastlePiece3D next = TrackedPiece;
            Mouse mouse = Mouse.current;
            if (next == null && mouse != null && !mouse.rightButton.isPressed)
                next = PickAt(mouse.position.ReadValue());

            _hovered = next;
            ClearOutlines();
            if (next == null)
            {
                _hoverPanel.gameObject.SetActive(false);
                return;
            }

            List<CastlePiece3D> group = CastleSynergy3D.Collect(next);
            CastleSynergy3D.Result result = CastleSynergy3D.Evaluate(group, _catalog);
            foreach (CastlePiece3D p in result.Contributors)
            {
                if (p == null || p == next || p.IsSettling || !_registered.Contains(p))
                    continue;
                p.SetOutlineLayer(_catalog.Get(p.MaterialId).category == "쓰레기" ? 8u : 4u);
                _outlined.Add(p);
            }
            next.SetOutlineLayer(2u);
            _outlined.Add(next);

            CastleMaterial3D material = _catalog.Get(next.MaterialId);
            string description = material.abilities == null || material.abilities.Length == 0
                ? "없음" : string.Join("\n", material.abilities);
            _hoverText.text = material.name + (TrackedPiece == next ? "  [우클릭 추적 중]" : "") +
                "\n예상 점수: " + result.Total + "점\n" +
                (group.Count > 1
                    ? string.Join("\n", group.Take(6).Select(p =>
                        _catalog.Get(p.MaterialId).name + " " +
                        (result.Points.TryGetValue(p, out int n) && n > 0 ? "+" : "") +
                        (result.Points.TryGetValue(p, out int value) ? value : 0)))
                    : "아직 부착된 기물 없음") +
                "\n\n능력: " + description;
            _hoverPanel.gameObject.SetActive(true);
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
