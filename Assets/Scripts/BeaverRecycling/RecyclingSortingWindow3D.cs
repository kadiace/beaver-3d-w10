using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace BeaverRecycling
{
    public sealed class RecyclingSortingWindow3D : MonoBehaviour
    {
        private const float PHYSICS_STEP = 0.02f;
        private const float FINISH_DELAY = 3f;
        private const float RESULT_SECONDS = 1.3f;
        private const float LINE_REACH = 22f;
        private const float ITEM_SCALE = 0.25f;

        private static SimulationMode2D _previousSimulationMode;
        private static int _activeSimulationWindows;

        [Header("Window")]
        [SerializeField] private RectTransform _playfield;
        [SerializeField] private RectTransform _feedBoard;
        [SerializeField] private RectTransform _judgeLine;
        [SerializeField] private Image _spriteTemplate;
        [SerializeField] private Text _status;
        [SerializeField] private Text _score;

        [Header("Falling items")]
        [SerializeField, Min(0.02f), Tooltip("Maximum time between item drops, in seconds.")]
        private float _maximumDropInterval = 0.32f / 1.5f;
        [SerializeField, Range(0f, 1f), Tooltip("0: fixed maximum interval. 1: random interval from a physics step up to the maximum.")]
        private float _dropIntervalVariance = 0.35f;
        [SerializeField, Min(0f), Tooltip("Maximum horizontal offset from the top center, in playfield units.")]
        private float _maximumHorizontalOffset = 90f;
        [SerializeField, Range(0f, 1f), Tooltip("0: drop at center. 1: spread across the full horizontal range.")]
        private float _horizontalOffsetVariance = 0.8f;
        [SerializeField, Min(0.1f)] private float _fallGravityScale = 50f;

        private readonly List<RectTransform> _sprites = new List<RectTransform>();
        private readonly List<float> _radii = new List<float>();
        private readonly List<RectTransform> _visuals = new List<RectTransform>();
        private readonly List<GameObject> _borders = new List<GameObject>();
        private Action<RecyclingSubmission, int> _completed;
        private RecyclingDefinition[] _definitions;
        private RecyclingCategory _chute;
        private RecyclingSubmission _submission;
        private bool[] _excluded;
        private bool _simulationOwned;
        private bool _finished;
        private float _elapsed;
        private float _physicsAccumulator;
        private float _finishedAt;
        private float _lastSpawnedAt;
        private float _nextSpawnAt;
        private int _spawnedCount;
        private int _includedCount;
        private int _selectedIndex = -1;

        /// <summary>제출 아이템을 섞고 상단 중앙에서 중력으로 떨어지는 물리 시뮬레이션을 시작한다.</summary>
        public void Play(IReadOnlyList<RecyclingDefinition> definitions, RecyclingCategory chute,
            Action<RecyclingSubmission, int> completed)
        {
            _completed = completed;
            _chute = chute;
            _definitions = new RecyclingDefinition[definitions.Count];
            for (int i = 0; i < definitions.Count; i++) _definitions[i] = definitions[i];
            ShuffleDefinitions();
            _excluded = new bool[definitions.Count];
            _elapsed = 0f;
            _physicsAccumulator = 0f;
            _lastSpawnedAt = 0f;
            _nextSpawnAt = 0f;
            _spawnedCount = 0;
            _selectedIndex = -1;
            _finished = false;
            _score.gameObject.SetActive(false);
            _status.text = "스페이스 · 빨간 선의 아이템 제외";
            _spriteTemplate.gameObject.SetActive(false);
            _feedBoard.gameObject.SetActive(false);
            if (!_simulationOwned)
            {
                if (_activeSimulationWindows == 0) _previousSimulationMode = Physics2D.simulationMode;
                _activeSimulationWindows++;
                _simulationOwned = true;
            }
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        void Update()
        {
            if (_completed == null) return;
            float deltaTime = Time.unscaledDeltaTime;
            _elapsed += deltaTime;
            while (_spawnedCount < _definitions.Length && _elapsed >= _nextSpawnAt)
            {
                if (!TrySpawnNext()) break;
                _nextSpawnAt = _elapsed + NextDropInterval();
            }
            _physicsAccumulator += deltaTime;
            while (_physicsAccumulator >= PHYSICS_STEP)
            {
                if (Physics2D.simulationMode != SimulationMode2D.Script)
                    Physics2D.simulationMode = SimulationMode2D.Script;
                Physics2D.Simulate(PHYSICS_STEP);
                _physicsAccumulator -= PHYSICS_STEP;
            }
            for (int i = 0; i < _visuals.Count; i++)
                _visuals[i].rotation = Quaternion.identity;
            if (!_finished)
            {
                RefreshTarget();
                if (Managers.Input.SortExcludePressed) ExcludeNearest();
                if (_spawnedCount == _definitions.Length && _elapsed >= _lastSpawnedAt + FINISH_DELAY)
                    FinishSorting();
            }
            if (!_finished || _elapsed < _finishedAt + RESULT_SECONDS) return;
            Action<RecyclingSubmission, int> completed = _completed;
            _completed = null;
            completed(_submission, _includedCount);
        }

        void OnDestroy()
        {
            if (!_simulationOwned) return;
            _activeSimulationWindows--;
            if (_activeSimulationWindows == 0) Physics2D.simulationMode = _previousSimulationMode;
        }

        /// <summary>Fisher-Yates 방식으로 제출 아이템의 투입 순서를 섞는다.</summary>
        private void ShuffleDefinitions()
        {
            for (int i = _definitions.Length - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                RecyclingDefinition temp = _definitions[i];
                _definitions[i] = _definitions[swapIndex];
                _definitions[swapIndex] = temp;
            }
        }

        /// <summary>지정된 최대 낙하 간격 안에서 분산 정도에 따라 다음 생성 시간을 정한다.</summary>
        private float NextDropInterval()
        {
            float maximum = Mathf.Max(PHYSICS_STEP, _maximumDropInterval);
            float minimum = Mathf.Lerp(maximum, PHYSICS_STEP, _dropIntervalVariance);
            return UnityEngine.Random.Range(minimum, maximum);
        }

        /// <summary>상단 중앙의 좌우 범위에서 기존 기물과 겹치지 않는 위치를 찾아 중력 낙하를 시작한다.</summary>
        private bool TrySpawnNext()
        {
            RecyclingDefinition definition = _definitions[_spawnedCount];
            Sprite sprite = Resources.Load<Sprite>("Sprites/Voxels/" + definition.Id);
            Vector2 size = sprite.rect.size * ITEM_SCALE;
            float radius = Mathf.Max(size.x, size.y) * 0.5f;
            const float padding = 3f;
            float spawnY = _playfield.rect.yMax - radius - padding;
            float allowedOffset = Mathf.Max(0f, _playfield.rect.width * 0.5f - radius - padding);
            float horizontalRange = Mathf.Min(allowedOffset, _maximumHorizontalOffset) * _horizontalOffsetVariance;
            Vector2 spawnPosition = default;
            bool clear = false;

            // If the previous item is still near the entry, defer this drop instead of spawning overlapped bodies.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float spawnX = UnityEngine.Random.Range(-horizontalRange, horizontalRange);
                Vector2 candidate = new Vector2(spawnX, spawnY);
                bool overlaps = false;
                for (int i = 0; i < _sprites.Count; i++)
                {
                    if (_excluded[i]) continue;
                    Vector2 existing = _playfield.InverseTransformPoint(_sprites[i].position);
                    float separation = radius + _radii[i] + 2f;
                    if ((candidate - existing).sqrMagnitude >= separation * separation) continue;
                    overlaps = true;
                    break;
                }
                if (overlaps) continue;
                spawnPosition = candidate;
                clear = true;
                break;
            }
            if (!clear) return false;

            _spawnedCount++;
            _lastSpawnedAt = _elapsed;
            Image image = Instantiate(_spriteTemplate, _playfield);
            image.name = definition.Id;
            image.sprite = sprite;
            image.preserveAspect = true;
            image.gameObject.SetActive(true);
            RectTransform rect = image.rectTransform;
            rect.sizeDelta = size;
            rect.anchoredPosition = spawnPosition;
            rect.rotation = Quaternion.identity;
            _sprites.Add(rect);
            _radii.Add(radius);
            Image visual = Instantiate(_spriteTemplate, rect);
            visual.name = "Visual";
            visual.sprite = image.sprite;
            visual.preserveAspect = true;
            visual.rectTransform.sizeDelta = rect.sizeDelta;
            visual.rectTransform.anchoredPosition = Vector2.zero;
            visual.gameObject.SetActive(true);
            image.enabled = false;
            _visuals.Add(visual.rectTransform);
            _borders.Add(CreateSelectionBorder(visual.rectTransform));
            CircleCollider2D circle = image.gameObject.AddComponent<CircleCollider2D>();
            circle.radius = radius;
            Rigidbody2D body = image.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = _fallGravityScale;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            return true;
        }

        /// <summary>이미지의 바깥 크기를 늘리지 않는 흰색 선택 테두리를 이미지 안쪽에 만든다.</summary>
        private static GameObject CreateSelectionBorder(RectTransform visual)
        {
            var border = new GameObject("Selection Border", typeof(RectTransform));
            RectTransform rect = border.GetComponent<RectTransform>();
            rect.SetParent(visual, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            CreateBorderEdge(rect, "Top", new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, 2f), new Vector2(0f, -1f));
            CreateBorderEdge(rect, "Bottom", Vector2.zero, new Vector2(1f, 0f),
                new Vector2(0f, 2f), new Vector2(0f, 1f));
            CreateBorderEdge(rect, "Left", Vector2.zero, new Vector2(0f, 1f),
                new Vector2(2f, 0f), new Vector2(1f, 0f));
            CreateBorderEdge(rect, "Right", new Vector2(1f, 0f), Vector2.one,
                new Vector2(2f, 0f), new Vector2(-1f, 0f));
            border.SetActive(false);
            return border;
        }

        /// <summary>선택 테두리의 한 변을 지정한 앵커와 두께로 생성한다.</summary>
        private static void CreateBorderEdge(RectTransform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            var edge = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = edge.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            edge.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>판정선에 가장 가까운 활성 아이템 한 개를 흰색으로 강조한다.</summary>
        private void RefreshTarget()
        {
            if (_selectedIndex >= 0) _borders[_selectedIndex].SetActive(false);
            _selectedIndex = -1;
            float nearest = float.PositiveInfinity;
            float lineY = _playfield.InverseTransformPoint(_judgeLine.position).y;
            for (int i = 0; i < _spawnedCount; i++)
            {
                if (_excluded[i]) continue;
                float distance = Mathf.Abs(_playfield.InverseTransformPoint(_sprites[i].position).y - lineY);
                if (distance >= nearest) continue;
                nearest = distance;
                _selectedIndex = i;
            }
            if (_selectedIndex >= 0 && nearest <= LINE_REACH)
                _borders[_selectedIndex].SetActive(true);
        }

        /// <summary>선 근처의 강조 아이템을 비활성화하고 최종 점수에서 제외한다.</summary>
        private void ExcludeNearest()
        {
            if (_selectedIndex < 0) return;
            int index = _selectedIndex;
            float lineY = _playfield.InverseTransformPoint(_judgeLine.position).y;
            float itemY = _playfield.InverseTransformPoint(_sprites[index].position).y;
            if (Mathf.Abs(itemY - lineY) > LINE_REACH) return;
            _excluded[index] = true;
            _borders[index].SetActive(false);
            _sprites[index].gameObject.SetActive(false);
            _selectedIndex = -1;
            _status.text = "아이템 1개 제외";
        }

        /// <summary>모든 투입이 끝난 뒤 남은 아이템만 한 번 평가하고 최종 점수를 표시한다.</summary>
        private void FinishSorting()
        {
            _finished = true;
            _finishedAt = _elapsed;
            if (_selectedIndex >= 0) _borders[_selectedIndex].SetActive(false);
            _selectedIndex = -1;
            var included = new List<RecyclingDefinition>(_definitions.Length);
            for (int i = 0; i < _definitions.Length; i++)
                if (!_excluded[i]) included.Add(_definitions[i]);
            _includedCount = included.Count;
            _submission = RecyclingRules.EvaluateDefinitions(included, _chute);
            _score.text = (_submission.Score >= 0 ? "+" : "") + _submission.Score + "점";
            _score.gameObject.SetActive(true);
            _status.text = "분류 완료 · " + _includedCount + "개 투입";
        }
    }
}
