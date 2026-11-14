using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.EventSystems;

namespace BeaverRecycling
{
    public sealed class RecyclingGame3D : MonoBehaviour
    {
        private enum DayPhase { Preparation, Supply, Closing, Result, Paused, Sorting, Clear }
        private const int NO_TRASH_LOSS_SCORE_BONUS = 10;
        private const int NO_TRASH_LOSS_WOOD_BONUS = 3;
        private const int NO_MISCLASSIFICATION_SCORE_BONUS = 10;
        private const int NO_MISCLASSIFICATION_WOOD_BONUS = 3;
        private const int SURPLUS_SCORE_PER_BONUS_WOOD = 10;
        private const int FIRST_BOMB_DAY = 4;
        private const string BOMB_PREFAB_PATH = "Prefabs/Recycling/bomb";
        private static readonly int[] UPGRADE_COSTS = { 2, 4, 7 };

        [Header("Scene objects")]
        [SerializeField] private RecyclingCatalog _catalog;
        [SerializeField] private RecyclingBeaver3D _beaver;
        [SerializeField] private BoxCollider _pickupSensor;
        [SerializeField] private RecyclingChute3D[] _chutes;
        [SerializeField] private RecyclingHud3D _hud;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _spawnPoint;

        [Header("Working day")]
        [SerializeField, Min(1f)] private float _supplySeconds = 60f;
        [SerializeField, Min(1f)] private float _closingSeconds = 30f;
        [SerializeField] private int[] _targets = { 30, 50, 70, 90, 110 };
        [SerializeField] private float[] _spawnIntervals = { 2f, 1.8f, 1.6f, 1.4f, 1.2f };
        [SerializeField] private float[] _flowSpeeds = { 1.2f, 1.35f, 1.5f, 1.65f, 1.8f };

        [Header("Bombs")]
        [SerializeField, Range(0f, 1f)] private float _bombSpawnChance = 0.05f;

        [Header("River")]
        [SerializeField] private Vector2 _riverMinimum = new Vector2(-12f, 15f);
        [SerializeField] private Vector2 _riverMaximum = new Vector2(12f, 45f);
        [SerializeField] private float _waterfallZ = 15f;
        [SerializeField] private float _fallbackY = -10f;
        [SerializeField] private float _waterY = -1.95f;
        [SerializeField, Min(0.1f)] private float _spawnHalfWidth = 8.5f;

        [Header("Beaver")]
        [SerializeField, Min(0.1f)] private float _baseSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float _baseReach = 1.8f;
        [SerializeField, Min(1)] private int _baseCapacity = 6;

        [Header("Bundle")]
        [SerializeField, Min(0f)] private float _splitScatterDistance = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _minimumProjectedOverlap = 0.3f;

        private readonly HashSet<RecyclingItem> _items = new HashSet<RecyclingItem>();
        private readonly HashSet<BombExplosion3D> _bombs = new HashSet<BombExplosion3D>();
        private readonly List<RecyclingItem> _held = new List<RecyclingItem>();
        private readonly List<Rect> _projectedOverlaps = new List<Rect>();
        private readonly List<float> _projectedXEdges = new List<float>();
        private readonly List<Vector2> _projectedZIntervals = new List<Vector2>();
        private readonly int[] _levels = new int[3];
        private readonly int[] _startLevels = new int[3];
        private Collider[] _overlapBuffer = new Collider[64];
        private RecyclingItem _tracked;
        private DayPhase _phase;
        private DayPhase _beforePause;
        private DayPhase _beforeSorting;
        private Vector2 _movement;
        private Vector2 _dragDistance;
        private Vector3 _cameraTarget;
        private bool _cameraGesture;
        private bool _cameraDragged;
        private bool _followBeaver = true;
        private bool _passed;
        private int _day = 1;
        private int _wood;
        private int _dayStartWood;
        private int _dayScore;
        private int _totalScore;
        private int _correct;
        private int _wrong;
        private int _lost;
        private int _bestBundle;
        private int _dayWood;
        private float _elapsed;
        private float _nextSpawn;
        private float _capacityNoticeAt;
        private float _beaverStunnedUntil;

        public bool IsWorking => _phase == DayPhase.Supply || _phase == DayPhase.Closing;
        public float WaterfallZ => _waterfallZ;
        public float FallbackY => _fallbackY;
        public float WaterY => _waterY;
        public float FlowSpeed => _flowSpeeds[_day - 1];
#if UNITY_EDITOR
        public IReadOnlyList<RecyclingDefinition> DebugItems => _catalog.DebugItems;
        public Vector3 DebugSupplyPosition => _spawnPoint.position;
        public Vector3 DebugBeaverPosition => new Vector3(_beaver.transform.position.x, _waterY + 1.5f,
            _beaver.transform.position.z) + _beaver.transform.forward * 2f;
#endif
        private int Capacity => _baseCapacity + 2 * _levels[0];
        private float Reach => _baseReach + 0.3f * _levels[2];

        void Awake()
        {
            if (_pickupSensor != null) return;
            _pickupSensor = _beaver.GetComponent<BoxCollider>();
            if (_pickupSensor != null) return;
            _pickupSensor = _beaver.gameObject.AddComponent<BoxCollider>();
            _pickupSensor.isTrigger = true;
            _pickupSensor.size = new Vector3(2.2f, 1.5f, 2.4f);
            _pickupSensor.center = new Vector3(0f, 0.4f, 1f);
        }
        void Start()
        {
            Managers.Input.UseRecyclingControls(true);
            _hud.Initialize(this);
            _camera.orthographic = true;
            _camera.orthographicSize = 13.5f;
            ResetCameraTarget();
            PrepareDay();
        }

        void Update()
        {
            if (Managers.Input.CancelPressed)
            {
                TogglePause();
                return;
            }
            if (IsWorking)
            {
                bool stunned = Time.time < _beaverStunnedUntil;
                _beaver.SetStunned(stunned);
                _movement = stunned ? Vector2.zero : Managers.Input.MoveInput;
                if (!stunned && _movement.sqrMagnitude > 0.001f)
                {
                    _tracked = null;
                    _followBeaver = true;
                }
                if (!stunned && Managers.Input.InteractPressed) HandleInteract();
                if (!stunned && Managers.Input.SplitBundlePressed && _held.Count > 0) ReleaseBundle(true);
                _elapsed += Time.deltaTime;
                if (_phase == DayPhase.Supply && _elapsed >= _supplySeconds)
                {
                    _phase = DayPhase.Closing;
                    _hud.Message("공급 종료 · 남은 기물을 정리하세요");
                }
                if (_phase == DayPhase.Supply)
                {
                    _nextSpawn -= Time.deltaTime;
                    if (_nextSpawn <= 0f)
                    {
                        SpawnItem();
                        _nextSpawn += _spawnIntervals[_day - 1];
                    }
                }
                if (_elapsed >= _supplySeconds + _closingSeconds ||
                    (_phase == DayPhase.Closing && _items.Count == 0 && _bombs.Count == 0)) FinishDay();
                if (IsWorking) RefreshHover();
            }
            RefreshHud();
            RefreshChutes();
        }

        void FixedUpdate()
        {
            if (!IsWorking) return;
            float slowdown = Mathf.Max(0.6f, 1f - 0.05f * Mathf.Max(0, _held.Count - 3));
            Vector3 previousBeaverPosition = _beaver.transform.position;
            Quaternion previousBeaverRotation = _beaver.transform.rotation;
            _beaver.Move(_movement, (_baseSpeed + 0.5f * _levels[1]) * slowdown, Time.fixedDeltaTime);
            if (_held.Count == 0) return;
            Quaternion rotationDelta = _beaver.transform.rotation * Quaternion.Inverse(previousBeaverRotation);
            Vector3 beaverPosition = _beaver.transform.position;
            foreach (RecyclingItem member in _held)
            {
                member.Body.position = beaverPosition + rotationDelta * (member.Body.position - previousBeaverPosition);
                member.Body.rotation = rotationDelta * member.Body.rotation;
            }
            Vector3 delta = _beaver.CarryPoint.position - _held[0].Body.position;
            foreach (RecyclingItem member in _held) member.Body.position += delta;
            Physics.SyncTransforms();
            CollectContacts();
        }

        void LateUpdate()
        {
            if (IsWorking) ReadCameraInput();
            if (_tracked != null && !_tracked.IsResolved) _cameraTarget = Vector3.Lerp(_cameraTarget,
                new Vector3(_tracked.transform.position.x, 0f, _tracked.transform.position.z),
                1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            else if (_followBeaver) _cameraTarget = Vector3.Lerp(_cameraTarget,
                new Vector3(_beaver.transform.position.x, 0f, _beaver.transform.position.z + 2f),
                1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            _cameraTarget.x = Mathf.Clamp(_cameraTarget.x, -9f, 9f);
            _cameraTarget.z = Mathf.Clamp(_cameraTarget.z, 19f, 41f);
            _camera.transform.position = _cameraTarget - _camera.transform.forward * 32f;
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        /// <summary>기물 위치를 받아 하천의 수평 영역 안에 있는지 반환한다.</summary>
        public bool IsInsideRiver(Vector3 position) => position.x >= _riverMinimum.x && position.x <= _riverMaximum.x &&
            position.z >= _riverMinimum.y && position.z <= _riverMaximum.y;

#if UNITY_EDITOR
        /// <summary>기물 인덱스와 위치를 받아 작업 중에 해당 기물을 생성하고 관리 목록에 등록한 성공 여부를 반환한다.</summary>
        public bool DebugSpawnItem(int index, Vector3 position)
        {
            if (!IsWorking || index < 0 || index >= _catalog.DebugItems.Count) return false;
            RecyclingDefinition definition = _catalog.DebugItems[index];
            GameObject instance = Managers.Resource.Instantiate("Prefabs/Recycling/" + definition.Prefab.name,
                position: position, rotation: Quaternion.identity);
            RecyclingItem item = instance.GetComponent<RecyclingItem>();
            instance.name = definition.Name;
            item.Initialize(this, definition);
            _items.Add(item);
            return true;
        }

        /// <summary>위치를 받아 작업 중에 폭탄을 생성하고 관리 목록에 등록한 성공 여부를 반환한다.</summary>
        public bool DebugSpawnBomb(Vector3 position)
        {
            if (!IsWorking) return false;
            GameObject instance = Managers.Resource.Instantiate(BOMB_PREFAB_PATH,
                position: position, rotation: Quaternion.identity);
            BombExplosion3D bomb = instance.GetComponent<BombExplosion3D>();
            instance.name = "폭탄";
            bomb.Initialize(this);
            _bombs.Add(bomb);
            return true;
        }
#endif

        /// <summary>활성 정의를 난수로 골라 풀에서 생성하고 새 기물의 상태를 초기화한다.</summary>
        private void SpawnItem()
        {
            RecyclingDefinition definition = _catalog.Draw(_day);
            Vector3 position = _spawnPoint.position;
            position.x += UnityEngine.Random.Range(-_spawnHalfWidth, _spawnHalfWidth);
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            GameObject instance = Managers.Resource.Instantiate("Prefabs/Recycling/" + definition.Prefab.name,
                position: position, rotation: rotation);
            RecyclingItem item = instance.GetComponent<RecyclingItem>();
            instance.name = definition.Name;
            item.Initialize(this, definition);
            _items.Add(item);
            if (_day >= FIRST_BOMB_DAY && _bombSpawnChance > 0f &&
                (_bombSpawnChance >= 1f || UnityEngine.Random.value < _bombSpawnChance)) SpawnBomb();
        }

        /// <summary>상류 공급 위치를 받아 폭탄을 별도로 생성하고 이동 흐름에 등록한다.</summary>
        private void SpawnBomb()
        {
            Vector3 position = _spawnPoint.position;
            position.x += UnityEngine.Random.Range(-_spawnHalfWidth, _spawnHalfWidth);
            position.z += UnityEngine.Random.Range(-0.5f, 0.5f);
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            GameObject instance = Managers.Resource.Instantiate(BOMB_PREFAB_PATH,
                position: position, rotation: rotation);
            BombExplosion3D bomb = instance.GetComponent<BombExplosion3D>();
            instance.name = "폭탄";
            bomb.Initialize(this);
            _bombs.Add(bomb);
        }

        /// <summary>센서 박스와 겹친 활성 기물 중 비버와 가장 가까운 대상을 반환한다.</summary>
        private RecyclingItem FindPickupTarget()
        {
            Transform sensorTransform = _pickupSensor.transform;
            Vector3 scale = sensorTransform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_pickupSensor.size * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 center = sensorTransform.TransformPoint(_pickupSensor.center);
            int count;
            while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer,
                sensorTransform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == _overlapBuffer.Length)
                Array.Resize(ref _overlapBuffer, _overlapBuffer.Length * 2);

            RecyclingItem closest = null;
            float closestDistance = float.PositiveInfinity;
            Vector3 playerPosition = _beaver.transform.position;
            for (int i = 0; i < count; i++)
            {
                RecyclingItem item = _overlapBuffer[i].GetComponentInParent<RecyclingItem>();
                if (item == null || item.IsHeld || item.IsResolved || !_items.Contains(item)) continue;
                Vector3 carryPosition = _beaver.CarryPoint.position;
                Vector3 pickupPoint = item.Collider.ClosestPoint(carryPosition);
                if ((pickupPoint - carryPosition).sqrMagnitude > Reach * Reach) continue;
                Vector3 nearestPoint = item.Collider.ClosestPoint(playerPosition);
                float distance = (nearestPoint - playerPosition).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closest = item;
                closestDistance = distance;
            }
            return closest;
        }

        /// <summary>현재 운반 상태와 투입구 위치에 따라 E 입력을 집기·내려놓기·투입으로 처리한다.</summary>
        private void HandleInteract()
        {
            if (_held.Count == 0)
            {
                PickUp(FindPickupTarget());
                return;
            }

            RecyclingChute3D chute = FindChute();
            if (chute != null) Submit(chute);
            else ReleaseBundle(false);
        }

        /// <summary>선택된 기물 묶음의 거리와 크기를 검사하고 전체를 비버의 운반 상태로 바꾼다.</summary>
        private void PickUp(RecyclingItem item)
        {
            if (item == null || item.IsHeld || item.IsResolved || !_items.Contains(item)) return;
            float distance = Vector3.Distance(item.Collider.ClosestPoint(_beaver.CarryPoint.position), _beaver.CarryPoint.position);
            if (distance > Reach) { _hud.Message("가까이 이동해서 집으세요"); return; }
            List<RecyclingItem> group = RecyclingItem.Collect(item);
            if (group.Count > Capacity) { _hud.Message("운반 한도 " + Capacity + "개 · 현재 묶음 " + group.Count + "개"); return; }
            _tracked = null;
            _held.Add(item);
            foreach (RecyclingItem member in group) if (member != item) _held.Add(member);
            foreach (RecyclingItem member in _held) member.SetHeld(true);
        }

        /// <summary>운반 묶음과 새 기물의 XZ 겹침 비율을 검사해 기준을 넘은 상대 묶음을 한도 안에서 합친다.</summary>
        private void CollectContacts()
        {
            foreach (RecyclingItem other in _items)
            {
                if (other.IsHeld || !other.CanJoin) continue;
                if (!TryGetProjectedContactPoint(other.Collider.bounds, out RecyclingItem held, out Vector3 contactPoint)) continue;
                List<RecyclingItem> incoming = RecyclingItem.Collect(other);
                if (incoming.Any(p => p.IsHeld || !p.CanJoin)) continue;
                if (_held.Count + incoming.Count > Capacity)
                {
                    if (Time.time > _capacityNoticeAt) { _hud.Message("운반 한도가 가득 찼어요"); _capacityNoticeAt = Time.time + 1.5f; }
                    continue;
                }
                contactPoint = RaiseJoiningItemToContact(held, other, contactPoint);
                held.Attach(other, contactPoint);
                foreach (RecyclingItem member in incoming) { member.SetHeld(true); _held.Add(member); }
            }
        }

        /// <summary>새 기물의 바운드를 받아 운반 묶음과의 XZ 겹침 비율과 연결할 기물·접촉점을 반환한다.</summary>
        private bool TryGetProjectedContactPoint(Bounds joiningBounds, out RecyclingItem contact, out Vector3 contactPoint)
        {
            contact = null;
            contactPoint = default;
            float joiningArea = joiningBounds.size.x * joiningBounds.size.z;
            if (joiningArea <= 0f) return false;

            float requiredArea = joiningArea * _minimumProjectedOverlap;
            float largestArea = 0f;
            _projectedOverlaps.Clear();
            _projectedXEdges.Clear();
            foreach (RecyclingItem held in _held)
            {
                Bounds heldBounds = held.Collider.bounds;
                float minX = Mathf.Max(heldBounds.min.x, joiningBounds.min.x);
                float maxX = Mathf.Min(heldBounds.max.x, joiningBounds.max.x);
                float minZ = Mathf.Max(heldBounds.min.z, joiningBounds.min.z);
                float maxZ = Mathf.Min(heldBounds.max.z, joiningBounds.max.z);
                if (minX >= maxX || minZ >= maxZ) continue;

                float area = (maxX - minX) * (maxZ - minZ);
                if (area > largestArea)
                {
                    largestArea = area;
                    contact = held;
                    contactPoint = new Vector3((minX + maxX) * 0.5f,
                        (heldBounds.center.y + joiningBounds.center.y) * 0.5f, (minZ + maxZ) * 0.5f);
                }
                if (area >= requiredArea) return true;
                _projectedOverlaps.Add(Rect.MinMaxRect(minX, minZ, maxX, maxZ));
                _projectedXEdges.Add(minX);
                _projectedXEdges.Add(maxX);
            }
            if (_projectedOverlaps.Count == 0) return false;

            _projectedXEdges.Sort();
            float totalArea = 0f;
            for (int i = 0; i < _projectedXEdges.Count - 1; i++)
            {
                float left = _projectedXEdges[i];
                float right = _projectedXEdges[i + 1];
                if (left >= right) continue;

                _projectedZIntervals.Clear();
                foreach (Rect overlap in _projectedOverlaps)
                    if (overlap.xMin < right && overlap.xMax > left)
                        _projectedZIntervals.Add(new Vector2(overlap.yMin, overlap.yMax));
                if (_projectedZIntervals.Count == 0) continue;

                _projectedZIntervals.Sort((a, b) => a.x.CompareTo(b.x));
                float start = _projectedZIntervals[0].x;
                float end = _projectedZIntervals[0].y;
                float length = 0f;
                for (int j = 1; j < _projectedZIntervals.Count; j++)
                {
                    Vector2 interval = _projectedZIntervals[j];
                    if (interval.x > end)
                    {
                        length += end - start;
                        start = interval.x;
                        end = interval.y;
                    }
                    else end = Mathf.Max(end, interval.y);
                }
                totalArea += (right - left) * (length + end - start);
                if (totalArea >= requiredArea) return true;
            }
            return false;
        }

        /// <summary>기존 묶음보다 아래에 있는 새 기물을 올려 충돌체 바운드가 맞닿게 하고 조인트 지점을 조정한다.</summary>
        private static Vector3 RaiseJoiningItemToContact(RecyclingItem held, RecyclingItem joining, Vector3 contactPoint)
        {
            Bounds heldBounds = held.Collider.bounds;
            Bounds joiningBounds = joining.Collider.bounds;
            float lift = heldBounds.min.y - joiningBounds.max.y;
            if (lift <= 0f) return contactPoint;

            Vector3 raisedPosition = joining.Body.position + Vector3.up * lift;
            joining.Body.position = raisedPosition;
            joining.transform.SetPositionAndRotation(raisedPosition, joining.Body.rotation);
            contactPoint.y = heldBounds.min.y;
            return contactPoint;
        }

        /// <summary>분리 여부를 받아 묶음을 내려놓고 결합을 해제하며 분리 시 기물을 묶음 중심에서 바깥으로 흩뜨린다.</summary>
        private void ReleaseBundle(bool split)
        {
            bool scatter = split && _held.Count > 1;
            Vector3 center = Vector3.zero;
            if (scatter)
            {
                foreach (RecyclingItem item in _held) center += item.Body.position;
                center /= _held.Count;
            }

            for (int i = 0; i < _held.Count; i++)
            {
                RecyclingItem item = _held[i];
                item.SetHeld(false);
                if (!split) continue;

                item.DetachAll();
                item.BlockAttachment(0.5f);
                if (!scatter) continue;

                Vector3 direction = item.Body.position - center;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    float angle = 2f * Mathf.PI * i / _held.Count;
                    direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
                else
                {
                    direction.Normalize();
                }

                Vector3 scatteredPosition = item.Body.position + direction * _splitScatterDistance;
                item.Body.position = scatteredPosition;
                item.transform.SetPositionAndRotation(scatteredPosition, item.Body.rotation);
            }

            if (scatter) Physics.SyncTransforms();
            _held.Clear();
        }

        /// <summary>현재 비버 위치에서 사용할 수 있는 가장 가까운 투입구를 반환한다.</summary>
        private RecyclingChute3D FindChute()
        {
            RecyclingChute3D nearest = null;
            float distance = float.PositiveInfinity;
            foreach (RecyclingChute3D chute in _chutes)
            {
                if (!chute.CanUse(_day, _beaver.transform.position)) continue;
                float next = (chute.transform.position - _beaver.transform.position).sqrMagnitude;
                if (next < distance) { nearest = chute; distance = next; }
            }
            return nearest;
        }

        /// <summary>운반 묶음을 제출하고, 다른 재질이 섞였을 때만 분류 미니게임을 연다.</summary>
        private void Submit(RecyclingChute3D chute)
        {
            var submitted = new List<RecyclingItem>(_held);
            RecyclingDefinition[] definitions = submitted.Select(item => item.Definition).ToArray();
            bool needsSorting = definitions.Any(definition => definition.Category != chute.Category);
            Vector3 intakePosition = chute.IntakePosition;
            _held.Clear();
            foreach (RecyclingItem item in submitted) item.Resolve(intakePosition, false);

            if (!needsSorting)
            {
                ApplySubmission(RecyclingRules.EvaluateDefinitions(definitions, chute.Category),
                    definitions.Length, intakePosition);
                return;
            }

            _beforeSorting = _phase;
            _phase = DayPhase.Sorting;
            _movement = Vector2.zero;
            SetUiMode(true);
            _hud.ShowSorting(definitions, chute.Category,
                (submission, includedCount) => CompleteSubmission(submission, includedCount, intakePosition));
        }

        /// <summary>미니게임이 끝나면 작업 단계로 복귀하고 최종 제출 결과를 적용한다.</summary>
        private void CompleteSubmission(RecyclingSubmission submission, int submittedCount, Vector3 intakePosition)
        {
            _phase = _beforeSorting;
            SetUiMode(false);
            ApplySubmission(submission, submittedCount, intakePosition);
        }

        /// <summary>즉시 제출과 미니게임 제출에 동일한 점수 및 기록 처리를 적용한다.</summary>
        private void ApplySubmission(RecyclingSubmission submission, int submittedCount, Vector3 intakePosition)
        {
            _dayScore += submission.Score;
            _correct += submission.Correct;
            _wrong += submission.Wrong;
            if (submission.Correct == submittedCount) _bestBundle = Mathf.Max(_bestBundle, submittedCount);
            _hud.Event((submission.Score - submission.Bonus) + " 점 (+ 묶음 보너스 " + submission.Bonus + " 점)", intakePosition,
                _camera, submission.Score >= 0 ? new Color(0.3f, 1f, 0.8f) : new Color(1f, 0.4f, 0.3f));
        }

        /// <summary>이탈한 기물과 폭포 여부를 받아 쓰레기 감점 또는 목재 점수·자원 보상을 한 번 적용하고 제거한다.</summary>
        public void ResolveExit(RecyclingItem item, bool waterfall, bool animate)
        {
            if (item.IsResolved) return;
            Vector3 position = item.transform.position;
            if (item.Definition.IsWood)
            {
                if (waterfall)
                {
                    _dayScore += RecyclingRules.BASE_SCORE;
                    _wood += item.Definition.WoodYield;
                    _dayWood += item.Definition.WoodYield;
                    if (animate) _hud.Event("+" + RecyclingRules.BASE_SCORE + "점 · +" +
                        item.Definition.WoodYield + " 목재", position, _camera,
                        new Color(1f, 0.8f, 0.4f));
                }
            }
            else
            {
                _dayScore -= RecyclingRules.ESCAPE_PENALTY;
                _lost++;
                if (animate) _hud.Event("-" + RecyclingRules.ESCAPE_PENALTY + "점", position, _camera,
                    new Color(1f, 0.4f, 0.3f));
            }
            item.Resolve(position + Vector3.down * 3f, animate);
        }

        /// <summary>접촉한 폭탄을 한 번 폭발시키고 넉백·기절·정리를 적용한다.</summary>
        public void ExplodeBomb(BombExplosion3D bomb)
        {
            if (!IsWorking || !_bombs.Contains(bomb) || !bomb.TryBeginExplosion()) return;

            Vector3 origin = bomb.transform.position;
            float duration = bomb.KnockbackDuration;
            float itemSpeed = duration > 0f ? 2f * bomb.ItemKnockbackDistance / duration : 0f;
            float beaverSpeed = duration > 0f ? 2f * bomb.BeaverKnockbackDistance / duration : 0f;
            ApplyBlastToItems(origin, bomb.ExplosionRadius, itemSpeed, duration);
            ApplyBlastToBombs(bomb, origin, bomb.ExplosionRadius, itemSpeed, duration);

            Vector3 beaverDirection = HorizontalDirection(origin, _beaver.transform.position);
            _beaver.ApplyKnockback(beaverDirection * beaverSpeed, duration);
            _beaverStunnedUntil = Mathf.Max(_beaverStunnedUntil, Time.time + bomb.StunDuration);
            _movement = Vector2.zero;
            _hud.Message("폭발! 잠시 행동할 수 없어요");

            _bombs.Remove(bomb);
            bomb.ReturnNow();
        }

        /// <summary>폭발 위치, 반경, 넉백 속도와 시간을 받아 범위 안의 기물 묶음에 넉백을 적용한다.</summary>
        private void ApplyBlastToItems(Vector3 origin, float radius, float knockbackSpeed, float duration)
        {
            float radiusSquared = radius * radius;
            var processed = new HashSet<RecyclingItem>();

            if (_held.Count > 0)
            {
                var carried = new List<RecyclingItem>(_held);
                Vector3 carriedCenter = Vector3.zero;
                foreach (RecyclingItem member in carried) carriedCenter += member.Body.position;
                carriedCenter /= carried.Count;
                if (HorizontalDistanceSquared(origin, carriedCenter) <= radiusSquared)
                {
                    foreach (RecyclingItem member in carried) processed.Add(member);
                    Vector3 direction = HorizontalDirection(origin, carriedCenter);
                    ReleaseBundle(true);
                    foreach (RecyclingItem member in carried)
                        member.ApplyKnockback(direction * knockbackSpeed, duration);
                }
            }

            foreach (RecyclingItem item in _items.ToArray())
            {
                if (item.IsResolved || item.IsHeld || processed.Contains(item)) continue;
                List<RecyclingItem> group = RecyclingItem.Collect(item);
                foreach (RecyclingItem member in group) processed.Add(member);

                RecyclingItem closestMember = null;
                float closestDistance = float.PositiveInfinity;
                foreach (RecyclingItem member in group)
                {
                    if (member.IsResolved || member.IsHeld) continue;
                    float distance = HorizontalDistanceSquared(origin, member.Body.position);
                    if (distance > radiusSquared || distance >= closestDistance) continue;
                    closestMember = member;
                    closestDistance = distance;
                }
                if (closestMember == null) continue;

                Vector3 velocityChange = HorizontalDirection(origin, closestMember.Body.position) * knockbackSpeed;
                foreach (RecyclingItem member in group)
                    if (!member.IsResolved && !member.IsHeld) member.ApplyKnockback(velocityChange, duration);
            }
        }

        /// <summary>원본 폭탄과 폭발 위치, 반경, 넉백 속도와 시간을 받아 범위 안의 다른 폭탄을 밀어낸다.</summary>
        private void ApplyBlastToBombs(BombExplosion3D source, Vector3 origin, float radius, float knockbackSpeed, float duration)
        {
            float radiusSquared = radius * radius;
            foreach (BombExplosion3D bomb in _bombs.ToArray())
            {
                if (bomb == source || HorizontalDistanceSquared(origin, bomb.transform.position) > radiusSquared) continue;
                Vector3 velocityChange = HorizontalDirection(origin, bomb.transform.position) * knockbackSpeed;
                bomb.ApplyKnockback(velocityChange, duration);
            }
        }

        /// <summary>폭발 지점과 대상 위치를 받아 XZ 평면의 바깥 방향을 반환한다.</summary>
        private static Vector3 HorizontalDirection(Vector3 origin, Vector3 target)
        {
            Vector3 direction = target - origin;
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.right : direction.normalized;
        }

        /// <summary>두 위치를 받아 높이를 제외한 수평 거리의 제곱을 반환한다.</summary>
        private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
        {
            Vector3 difference = first - second;
            difference.y = 0f;
            return difference.sqrMagnitude;
        }

        /// <summary>강 경계를 벗어난 폭탄을 점수·자원 정산 없이 제거한다.</summary>
        public void ResolveBombExit(BombExplosion3D bomb)
        {
            if (!_bombs.Remove(bomb)) return;
            bomb.ReturnNow();
        }

        /// <summary>풀에 반환된 폭탄을 진행 중 장애물 목록에서 제거한다.</summary>
        public void ForgetBomb(BombExplosion3D bomb)
        {
            _bombs.Remove(bomb);
        }

        /// <summary>풀에 반환된 기물을 받아 생성 목록·운반·카메라 추적에서 제거한다.</summary>
        public void ForgetItem(RecyclingItem item)
        {
            _items.Remove(item);
            _held.Remove(item);
            if (_tracked == item) _tracked = null;
        }

        /// <summary>하루 종료 또는 재시도에서 모든 생성 기물과 운반 상태를 풀에 반환한다.</summary>
        private void ClearWorld()
        {
            _held.Clear();
            _tracked = null;
            foreach (RecyclingItem item in _items.ToArray()) if (item != null) item.ReturnNow();
            _items.Clear();
            foreach (BombExplosion3D bomb in _bombs.ToArray()) bomb.ReturnNow();
            _bombs.Clear();
        }

        /// <summary>오늘 시작 시점의 재화와 업그레이드를 저장하고 출근 준비 화면으로 전환한다.</summary>
        private void PrepareDay()
        {
            ClearWorld();
            _dayStartWood = _wood;
            Array.Copy(_levels, _startLevels, _levels.Length);
            _dayScore = _correct = _wrong = _lost = _bestBundle = _dayWood = 0;
            _beaverStunnedUntil = 0f;
            _beaver.ResetPose();
            _phase = DayPhase.Preparation;
            _movement = Vector2.zero;
            ResetCameraTarget();
            SetUiMode(true);
            UpdateModal();
            RefreshHud();
        }

        /// <summary>출근 입력에서 공급 타이머를 초기화하고 작업을 시작한다.</summary>
        private void StartDay()
        {
            _elapsed = 0f;
            _nextSpawn = 0.5f;
            _phase = DayPhase.Supply;
            _cameraGesture = _cameraDragged = false;
            SetUiMode(false);
            _hud.HideModal();
        }

        /// <summary>마감 때 남은 기물과 무실수 보너스를 정산해 목표 통과·초과 점수 목재·최종 클리어를 확정한다.</summary>
        private void FinishDay()
        {
            ReleaseBundle(false);
            foreach (RecyclingItem item in _items.ToArray())
                if (!item.IsResolved) ResolveExit(item, false, false);
            ClearWorld();
            if (_lost == 0)
            {
                _dayScore += NO_TRASH_LOSS_SCORE_BONUS;
                _wood += NO_TRASH_LOSS_WOOD_BONUS;
                _dayWood += NO_TRASH_LOSS_WOOD_BONUS;
            }
            if (_wrong == 0)
            {
                _dayScore += NO_MISCLASSIFICATION_SCORE_BONUS;
                _wood += NO_MISCLASSIFICATION_WOOD_BONUS;
                _dayWood += NO_MISCLASSIFICATION_WOOD_BONUS;
            }
            _passed = _dayScore >= _targets[_day - 1];
            if (_passed)
            {
                int surplusWood = (_dayScore - _targets[_day - 1]) / SURPLUS_SCORE_PER_BONUS_WOOD;
                _wood += surplusWood;
                _dayWood += surplusWood;
                _totalScore += _dayScore;
            }
            _phase = _passed && _day == _targets.Length ? DayPhase.Clear : DayPhase.Result;
            _beaver.SetStunned(false);
            _movement = Vector2.zero;
            SetUiMode(true);
            UpdateModal();
        }

        /// <summary>모달의 현재 단계에 따라 출근·재개·다음 날·오늘 재시도를 실행한다.</summary>
        public void PrimaryAction()
        {
            switch (_phase)
            {
                case DayPhase.Preparation: StartDay(); break;
                case DayPhase.Paused:
                    _phase = _beforePause; SetUiMode(false); _hud.HideModal(); break;
                case DayPhase.Result:
                    if (_passed) { _day++; PrepareDay(); }
                    else
                    {
                        _wood = _dayStartWood;
                        Array.Copy(_startLevels, _levels, _levels.Length);
                        PrepareDay();
                    }
                    break;
                case DayPhase.Clear: RestartRun(); break;
            }
        }

        /// <summary>모달의 처음부터 버튼에서 전체 일차·점수·목재·업그레이드를 초기화한다.</summary>
        public void SecondaryAction() => RestartRun();

        /// <summary>처음부터 진행할 때 원래 재화와 능력치로 돌아가 첫날 준비를 연다.</summary>
        private void RestartRun()
        {
            _wood = _totalScore = 0;
            _day = 1;
            Array.Clear(_levels, 0, _levels.Length);
            PrepareDay();
        }

        /// <summary>작업 중 메뉴를 열거나 닫고 입력 맵과 시간 상태를 같은 단계로 복원한다.</summary>
        public void TogglePause()
        {
            if (_phase == DayPhase.Paused) { PrimaryAction(); return; }
            if (!IsWorking) return;
            _beforePause = _phase;
            _phase = DayPhase.Paused;
            _movement = Vector2.zero;
            SetUiMode(true);
            UpdateModal();
        }

        /// <summary>UI 활성 여부를 받아 InputManager의 맵·시간 상태와 포인터 표시를 설정한다.</summary>
        private void SetUiMode(bool ui)
        {
            Managers.Input.SetInputMode(ui ? InputMode.UI : InputMode.Player);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>선택된 업그레이드 인덱스를 받아 목재가 충분하면 한 단계 구매하고 상점 수치를 갱신한다.</summary>
        public void BuyUpgrade(int index)
        {
            if (_phase != DayPhase.Result || !_passed || index < 0 || index >= _levels.Length || _levels[index] >= 3) return;
            int cost = UPGRADE_COSTS[_levels[index]];
            if (_wood < cost) return;
            _wood -= cost;
            _levels[index]++;
            UpdateModal();
            RefreshHud();
        }

        /// <summary>현재 진행 상태와 통계를 사용하여 준비·결과·상점·일시정지 패널을 갱신한다.</summary>
        private void UpdateModal()
        {
            if (_phase == DayPhase.Preparation)
                _hud.ShowModal(_day + "일차 · 출근 준비", "오늘의 목표 " + _targets[_day - 1] + "점\n" +
                    RecyclingRules.DayCategories(_day) + "\n\nWASD 이동 · E 집기/놓기\n투입구에서 놓으면 분류 · Q 묶음 분리\n\n나무 폭포 통과 +3점·목재\n쓰레기 유실 0회 +10점·+3목재\n오분류 0회 +10점·+3목재\n목표 초과 10점당 +1목재", "출근", "", false);
            else if (_phase == DayPhase.Paused)
                _hud.ShowModal("잠깐 쉬는 중", "올바른 투입구 +3점 · 오분류 -4점\n놓친 쓰레기 -2점\n쓰레기 유실 0회 +10점 · +3 목재\n오분류 0회 +10점 · +3 목재\n목표 초과 10점당 +1 목재\n\n같은 재질 2/4/6개부터 묶음 보너스\nQ로 혼합 묶음을 풀 수 있어요", "계속", "처음부터", false);
            else
            {
                string body = "오늘 " + _dayScore + " / " + _targets[_day - 1] + "점\n" +
                    "정상 " + _correct + " · 오분류 " + _wrong + " · 놓침 " + _lost +
                    "\n최대 묶음 " + _bestBundle + "개 · 획득 +" + _dayWood + " 목재";
                if (_passed)
                {
                    int surplusWood = (_dayScore - _targets[_day - 1]) / SURPLUS_SCORE_PER_BONUS_WOOD;
                    if (surplusWood > 0) body += "\n목표 초과 보상 +" + surplusWood + " 목재";
                }
                if (_lost == 0)
                    body += "\n유실 없음 보너스 +" + NO_TRASH_LOSS_SCORE_BONUS + "점 · +" + NO_TRASH_LOSS_WOOD_BONUS + " 목재";
                if (_wrong == 0)
                    body += "\n오분류 없음 보너스 +" + NO_MISCLASSIFICATION_SCORE_BONUS + "점 · +" + NO_MISCLASSIFICATION_WOOD_BONUS + " 목재";
                if (_phase == DayPhase.Clear)
                    _hud.ShowModal("5일 근무 완료!", body + "\n\n누적 " + _totalScore + "점 · 목재 " + _wood,
                        "처음부터", "", false);
                else if (!_passed)
                    _hud.ShowModal("오늘의 목표 미달", body + "\n\n: 출근 전 목재와 능력 복원", "재도전", "처음부터", false);
                else
                {
                    _hud.ShowModal("퇴근 · 내일을 준비하세요", body + "\n보유 " + _wood + " 목재", "다음 날", "처음부터", true);
                    string[] names = { "튼튼한 앞발", "든든한 식사", "긴 집게" };
                    string[] statNames = { "운반 한도", "이동 속도", "집기 거리" };
                    for (int i = 0; i < _levels.Length; i++)
                    {
                        int level = _levels[i];
                        string current = i == 0 ? Capacity + "개" : i == 1
                            ? (_baseSpeed + level * 0.5f).ToString("0.0") : Reach.ToString("0.0");
                        string next = i == 0 ? (Capacity + 2) + "개" : i == 1
                            ? (_baseSpeed + (level + 1) * 0.5f).ToString("0.0") : (Reach + 0.3f).ToString("0.0");
                        string value = level == 3 ? current : current + " → " + next;
                        string description = names[i] + " " + level + "/3\n" + statNames[i] + " " + value + "\n" +
                            (level == 3 ? "최대 단계" : UPGRADE_COSTS[level] + " 목재");
                        _hud.SetUpgrade(i, description, level < 3 && _wood >= UPGRADE_COSTS[Mathf.Min(level, 2)]);
                    }
                }
            }
        }

        /// <summary>현재 점수와 운반 묶음의 재질 개수를 짧은 HUD 문구로 전달한다.</summary>
        private void RefreshHud()
        {
            string carried = "빈손 · 같은 재질을 모아 투입구에 제출하세요";
            if (_held.Count > 0)
                carried = "운반 " + _held.Count + "/" + Capacity + "  ·  " + string.Join("  ",
                    _held.GroupBy(p => p.Definition.Category).Select(g => RecyclingRules.Name(g.Key) + " " + g.Count()));
            string phase = _phase == DayPhase.Supply ? "공급" : _phase == DayPhase.Closing ? "마감 정리" : "준비 / 정산";
            float remaining = _phase == DayPhase.Supply ? _supplySeconds - _elapsed : _supplySeconds + _closingSeconds - _elapsed;
            _hud.Refresh(_day, phase, remaining, _dayScore, _targets[_day - 1], _wood, carried, IsWorking);
        }

        /// <summary>현재 제출 대상과 동일한 계산 결과를 월드 투입구 표지에 반영한다.</summary>
        private void RefreshChutes()
        {
            RecyclingChute3D selected = IsWorking ? FindChute() : null;
            foreach (RecyclingChute3D chute in _chutes)
                chute.Refresh(_day, chute == selected, _held.Count > 0,
                    RecyclingRules.Evaluate(_held, chute.Category), _camera);
        }

        /// <summary>센서 대상·운반·추적 상태를 받아 기물 강조와 분류 안내를 표시한다.</summary>
        private void RefreshHover()
        {
            foreach (RecyclingItem item in _items)
            {
                if (item.IsResolved) continue;
                uint outline = item.IsHeld ? 2u : 0u;
                if (_held.Count > 0 && !item.IsHeld && item.Definition.Category == _held[0].Definition.Category &&
                    (item.Body.position - _beaver.CarryPoint.position).sqrMagnitude < 9f) outline = 4u;
                item.SetOutline(outline);
            }
            RecyclingItem pickupTarget = _held.Count == 0 ? FindPickupTarget() : null;
            if (pickupTarget != null) pickupTarget.SetOutline(2u);
            RecyclingItem hovered = _tracked != null && !_tracked.IsResolved ? _tracked : pickupTarget;
            if (hovered == null) { _hud.Hint("", Vector2.zero); return; }
            bool mixed = _held.Count > 0 && hovered.Definition.Category != _held[0].Definition.Category;
            if (hovered != pickupTarget) hovered.SetOutline(mixed ? 8u : 2u);
            string hint = hovered.Definition.Name + " · " + RecyclingRules.Name(hovered.Definition.Category) +
                (hovered.Definition.IsWood ? "\n폭포로 보내 목재를 얻으세요" : mixed ? "\n다른 재질이 섞여요" : "");
            Vector2 screen = _camera.WorldToScreenPoint(hovered.transform.position);
            _hud.Hint(hint, screen);
        }

        /// <summary>
        /// Resets camera tracking to follow the beaver at the start of a day.
        /// Uses the beaver transform and updates the tracked item and camera target.
        /// </summary>
        private void ResetCameraTarget()
        {
            _tracked = null;
            _followBeaver = true;
            _cameraTarget = new Vector3(_beaver.transform.position.x, 0f, _beaver.transform.position.z + 2f);
        }

        /// <summary>
        /// Casts through the current pointer and returns the registered recycling item under it.
        /// Uses the camera and pointer position, and returns the hit item or null.
        /// </summary>
        private RecyclingItem PickAtCursor()
        {
            if (!Managers.Input.HasPointer) return null;
            Ray ray = _camera.ScreenPointToRay(Managers.Input.PointerPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
            RecyclingItem item = hit.collider.GetComponentInParent<RecyclingItem>();
            return item != null && !item.IsResolved && _items.Contains(item) ? item : null;
        }
        /// <summary>
        /// Reads right-click and pointer input and updates the tracked item.
        /// </summary>
        private void ReadCameraInput()
        {
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Managers.Input.SecondaryPressed)
            {
                _cameraGesture = !overUi;
                _cameraDragged = false;
                _dragDistance = Vector2.zero;
            }
            if (_cameraGesture && (Managers.Input.SecondaryHeld || Managers.Input.SecondaryReleased))
            {
                Vector2 delta = Managers.Input.SecondaryPressed ? Vector2.zero : Managers.Input.PointerDelta;
                if (!_cameraDragged)
                {
                    _dragDistance += delta;
                    _cameraDragged = _dragDistance.sqrMagnitude >= 36f;
                }
            }
            if (Managers.Input.SecondaryReleased)
            {
                if (_cameraGesture && !_cameraDragged && !overUi)
                {
                    _tracked = PickAtCursor();
                    _followBeaver = _tracked == null;
                }
                _cameraGesture = false;
            }
        }
    }
}
