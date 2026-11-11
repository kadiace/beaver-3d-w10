using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.EventSystems;

namespace BeaverRecycling
{
    public sealed class RecyclingGame3D : MonoBehaviour
    {
        private enum DayPhase { Preparation, Supply, Closing, Result, Paused, Clear }
        private static readonly int[] UPGRADE_COSTS = { 2, 4, 7 };

        [Header("Scene objects")]
        [SerializeField] private RecyclingCatalog _catalog;
        [SerializeField] private RecyclingBeaver3D _beaver;
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
        [SerializeField] private int _runSeed = 41357;

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

        [Header("Camera")]
        [SerializeField, Min(0.01f)] private float _zoomSensitivity = 0.12f;
        [SerializeField] private Vector2 _zoomRange = new Vector2(8f, 16f);
        private readonly HashSet<RecyclingItem> _items = new HashSet<RecyclingItem>();
        private readonly List<RecyclingItem> _held = new List<RecyclingItem>();
        private readonly int[] _levels = new int[3];
        private readonly int[] _startLevels = new int[3];
        private Collider[] _overlapBuffer = new Collider[64];
        private System.Random _random;
        private RecyclingItem _tracked;
        private DayPhase _phase;
        private DayPhase _beforePause;
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

        public bool IsWorking => _phase == DayPhase.Supply || _phase == DayPhase.Closing;
        public float WaterfallZ => _waterfallZ;
        public float FallbackY => _fallbackY;
        public float WaterY => _waterY;
        public float FlowSpeed => _flowSpeeds[_day - 1];
        private int Capacity => _baseCapacity + 2 * _levels[0];
        private float Reach => _baseReach + 0.3f * _levels[2];

        void Start()
        {
            Managers.Input.UseRecyclingControls(true);
            _hud.Initialize(this);
            _camera.orthographic = true;
            _camera.orthographicSize = 13.5f;
            _camera.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
            CenterCamera();
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
                _movement = Managers.Input.MoveInput;
                if (_movement.sqrMagnitude > 0.001f)
                {
                    _tracked = null;
                    _followBeaver = true;
                }
                bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (Managers.Input.PrimaryPressed && !pointerOverUi)
                {
                    if (_held.Count > 0) ReleaseBundle(false);
                    else PickUp();
                }
                if (Managers.Input.SplitBundlePressed && _held.Count > 0) ReleaseBundle(true);
                RecyclingChute3D chute = FindChute();
                if (Managers.Input.SubmitPressed && chute != null && _held.Count > 0) Submit(chute);
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
                if (_elapsed >= _supplySeconds + _closingSeconds) FinishDay();
                if (IsWorking) RefreshHover(pointerOverUi);
            }
            RefreshHud();
            RefreshChutes();
        }

        void FixedUpdate()
        {
            if (!IsWorking) return;
            float slowdown = Mathf.Max(0.6f, 1f - 0.05f * Mathf.Max(0, _held.Count - 3));
            _beaver.Move(_movement, (_baseSpeed + 0.5f * _levels[1]) * slowdown, Time.fixedDeltaTime);
            if (_held.Count == 0) return;
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
            _camera.transform.position = _cameraTarget - _camera.transform.forward * 28f;
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        /// <summary>기물 위치를 받아 하천의 수평 영역 안에 있는지 반환한다.</summary>
        public bool IsInsideRiver(Vector3 position) => position.x >= _riverMinimum.x && position.x <= _riverMaximum.x &&
            position.z >= _riverMinimum.y && position.z <= _riverMaximum.y;

        /// <summary>활성 정의를 난수로 골라 풀에서 생성하고 새 기물의 상태를 초기화한다.</summary>
        private void SpawnItem()
        {
            RecyclingDefinition definition = _catalog.Draw(_random, _day);
            Vector3 position = _spawnPoint.position;
            position.x += ((float)_random.NextDouble() * 2f - 1f) * _spawnHalfWidth;
            Quaternion rotation = Quaternion.Euler(0f, (float)_random.NextDouble() * 360f, 0f);
            GameObject instance = Managers.Resource.Instantiate("Prefabs/Recycling/" + definition.Prefab.name,
                position: position, rotation: rotation);
            RecyclingItem item = instance.GetComponent<RecyclingItem>();
            instance.name = definition.Name;
            item.Initialize(this, definition);
            _items.Add(item);
        }

        /// <summary>커서의 물리 레이를 받아 활성 분리수거 기물 하나를 반환한다.</summary>
        private RecyclingItem PickAtCursor()
        {
            if (!Managers.Input.HasPointer) return null;
            if (!Physics.Raycast(_camera.ScreenPointToRay(Managers.Input.PointerPosition), out RaycastHit hit,
                200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
            RecyclingItem item = hit.collider.GetComponentInParent<RecyclingItem>();
            return item != null && !item.IsResolved && _items.Contains(item) ? item : null;
        }

        /// <summary>커서 아래 묶음의 거리와 크기를 검사하고 전체를 비버의 운반 상태로 바꾼다.</summary>
        private void PickUp()
        {
            RecyclingItem item = PickAtCursor();
            if (item == null) return;
            float distance = Vector3.Distance(item.Collider.ClosestPoint(_beaver.CarryPoint.position), _beaver.CarryPoint.position);
            if (distance > Reach) { _hud.Message("가까이 이동해서 집으세요"); return; }
            List<RecyclingItem> group = RecyclingItem.Collect(item);
            if (group.Count > Capacity) { _hud.Message("운반 한도 " + Capacity + "개 · 현재 묶음 " + group.Count + "개"); return; }
            _tracked = null;
            _held.Add(item);
            foreach (RecyclingItem member in group) if (member != item) _held.Add(member);
            foreach (RecyclingItem member in _held) member.SetHeld(true);
        }

        /// <summary>운반 중 기물의 충돌 범위를 조회해 접촉한 상대 묶음을 한도 안에서 합친다.</summary>
        private void CollectContacts()
        {
            for (int i = 0; i < _held.Count; i++)
            {
                RecyclingItem held = _held[i];
                Bounds bounds = held.Collider.bounds;
                int count;
                while ((count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * 0.04f,
                    _overlapBuffer, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == _overlapBuffer.Length)
                    Array.Resize(ref _overlapBuffer, _overlapBuffer.Length * 2);
                for (int j = 0; j < count; j++)
                {
                    Collider collider = _overlapBuffer[j];
                    RecyclingItem other = collider.GetComponentInParent<RecyclingItem>();
                    if (other == null || other.IsHeld || !other.CanJoin || !_items.Contains(other)) continue;
                    if (!Physics.ComputePenetration(held.Collider, held.transform.position, held.transform.rotation,
                        other.Collider, other.transform.position, other.transform.rotation, out _, out _)) continue;
                    List<RecyclingItem> incoming = RecyclingItem.Collect(other);
                    if (incoming.Any(p => p.IsHeld || !p.CanJoin)) continue;
                    if (_held.Count + incoming.Count > Capacity)
                    {
                        if (Time.time > _capacityNoticeAt) { _hud.Message("운반 한도가 가득 찼어요"); _capacityNoticeAt = Time.time + 1.5f; }
                        continue;
                    }
                    held.Attach(other, other.Collider.ClosestPoint(held.Body.position));
                    foreach (RecyclingItem member in incoming) { member.SetHeld(true); _held.Add(member); }
                }
            }
        }

        /// <summary>분리 여부를 받아 운반 묶음을 내려놓고 필요하면 모든 결합과 재접착을 잠시 해제한다.</summary>
        private void ReleaseBundle(bool split)
        {
            foreach (RecyclingItem item in _held)
            {
                item.SetHeld(false);
                if (split) { item.DetachAll(); item.BlockAttachment(0.5f); }
            }
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

        /// <summary>선택된 투입구와 운반 묶음을 확정하고 같은 규칙의 점수·통계·제출 연출을 적용한다.</summary>
        private void Submit(RecyclingChute3D chute)
        {
            RecyclingSubmission submission = RecyclingRules.Evaluate(_held, chute.Category);
            var submitted = new List<RecyclingItem>(_held);
            _held.Clear();
            _dayScore += submission.Score;
            _correct += submission.Correct;
            _wrong += submission.Wrong;
            if (submission.Wrong == 0) _bestBundle = Mathf.Max(_bestBundle, submitted.Count);
            foreach (RecyclingItem item in submitted) item.Resolve(chute.IntakePosition, true);
            _hud.Event((submission.Score >= 0 ? "+" : "") + submission.Score + "점" +
                (submission.Bonus > 0 ? " · 묶음 +" + submission.Bonus : ""), chute.IntakePosition,
                _camera, submission.Score >= 0 ? new Color(0.3f, 1f, 0.8f) : new Color(1f, 0.4f, 0.3f));
        }

        /// <summary>이탈한 기물과 폭포 여부를 받아 한 번만 감점 또는 회수한 목재를 지급하고 제거한다.</summary>
        public void ResolveExit(RecyclingItem item, bool waterfall, bool animate)
        {
            if (item.IsResolved) return;
            Vector3 position = item.transform.position;
            if (item.Definition.IsWood)
            {
                if (waterfall && item.WasCollected)
                {
                    _wood += item.Definition.WoodYield;
                    _dayWood += item.Definition.WoodYield;
                    if (animate) _hud.Event("+" + item.Definition.WoodYield + " 목재", position, _camera,
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
        }

        /// <summary>오늘 시작 시점의 재화와 업그레이드를 저장하고 출근 준비 화면으로 전환한다.</summary>
        private void PrepareDay()
        {
            ClearWorld();
            _dayStartWood = _wood;
            Array.Copy(_levels, _startLevels, _levels.Length);
            _dayScore = _correct = _wrong = _lost = _bestBundle = _dayWood = 0;
            _beaver.ResetPose();
            _phase = DayPhase.Preparation;
            _movement = Vector2.zero;
            CenterCamera();
            SetUiMode(true);
            UpdateModal();
            RefreshHud();
        }

        /// <summary>출근 입력에서 일차별 동일 난수 시드와 공급 타이머를 초기화하고 작업을 시작한다.</summary>
        private void StartDay()
        {
            _random = new System.Random(_runSeed + _day * 7919);
            _elapsed = 0f;
            _nextSpawn = 0.5f;
            _phase = DayPhase.Supply;
            _cameraGesture = _cameraDragged = false;
            SetUiMode(false);
            _hud.HideModal();
        }

        /// <summary>마감 때 남은 기물의 감점을 합산하고 목표 통과·실패·최종 클리어를 확정한다.</summary>
        private void FinishDay()
        {
            ReleaseBundle(false);
            foreach (RecyclingItem item in _items.ToArray())
                if (!item.IsResolved) ResolveExit(item, false, false);
            ClearWorld();
            _passed = _dayScore >= _targets[_day - 1];
            if (_passed) _totalScore += _dayScore;
            _phase = _passed && _day == _targets.Length ? DayPhase.Clear : DayPhase.Result;
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
                    RecyclingRules.DayCategories(_day) + "\n\nWASD 이동 · 좌클릭 집기/놓기\nE 제출 · Q 묶음 분리\n\n회수한 목재는 폭포로 보내세요", "출근", "", false);
            else if (_phase == DayPhase.Paused)
                _hud.ShowModal("잠깐 쉬는 중", "올바른 투입구 +3점 · 오분류 -4점\n놓친 쓰레기 -2점\n\n같은 재질 2/4/6개부터 묶음 보너스\nQ로 혼합 묶음을 풀 수 있어요", "계속", "처음부터", false);
            else
            {
                string body = "오늘 " + _dayScore + " / " + _targets[_day - 1] + "점\n" +
                    "정상 " + _correct + " · 오분류 " + _wrong + " · 놓침 " + _lost +
                    "\n최대 묶음 " + _bestBundle + "개 · 회수 +" + _dayWood + " 목재";
                if (_phase == DayPhase.Clear)
                    _hud.ShowModal("5일 근무 완료!", body + "\n\n누적 " + _totalScore + "점 · 목재 " + _wood,
                        "처음부터", "", false);
                else if (!_passed)
                    _hud.ShowModal("오늘의 목표 미달", body + "\n\n오늘 다시: 출근 전 목재와 능력 복원", "오늘 다시", "처음부터", false);
                else
                {
                    _hud.ShowModal("퇴근 · 내일을 준비하세요", body + "\n보유 " + _wood + " 목재", "다음 날", "처음부터", true);
                    string[] names = { "튼튼한 앞발", "든든한 식사", "긴 집게" };
                    for (int i = 0; i < _levels.Length; i++)
                    {
                        int level = _levels[i];
                        string value = i == 0 ? (Capacity + " → " + (Capacity + 2) + "개") : i == 1
                            ? ((_baseSpeed + level * 0.5f).ToString("0.0") + " → " + (_baseSpeed + (level + 1) * 0.5f).ToString("0.0"))
                            : (Reach.ToString("0.0") + " → " + (Reach + 0.3f).ToString("0.0"));
                        string description = names[i] + " " + level + "/3\n" + (level == 3 ? "최대 단계" : value + "\n" + UPGRADE_COSTS[level] + " 목재");
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

        /// <summary>포인터·운반·추적 상태를 받아 기물 강조와 분류 안내를 표시한다.</summary>
        private void RefreshHover(bool pointerOverUi)
        {
            foreach (RecyclingItem item in _items)
            {
                if (item.IsResolved) continue;
                uint outline = item.IsHeld ? 2u : 0u;
                if (_held.Count > 0 && !item.IsHeld && item.Definition.Category == _held[0].Definition.Category &&
                    (item.Body.position - _beaver.CarryPoint.position).sqrMagnitude < 9f) outline = 4u;
                item.SetOutline(outline);
            }
            RecyclingItem hovered = _tracked != null && !_tracked.IsResolved ? _tracked : pointerOverUi ? null : PickAtCursor();
            if (hovered == null) { _hud.Hint("", Vector2.zero); return; }
            bool mixed = _held.Count > 0 && hovered.Definition.Category != _held[0].Definition.Category;
            hovered.SetOutline(mixed ? 8u : 2u);
            string hint = hovered.Definition.Name + " · " + RecyclingRules.Name(hovered.Definition.Category) +
                (hovered.Definition.IsWood && hovered.WasCollected ? "\n회수됨 · 폭포로 보내세요" : mixed ? "\n다른 재질이 섞여요" : "");
            Vector2 screen = _tracked == hovered ? (Vector2)_camera.WorldToScreenPoint(hovered.transform.position)
                : Managers.Input.PointerPosition;
            _hud.Hint(hint, screen);
        }

        /// <summary>비버 보기 입력에서 기물 추적과 수동 이동을 끝내고 비버를 카메라 중심으로 둔다.</summary>
        public void CenterCamera()
        {
            _tracked = null;
            _followBeaver = true;
            _cameraTarget = new Vector3(_beaver.transform.position.x, 0f, _beaver.transform.position.z + 2f);
        }

        /// <summary>공용 액션의 우클릭과 휠을 받아 카메라 평행 이동·기물 추적·확대를 갱신한다.</summary>
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
                    if (_dragDistance.sqrMagnitude >= 36f) { _cameraDragged = true; delta = _dragDistance; }
                    else delta = Vector2.zero;
                }
                if (_cameraDragged)
                {
                    _tracked = null;
                    _followBeaver = false;
                    Plane plane = new Plane(Vector3.up, Vector3.zero);
                    Vector2 center = _camera.pixelRect.center;
                    Ray a = _camera.ScreenPointToRay(center);
                    Ray b = _camera.ScreenPointToRay(center + delta);
                    if (plane.Raycast(a, out float da) && plane.Raycast(b, out float db))
                        _cameraTarget += a.GetPoint(da) - b.GetPoint(db);
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
            if (!overUi && !Managers.Input.SecondaryHeld)
                _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize *
                    Mathf.Exp(-Managers.Input.CameraZoomSteps * _zoomSensitivity), _zoomRange.x, _zoomRange.y);
        }
    }
}
