using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;

namespace RestoreCastle3D
{
    public sealed class CastleGame3D : MonoBehaviour
    {
        private const int ROUNDS_PER_STAGE = 5;
        private const int PLACEMENTS_PER_ROUND = 5;
        private const int STAGE_COUNT = 5;
        private const int LIVING_TREE_MAX_GROWTH = 8;
        private const float BARREL_EXPLOSION_RADIUS = 2.5f;
        private const float WASTE_EFFECT_RADIUS = 2.5f;
        private const float CAMERA_EDGE_MARGIN = 3f;
        private const float CAMERA_MOVEMENT_RANGE_MULTIPLIER = 1.2f;
        private const float CAMERA_MOVEMENT_REFERENCE_SIZE = 7f;

        private static readonly float _livingTreeScalePerGrowth =
            1.05f * Mathf.Pow(2f, 1f / LIVING_TREE_MAX_GROWTH);

        [Header("Barrel explosion")]
        [SerializeField, Min(0f)] private float barrelExplosionImpulse = 18f;
        [SerializeField, Min(0f)] private float barrelExplosionUpwardsModifier = 0.6f;

        [Header("3D drop")]
        [SerializeField, Min(2f)] private float dropHeight = 7f;
        [SerializeField, Range(0.05f, 0.5f)] private float dropClickCooldown = 0.1f;
        [SerializeField] private float groundY = -2.25f;

        [Header("Flood stage transition")]
        [SerializeField, Min(0.1f)] private float floodFillDuration = 2.25f;
        [SerializeField, Min(0.05f)] private float floodDrainDuration = 0.28f;
        [SerializeField]
        private AnimationCurve floodFillCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private readonly List<CastlePiece3D> _pieces = new List<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _pieceSet = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _outlinedNeighbors = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _penaltyTargets = new HashSet<CastlePiece3D>();
        private readonly Dictionary<CastlePiece3D, int> _score = new Dictionary<CastlePiece3D, int>();
        private readonly Dictionary<(CastlePiece3D, CastlePiece3D), int> _contributions =
            new Dictionary<(CastlePiece3D, CastlePiece3D), int>();
        private readonly HashSet<string> _consumedContacts = new HashSet<string>();
        private readonly CastleMaterial3D[] _offers = new CastleMaterial3D[3];
        private readonly CastleFloodQuery3D _floodQuery = new CastleFloodQuery3D();
        private CastleCatalog3D _catalog;
        private CastleHud3D _hud;
        private CastleDropPreview3D _dropPreview;
        private CastleFloodEffect3D _floodEffect;
        private GameSceneContext _sceneContext;
        private CastleFloodPreview3D _floodPreview;
        private Bounds _placementBounds;
        private CastleFloatingText3D _floatingText;
        private CastlePiece3D _effectSource;
        private Coroutine _stageFloodRoutine;
        private Camera _camera;
        private CastlePiece3D _outlinedPiece;
        private System.Random _random;
        private Vector3 _cameraTarget;
        private int _selected = -1;
        private int _turn;
        private int _placementsInRound;
        private int _pendingDrops;
        private float _lastDropTime = float.NegativeInfinity;
        private bool _dropMouseReleased = true;
        private int _stage;
        private int _rerolls;
        private int _extraRerolls;
        private float _objectYaw;
        private float _nextHoverScoreRefresh;
        private bool _rulesOpen;
        private bool _paletteOpen = true;
        private bool _stageTransitioning;
        private bool _resolving;
        private bool _awaitingStage;
        private bool _finished;
        private bool _stagePassed;
        private string _status;

        private void Start()
        {
            _sceneContext = GetComponent<GameSceneContext>();
            _catalog = new CastleCatalog3D();
            ConfigureScene();
            CreateHud();
            _dropPreview = new CastleDropPreview3D();
            _floodEffect = new CastleFloodEffect3D();
            _floodPreview = _camera.GetComponent<CastleFloodPreview3D>();
            _floodPreview.Initialize(this, _floodEffect);
            ResetGame();
        }

        private void Update()
        {
            if (_catalog == null) return;
            Mouse mouse = Mouse.current;
            // Track releases even while the fifth drop is finishing or a popup is open.
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
                _dropMouseReleased = true;

            UpdateCameraInput();
            if (CanPreviewFlood && Managers.Input.FloodPreviewPressed)
                _floodPreview.Toggle();
            if (!_stageTransitioning)
            {
                ApplyContactDestruction();
                UpdateHoveredPiece();
            }
            if (_resolving || _awaitingStage || _finished || _rulesOpen)
            {
                HideDropPreview();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    OpenCandidatePalette();
                if (keyboard.qKey.wasPressedThisFrame)
                    _objectYaw -= 1f;
                else if (keyboard.qKey.isPressed)
                    _objectYaw -= RotationSpeed * Time.deltaTime;

                if (keyboard.eKey.wasPressedThisFrame)
                    _objectYaw += 1f;
                else if (keyboard.eKey.isPressed)
                    _objectYaw += RotationSpeed * Time.deltaTime;
            }

            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                OpenCandidatePalette();

            UpdateDropPreview();

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame ||
                !_dropMouseReleased || _selected < 0 ||
                _placementsInRound >= PLACEMENTS_PER_ROUND ||
                Time.unscaledTime - _lastDropTime < dropClickCooldown)
                return;

            if (!TryGetPlacementXZ(mouse.position.ReadValue(), out Vector3 placement))
                return;

            // An actual placement requires a new mouse-down after release and
            // at least 0.1 seconds since the previous accepted placement.
            _dropMouseReleased = false;
            _lastDropTime = Time.unscaledTime;
            _paletteOpen = false;
            HideDropPreview();
            StartCoroutine(DropAndResolve(_offers[_selected],
                new Vector3(placement.x, dropHeight, placement.z)));
        }

        /// <summary>Esc 또는 우클릭으로 현재 라운드의 고정 후보를 다시 연다.</summary>
        private void OpenCandidatePalette()
        {
            if (_resolving || _awaitingStage || _finished || _rulesOpen) return;
            _paletteOpen = true;
            _selected = -1;
            HideDropPreview();
            if (_placementsInRound > 0)
                _status = "후보를 다시 선택하세요. 남은 배치 "
                    + (PLACEMENTS_PER_ROUND - _placementsInRound) + "회 (리롤 불가)";
        }

        /// <summary>화면 좌표의 XZ가 배치 영역 안에 있으면 실제 배치 위치를 반환한다.</summary>
        private bool TryGetPlacementXZ(Vector2 screen, out Vector3 position)
        {
            if (!TryGetCursorXZ(screen, out position)) return false;
            return CanPlaceAt(position);
        }

        /// <summary>UI 밖 화면 좌표를 배치 가능 여부와 관계없이 월드 XZ로 변환한다.</summary>
        private bool TryGetCursorXZ(Vector2 screen, out Vector3 position)
        {
            position = default;
            if (screen.x < 0f || screen.y < 0f ||
                screen.x >= Screen.width || screen.y >= Screen.height ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                return false;

            Ray viewRay = _camera.ScreenPointToRay(screen);
            Plane referencePlane = new Plane(Vector3.up, Vector3.zero);
            if (!referencePlane.Raycast(viewRay, out float distance)) return false;
            position = viewRay.GetPoint(distance);
            return true;
        }

        /// <summary>월드 XZ가 바닥 범위와 원형 배치 영역에 모두 포함되는지 확인한다.</summary>
        private bool CanPlaceAt(Vector3 position)
        {
            if (!_placementBounds.Contains(new Vector3(position.x, groundY, position.z)))
                return false;

            Vector2 center = new Vector2(_placementBounds.center.x, _placementBounds.center.z)
                + PlacementCenterOffset;
            Vector2 offset = new Vector2(position.x, position.z) - center;
            return offset.sqrMagnitude <= PlacementRadius * PlacementRadius;
        }

        /// <summary>선택한 자재의 실제 프리팹으로 -Y 방향 Raycast 낙하 예상 위치를 표시한다.</summary>
        private void UpdateDropPreview()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _selected < 0 || mouse.rightButton.isPressed ||
                _offers[_selected] == null ||
                !TryGetCursorXZ(mouse.position.ReadValue(), out Vector3 placement))
            {
                HideDropPreview();
                return;
            }

            bool canPlace = CanPlaceAt(placement);
            _dropPreview.Show(_offers[_selected], placement,
                Quaternion.Euler(0f, _objectYaw, 0f), dropHeight,
                canPlace, _placementBounds.max.y);

            UpdatePenaltyTargets(_offers[_selected].id);

            if (_dropPreview.IsVisible)
            {
                Vector3 launchTarget = _camera.WorldToScreenPoint(
                    new Vector3(placement.x, dropHeight, placement.z));
                Vector3 screenTarget = _camera.WorldToScreenPoint(_dropPreview.LandingPosition);
                if (launchTarget.z > 0f && screenTarget.z > 0f)
                    _hud.ShowDropGuide(new Vector2(launchTarget.x, launchTarget.y),
                        new Vector2(screenTarget.x, screenTarget.y), canPlace);
                else
                    _hud.HideDropGuide();
            }
            else
                _hud.HideDropGuide();
        }

        /// <summary>배치 중단·UI 가림·라운드 종료 시 고스트와 화면 점선을 함께 숨긴다.</summary>
        private void HideDropPreview()
        {
            _dropPreview?.Hide();
            ClearPenaltyTargets();
            _hud?.HideDropGuide();
        }

        /// <summary>현재 가이드 중심에서 실제 폐기물 능력의 적용 대상만 빨간 외곽선으로 표시한다.</summary>
        private void UpdatePenaltyTargets(string materialId)
        {
            if (!_dropPreview.IsVisible ||
                (materialId != "foul_smelling_lump" && materialId != "waste_bag_small"))
            {
                ClearPenaltyTargets();
                return;
            }

            foreach (CastlePiece3D piece in _penaltyTargets)
                if (piece != null) piece.SetPenaltyOutline(false);
            _penaltyTargets.Clear();

            Vector3 center = _dropPreview.LandingPosition;
            foreach (CastlePiece3D piece in _pieces)
            {
                if (piece == null || !_pieceSet.Contains(piece) ||
                    _catalog.Get(piece.MaterialId).category == "쓰레기" ||
                    (materialId == "foul_smelling_lump" && piece.MaterialId == "foundation") ||
                    !IsInWasteEffectRange(piece, center))
                    continue;
                piece.SetPenaltyOutline(true);
                _penaltyTargets.Add(piece);
            }
        }

        /// <summary>가이드가 사라질 때 이전 디메리트 대상의 빨간 외곽선을 지운다.</summary>
        private void ClearPenaltyTargets()
        {
            foreach (CastlePiece3D piece in _penaltyTargets)
                if (piece != null) piece.SetPenaltyOutline(false);
            _penaltyTargets.Clear();
        }

        private void OnDestroy()
        {
            if (_floodPreview != null) _floodPreview.Hide();
            SetOutlineTargets(null);
            _dropPreview?.Dispose();
            _floodEffect?.Dispose();
        }

        /// <summary>3D 물리 충돌체를 마우스 레이로 탐색해 원본 자재 상세 UI에 전달한다.</summary>
        private void UpdateHoveredPiece()
        {
            if (_hud == null) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) { SetOutlineTargets(null); _hud.SetHoveredPiece(null, Vector2.zero); return; }

            Vector2 position = mouse.position.ReadValue();
            if (!CanShowItemInfo || mouse.rightButton.isPressed ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                SetOutlineTargets(null);
                _hud.SetHoveredPiece(null, position);
                return;
            }

            CastlePiece3D piece = null;
            Ray ray = _camera.ScreenPointToRay(position);
            if (Physics.Raycast(ray, out RaycastHit hit, 300f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                CastlePiece3D candidate = hit.collider.GetComponentInParent<CastlePiece3D>();
                if (candidate != null && _pieceSet.Contains(candidate)) piece = candidate;
            }
            if (piece != null && Time.unscaledTime >= _nextHoverScoreRefresh)
            {
                RecalculateScores();
                _nextHoverScoreRefresh = Time.unscaledTime + 0.2f;
            }
            SetOutlineTargets(piece);
            _hud.SetHoveredPiece(piece, position);
        }

        /// <summary>호버 대상은 노란색, 해당 대상과 현재 접촉 중인 기물은 cyan 레이어로 갱신한다.</summary>
        private void SetOutlineTargets(CastlePiece3D hovered)
        {
            if (_outlinedPiece != hovered)
            {
                if (_outlinedPiece != null)
                {
                    _outlinedPiece.SetOutlineLayer(0);
                    Transform previousRange = _outlinedPiece.transform.Find("Penalty Range");
                    if (previousRange != null) previousRange.gameObject.SetActive(false);
                }
                _outlinedPiece = hovered;
            }

            foreach (CastlePiece3D neighbor in _outlinedNeighbors)
                if (neighbor != null) neighbor.SetOutlineLayer(0);
            _outlinedNeighbors.Clear();

            if (_outlinedPiece == null || !_pieceSet.Contains(_outlinedPiece))
            {
                _outlinedPiece = null;
                return;
            }

            foreach (CastlePiece3D neighbor in Adjacent(_outlinedPiece))
            {
                if (neighbor == _outlinedPiece) continue;
                neighbor.SetOutlineLayer(4u);
                _outlinedNeighbors.Add(neighbor);
            }
            _outlinedPiece.SetOutlineLayer(2u);
            Transform range = _outlinedPiece.transform.Find("Penalty Range");
            if (range != null && _outlinedPiece.MaterialId != "mud")
                range.gameObject.SetActive(true);
        }

        /// <summary>ISO 카메라를 설정하고 씬에 배치된 평평한 진흙 냇가의 배치 범위를 읽는다.</summary>
        private void ConfigureScene()
        {
            _camera = Camera.main;
            if (_camera == null) throw new InvalidOperationException("Game scene needs a Main Camera.");
            _camera.orthographic = true;
            _camera.orthographicSize = 10.5f;
            _camera.transform.rotation = Quaternion.Euler(60f, 45f, 0f);
            _cameraTarget = Vector3.zero;
            UpdateCameraPosition();
            _placementBounds = GameObject.Find("Flat Muddy Creek Bed")
                .GetComponent<BoxCollider>().bounds;
        }

        /// <summary>원본 2D 게임의 상단 상태 UI와 하단 자재 카드 UI 프리팹을 연결한다.</summary>
        private void CreateHud()
        {
            // The Game scene has no EventSystem; uGUI buttons and hover handlers need one.
            if (EventSystem.current == null)
            {
                GameObject events = new GameObject("RestoreCastle UI EventSystem");
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>();
            }
            GameObject source = Resources.Load<GameObject>("Prefabs/UIs/RestoreCastleHud");
            if (source == null) throw new InvalidOperationException("Missing original RestoreCastle HUD prefab.");
            GameObject instance = Instantiate(source);
            instance.name = "RestoreCastleHud";
            _hud = instance.AddComponent<CastleHud3D>();
            _hud.Initialize(this);
            _floatingText = instance.AddComponent<CastleFloatingText3D>();
            _floatingText.Initialize(_camera, (RectTransform)instance.transform);
            ChangeCommitted += _floatingText.Show;
        }

        public int StageNumber => Mathf.Min(_stage + 1, STAGE_COUNT);
        public int StageCount => STAGE_COUNT;
        public int RoundsPerStage => ROUNDS_PER_STAGE;
        public int CurrentStageRound => _awaitingStage ? ROUNDS_PER_STAGE : _turn % ROUNDS_PER_STAGE + 1;
        public int PlacementsPerRound => PLACEMENTS_PER_ROUND;
        public float RotationSpeed { get; set; } = 90f;
        public Vector2 PlacementCenterOffset { get; set; }
        public float PlacementRadius { get; set; } = 9f;
        public int PlacementsThisRound => _placementsInRound;
        // The palette hides after each real drop; Esc/right-click reopens it.
        public bool IsRoundPlacing => _resolving || !_paletteOpen;
        public int Durability => _score.Values.Sum();
        public int TargetDurability => StageTarget(Mathf.Min(_stage, STAGE_COUNT - 1));
        public int RerollCount => _rerolls + _extraRerolls;
        public bool CanSelect => !_resolving && !_awaitingStage && !_finished && !_rulesOpen
            && _paletteOpen && _placementsInRound < PLACEMENTS_PER_ROUND;
        public bool CanReroll => CanSelect && _placementsInRound == 0;
        public bool CanShowItemInfo => !_awaitingStage && !_finished && !_rulesOpen;
        internal bool CanPreviewFlood => !_stageTransitioning && !_finished && !_rulesOpen;
        public event Action<CastleChange3D> ChangeCommitted;

        /// <summary>게임 규칙이 확정한 변화를 위치와 원인을 보존한 채 표시 시스템에 전달한다.</summary>
        public void PublishChange(CastleChange3D change) => ChangeCommitted?.Invoke(change);

        /// <summary>실제로 배치되어 살아 있는 기물인지 확인한다.</summary>
        public bool IsPlacedPiece(CastlePiece3D piece) => piece != null && _pieceSet.Contains(piece);

        /// <summary>배치한 기물의 카탈로그 정보와 현재 내구도를 툴팁에 제공한다.</summary>
        public CastleMaterial3D GetPieceMaterial(CastlePiece3D piece) => _catalog.Get(piece.MaterialId);
        public int GetPieceReinforcement(CastlePiece3D piece)
        {
            if (_score.TryGetValue(piece, out int value)) return value;
            CastleMaterial3D material = GetPieceMaterial(piece);
            return material.baseReinforcement + piece.PermanentBonus;
        }

        /// <summary>현재 충돌 상태에 따라 해당 기물에 닿는 기물 목록을 제공한다.</summary>
        public CastlePiece3D[] GetTouchingPieces(CastlePiece3D piece) => Adjacent(piece).ToArray();
        public bool AwaitingStage => _awaitingStage;
        public bool IsFinished => _finished;
        public bool StagePassed => _stagePassed;
        public string StatusMessage => _status;
        public int PieceCount => _pieces.Count;
        public int SelectedOffer => _selected;
        public CastleMaterial3D[] CurrentOffers => _offers;

        /// <summary>화면 카드가 지정한 후보를 다음 낙하 재료로 선택한다.</summary>
        public void SelectOffer(int index)
        {
            if (!CanSelect || index < 0 || index >= _offers.Length) return;
            _selected = index;
            _status = _offers[index].name + " 선택 (남은 배치 "
                + (PLACEMENTS_PER_ROUND - _placementsInRound) + "회)";
        }

        /// <summary>규칙 창이 열려 있는 동안 게임의 배치 입력을 잠근다.</summary>
        public void SetRulesOpen(bool isOpen)
        {
            _rulesOpen = isOpen;
        }

        /// <summary>스테이지 또는 전체 게임 종료 팝업에서 재시작한다.</summary>
        public void Restart()
        {
            ResetGame();
        }

        /// <summary>카메라 회전은 변경하지 않고 XZ 평행이동과 정사영 크기만 변경한다.</summary>
        private void UpdateCameraInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float unit = 2f * _camera.orthographicSize / Mathf.Max(Screen.height, 1);
                Vector3 right = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;
                Vector3 up = Vector3.ProjectOnPlane(_camera.transform.up, Vector3.up).normalized;
                _cameraTarget -= unit * (right * delta.x + up * delta.y);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize *
                    Mathf.Exp(-scroll * 0.03f), 6f, 13.5f);
            ClampCameraTarget();
            UpdateCameraPosition();
        }

        /// <summary>기존 강바닥 이동 범위를 유지하면서 카메라 주시점을 배치 반경 안으로 제한한다.</summary>
        private void ClampCameraTarget()
        {
            float movementSize = Mathf.Min(_camera.orthographicSize, CAMERA_MOVEMENT_REFERENCE_SIZE);
            float horizontalReach = movementSize * _camera.aspect;
            float verticalReach = movementSize / Mathf.Sin(60f * Mathf.Deg2Rad);
            Vector3 right = _camera.transform.right;
            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.up, Vector3.up).normalized;
            float halfVisibleX = Mathf.Abs(right.x) * horizontalReach +
                Mathf.Abs(forward.x) * verticalReach;
            float halfVisibleZ = Mathf.Abs(right.z) * horizontalReach +
                Mathf.Abs(forward.z) * verticalReach;
            float limitX = Mathf.Max(0f, _placementBounds.extents.x +
                CAMERA_EDGE_MARGIN - halfVisibleX) * CAMERA_MOVEMENT_RANGE_MULTIPLIER;
            float limitZ = Mathf.Max(0f, _placementBounds.extents.z -
                CAMERA_EDGE_MARGIN - halfVisibleZ) * CAMERA_MOVEMENT_RANGE_MULTIPLIER;
            _cameraTarget.x = Mathf.Clamp(_cameraTarget.x, -limitX, limitX);
            _cameraTarget.z = Mathf.Clamp(_cameraTarget.z, -limitZ, limitZ);

            Vector2 center = new Vector2(_placementBounds.center.x, _placementBounds.center.z)
                + PlacementCenterOffset;
            Vector2 offset = new Vector2(_cameraTarget.x, _cameraTarget.z) - center;
            Vector2 clamped = center + Vector2.ClampMagnitude(offset, PlacementRadius);
            _cameraTarget.x = clamped.x;
            _cameraTarget.z = clamped.y;
        }

        /// <summary>고정 각도를 유지한 채 평면상의 주시 위치를 변경한다.</summary>
        private void UpdateCameraPosition()
        {
            _camera.transform.position = _cameraTarget - _camera.transform.forward * 20f;
        }

        /// <summary>모든 배치와 소모품 상태를 초기화하고 기본 발판을 배치한다.</summary>
        private void ResetGame()
        {
            _floodPreview.Hide();
            SetOutlineTargets(null);
            if (_stageFloodRoutine != null)
            {
                StopCoroutine(_stageFloodRoutine);
                _stageFloodRoutine = null;
            }
            _floodEffect?.Hide();
            _stageTransitioning = false;

            foreach (CastlePiece3D piece in _pieces)
                if (piece != null) Destroy(piece.gameObject);
            _pieces.Clear();
            _pieceSet.Clear();
            _score.Clear();
            _contributions.Clear();
            _consumedContacts.Clear();
            _random = new System.Random(Environment.TickCount);
            _turn = 0;
            _stage = 0;
            _rerolls = 3;
            _extraRerolls = 0;
            _selected = -1;
            _paletteOpen = true;
            _placementsInRound = 0;
            _pendingDrops = 0;
            _lastDropTime = float.NegativeInfinity;
            _dropMouseReleased = true;
            HideDropPreview();
            _objectYaw = 0f;
            _rulesOpen = false;
            _nextHoverScoreRefresh = 0f;
            _resolving = false;
            _awaitingStage = false;
            _finished = false;
            _cameraTarget = Vector3.zero;
            UpdateCameraPosition();
            DealOffers();
            _status = "재료를 떨어뜨려 집을 쌓으세요. Q/E: 재료 회전";
        }

        /// <summary>Resources/Prefabs에서 3D 기물을 로드하고 Rigidbody를 통한 중력 낙하를 준비한다.</summary>
        private CastlePiece3D Spawn(CastleMaterial3D material, Vector3 position, bool anchored = false)
        {
            GameObject prefab = Resources.Load<GameObject>("Prefabs/" + CastleCatalog3D.PrefabId(material.id));
            if (prefab == null) throw new InvalidOperationException("Missing 3D prefab: " + material.id);
            GameObject instance = Instantiate(prefab, position, Quaternion.Euler(0f, _objectYaw, 0f));
            instance.name = material.name;
            CastlePiece3D piece = instance.GetComponent<CastlePiece3D>();
            piece.Initialize(this, _turn, material.id);
            piece.Body.isKinematic = anchored;
            piece.Body.useGravity = !anchored;
            _pieces.Add(piece);
            _pieceSet.Add(piece);
            _floodPreview.RequestRefresh();
            if (material.id == "vine")
                PublishChange(new CastleChange3D(piece, null, CastleChangeKind.Created, 0,
                    position, "덩쿨 생성!"));
            return piece;
        }

        /// <summary>희귀도 4:2:1, 태그 5:5:2:6:2(2.5:2.5:1:3:1), 같은 묶음 내 균등 확률로 중복 없는 후보를 뽑는다.</summary>
        private void DealOffers()
        {
            List<CastleMaterial3D> available = _catalog.All.Where(x => !x.startOnly && !x.spawnOnly).ToList();
            string[] rarities = { "일반", "희귀", "고급" };
            int[] rarityWeights = { 4, 2, 1 };
            string[] categories = { "석재", "목재", "식물", "잡동사니", "쓰레기" };
            int[] categoryWeights = { 5, 5, 2, 6, 2 };
            for (int i = 0; i < _offers.Length; i++)
            {
                int rarityTotal = 0;
                for (int r = 0; r < rarities.Length; r++)
                    if (available.Any(x => x.rarity == rarities[r])) rarityTotal += rarityWeights[r];
                int rarityRoll = _random.Next(rarityTotal);
                int rarityIndex = 0;
                for (; rarityIndex < rarities.Length; rarityIndex++)
                {
                    if (!available.Any(x => x.rarity == rarities[rarityIndex])) continue;
                    rarityRoll -= rarityWeights[rarityIndex];
                    if (rarityRoll < 0) break;
                }
                List<CastleMaterial3D> tier = available.Where(x => x.rarity == rarities[rarityIndex]).ToList();
                int categoryTotal = 0;
                for (int c = 0; c < categories.Length; c++)
                    if (tier.Any(x => x.category == categories[c])) categoryTotal += categoryWeights[c];
                int categoryRoll = _random.Next(categoryTotal);
                int categoryIndex = 0;
                for (; categoryIndex < categories.Length; categoryIndex++)
                {
                    if (!tier.Any(x => x.category == categories[categoryIndex])) continue;
                    categoryRoll -= categoryWeights[categoryIndex];
                    if (categoryRoll < 0) break;
                }
                List<CastleMaterial3D> group = tier.Where(x => x.category == categories[categoryIndex]).ToList();
                _offers[i] = group[_random.Next(group.Count)];
                available.Remove(_offers[i]);
            }
            _selected = -1;
            _placementsInRound = 0;
            _paletteOpen = true;
            HideDropPreview();
        }

        /// <summary>기본 리롤권부터 소비하고 후보 3종을 다시 제시한다.</summary>
        public void Reroll()
        {
            if (!CanReroll) return;
            if (_rerolls > 0) _rerolls--;
            else if (_extraRerolls > 0) _extraRerolls--;
            else return;
            DealOffers();
        }

        /// <summary>각 기물의 낙하는 병렬 진행하고 5개가 모두 안정화되면 라운드를 정산한다.</summary>
        private IEnumerator DropAndResolve(CastleMaterial3D material, Vector3 position)
        {
            // StartCoroutine executes this section immediately, before its first
            // yield. Therefore subsequent clicks may spawn while earlier drops
            // are still falling, without unlocking the chosen material.
            CastlePiece3D dropped = Spawn(material, position);
            _placementsInRound++;
            _pendingDrops++;
            bool finalDrop = _placementsInRound == PLACEMENTS_PER_ROUND;
            if (finalDrop) _resolving = true;

            if (material.id == "empty_bottle")
            {
                _extraRerolls++;
                PublishChange(new CastleChange3D(dropped, dropped, CastleChangeKind.Resource,
                    1, dropped.transform.position, "리롤권 +1"));
            }
            _status = material.name + " 배치 " + _placementsInRound + " / "
                + PLACEMENTS_PER_ROUND
                + (finalDrop ? " — 낙하 정산 대기 중" : " (계속 드랍 가능)");

            float elapsed = 0f;
            while (elapsed < 7f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
                if (dropped == null || (elapsed >= 1.2f && dropped.Body.IsSleeping()))
                    break;
            }

            _pendingDrops--;

            // Only the fifth placement owns round completion. Earlier coroutines
            // finish independently even if all five were spawned in the meantime.
            if (!finalDrop)
                yield break;

            // Wait for every previously spawned piece, not just the fifth one.
            while (_pendingDrops > 0)
                yield return null;

            // Barrel fuses and effects must finish before the round is scored.
            float fuseElapsed = 0f;
            while (_pieces.Any(x => x != null && x.MaterialId == "barrel" && x.FusePending)
                && fuseElapsed < 3.5f)
            {
                yield return null;
                fuseElapsed += Time.deltaTime;
            }

            yield return new WaitForSeconds(0.3f);
            _turn++;
            TickPeriodic();
            RecalculateScores();
            if (_turn % ROUNDS_PER_STAGE == 0)
                FinishStage();
            else
            {
                _resolving = false;
                DealOffers();
            }
            if (!_awaitingStage) _status = "라운드 정산 완료: 현재 내구도 " + Durability;
        }

        /// <summary>원본 턴 기반 식물 성장 및 덩굴 번식을 물리 기물에 적용한다.</summary>
        private void TickPeriodic()
        {
            foreach (CastlePiece3D piece in _pieces.ToArray())
            {
                if (piece == null || IsAbilityDisabled(piece)) continue;
                int age = _turn - piece.PlacedTurn;
                if (age <= 0) continue;
                int woods = Adjacent(piece).Count(n => _catalog.Get(n.MaterialId).category == "목재");
                if (piece.MaterialId == "branch" && age % 3 == 0) piece.PermanentBonus += woods;
                if (piece.MaterialId == "leafy_branch" && age % 2 == 0) piece.PermanentBonus += woods;
                if (piece.MaterialId == "living_tree" && piece.GrowthCount < LIVING_TREE_MAX_GROWTH)
                {
                    piece.GrowthCount++;
                    if (piece.GrowthCount % 2 == 0) piece.PermanentBonus++;
                    piece.transform.localScale *= _livingTreeScalePerGrowth;
                    PublishChange(new CastleChange3D(piece, piece, CastleChangeKind.Growth,
                        0, piece.transform.position));
                }
                if (piece.MaterialId == "bamboo" && piece.GrowthCount < 3)
                {
                    BoxCollider collider = piece.GetComponent<BoxCollider>();
                    float segmentHeight = collider.size.y / (piece.GrowthCount + 1);
                    Vector3 previousTop = collider.transform.TransformPoint(collider.center
                        + Vector3.up * collider.size.y * 0.5f);
                    Vector3 extensionCenter = previousTop + piece.transform.up *
                        (segmentHeight * piece.transform.lossyScale.y * 0.5f);
                    foreach (Collider hit in Physics.OverlapBox(extensionCenter,
                        Vector3.Scale(new Vector3(collider.size.x, segmentHeight, collider.size.z)
                            * 0.5f, piece.transform.lossyScale),
                        piece.transform.rotation, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    {
                        CastlePiece3D blocked = hit.GetComponentInParent<CastlePiece3D>();
                        if (blocked != null && blocked != piece && blocked.MaterialId != "foundation")
                            RemovePiece(blocked);
                    }
                    int segmentParts = piece.transform.childCount / (piece.GrowthCount + 1);
                    for (int i = 0; i < segmentParts; i++)
                    {
                        Transform original = piece.transform.GetChild(i);
                        GameObject section = Instantiate(original.gameObject, piece.transform);
                        section.transform.localPosition = original.localPosition
                            + Vector3.up * segmentHeight * (piece.GrowthCount + 1);
                    }
                    collider.center += Vector3.up * segmentHeight * 0.5f;
                    collider.size += Vector3.up * segmentHeight;
                    piece.GrowthCount++;
                    if (piece.GrowthCount == 3) piece.PermanentBonus += 2;
                    PublishChange(new CastleChange3D(piece, piece, CastleChangeKind.Growth,
                        0, piece.transform.position));
                }
                if (piece.MaterialId == "growing_vine" && age % 2 == 0)
                {
                    Vector3 direction = Quaternion.Euler(0f, _random.Next(360), 0f) * Vector3.forward;
                    Spawn(_catalog.Get("vine"), piece.transform.position + direction * 1.2f + Vector3.up);
                }
            }
        }

        /// <summary>물리 접촉 즉시 성장 덩쿨·생성 덩쿨을 첫 대상에 영구 부착한다.</summary>
        public void TryAttachVines(CastlePiece3D a, CastlePiece3D b)
        {
            if (_stageTransitioning || a == null || b == null ||
                !_pieceSet.Contains(a) || !_pieceSet.Contains(b))
                return;

            bool canA = a.IsVine && !a.AbilitySuppressed && !a.HasAttachmentTarget;
            bool canB = b.IsVine && !b.AbilitySuppressed && !b.HasAttachmentTarget;
            CastlePiece3D vine, target;

            if (canA && canB)
            {
                // Deterministic direction for a vine-vine contact; one
                // connection must not suppress both vines at the same instant.
                bool aIsVine = a.GetInstanceID() > b.GetInstanceID();
                vine = aIsVine ? a : b;
                target = aIsVine ? b : a;
            }
            else if (canA)
            {
                vine = a;
                target = b;
            }
            else if (canB)
            {
                vine = b;
                target = a;
            }
            else return;

            if (!vine.TryAttachTo(target)) return;
            PublishChange(new CastleChange3D(target, vine, CastleChangeKind.EffectDisabled,
                0, target.transform.position));
            // Preserve the target's own base reinforcement while also
            // transferring that same base amount to the vine's permanent bonus.
            vine.PermanentBonus += _catalog.Get(target.MaterialId).baseReinforcement;
            _floodPreview.RequestRefresh();
        }

        /// <summary>진흙과 직접 접촉한 목재를 부착하고 목재끼리의 간접 인접을 활성화한다.</summary>
        public void TryAttachMud(CastlePiece3D a, CastlePiece3D b)
        {
            if (_stageTransitioning || !_pieceSet.Contains(a) || !_pieceSet.Contains(b)) return;
            CastlePiece3D mud = a.MaterialId == "mud" ? a : b.MaterialId == "mud" ? b : null;
            CastlePiece3D wood = mud == a ? b : a;
            if (mud != null && wood.MaterialId != "foundation" && !IsAbilityDisabled(mud) &&
                _catalog.Get(wood.MaterialId).category == "목재") mud.AttachWood(wood);
        }

        /// <summary>덩쿨 부착을 우선 처리한 뒤 아직 특수능력이 있는 폐기물 덩어리만 파괴 효과를 낸다.</summary>
        private void ApplyContactDestruction()
        {
            // The first phase handles a contact that has persisted from a
            // previous physics step without an OnCollisionEnter callback.
            foreach (CastlePiece3D piece in _pieces.ToArray())
            {
                if (piece == null || !piece.IsVine || piece.AbilitySuppressed ||
                    piece.HasAttachmentTarget)
                    continue;
                foreach (CastlePiece3D neighbor in Adjacent(piece).ToArray())
                {
                    TryAttachVines(piece, neighbor);
                    if (piece.HasAttachmentTarget) break;
                }
            }

            foreach (CastlePiece3D lump in _pieces.ToArray())
            {
                if (lump == null || lump.MaterialId != "foul_smelling_lump" ||
                    lump.AbilitySuppressed)
                    continue;

                foreach (CastlePiece3D neighbor in PiecesInRange(lump).ToArray())
                {
                    if (neighbor == null || !_pieceSet.Contains(neighbor) ||
                        _catalog.Get(neighbor.MaterialId).category == "쓰레기" ||
                        neighbor.MaterialId == "foundation")
                        continue;

                    string key = lump.GetInstanceID() + ":" + neighbor.GetInstanceID();
                    if (_consumedContacts.Add(key))
                        RemovePiece(neighbor);
                }
            }
        }

        /// <summary>접촉 그래프에서 소멸된 기물을 제외한 실제 인접 기물만 반환한다.</summary>
        private IEnumerable<CastlePiece3D> Adjacent(CastlePiece3D piece)
        {
            HashSet<CastlePiece3D> neighbors = new HashSet<CastlePiece3D>(
                piece.Contacts.Where(n => n != null && _pieceSet.Contains(n)));
            if (piece.MaterialId == "mud" || _catalog.Get(piece.MaterialId).category == "목재")
                foreach (CastlePiece3D attached in piece.AttachedPieces)
                    if (attached != null && _pieceSet.Contains(attached)) neighbors.Add(attached);
            if (_catalog.Get(piece.MaterialId).category == "목재")
                foreach (CastlePiece3D mud in neighbors.Where(n => n.MaterialId == "mud" &&
                    !IsAbilityDisabled(n)).ToArray())
                    foreach (CastlePiece3D wood in mud.AttachedPieces)
                        if (wood != piece && wood != null && _pieceSet.Contains(wood) &&
                            _catalog.Get(wood.MaterialId).category == "목재") neighbors.Add(wood);
            return neighbors;
        }

        /// <summary>덩쿨 봉인 또는 활성 라바콘 꼭대기의 접촉으로 특수능력이 막혔는지 확인한다.</summary>
        public bool IsAbilityDisabled(CastlePiece3D piece)
        {
            return piece.AbilitySuppressed;
        }

        /// <summary>기물 Collider가 지정된 위치 중심의 폐기물 효과 범위에 일부라도 겹치는지 판정한다.</summary>
        private static bool IsInWasteEffectRange(CastlePiece3D piece, Vector3 center)
        {
            float radiusSquared = WASTE_EFFECT_RADIUS * WASTE_EFFECT_RADIUS;
            foreach (Collider collider in piece.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                Vector3 closestPoint = collider.ClosestPoint(center);
                if ((closestPoint - center).sqrMagnitude <= radiusSquared) return true;
            }
            return false;
        }

        /// <summary>Collider가 폐기물 효과 범위와 겹치는 살아 있는 다른 기물을 반환한다.</summary>
        private IEnumerable<CastlePiece3D> PiecesInRange(CastlePiece3D source)
        {
            return _pieces.Where(piece => piece != null && piece != source &&
                IsInWasteEffectRange(piece, source.transform.position));
        }

        /// <summary>기물이 제거되면 덩쿨 FixedJoint로 연결된 모든 기물도 함께 제거한다.</summary>
        private int RemovePiece(CastlePiece3D piece, CastleChangeKind reason = CastleChangeKind.Destroyed)
        {
            if (piece == null || !_pieceSet.Contains(piece)) return 0;

            HashSet<CastlePiece3D> connected = new HashSet<CastlePiece3D>();
            Stack<CastlePiece3D> pending = new Stack<CastlePiece3D>();
            CastleFloodQuery3D.CollectAttachmentGroup(piece, _pieceSet, connected, pending,
                reason == CastleChangeKind.Flooded);

            foreach (CastlePiece3D current in connected)
            {
                if (current == _outlinedPiece) SetOutlineTargets(null);
                _pieceSet.Remove(current);
                _pieces.Remove(current);
                _score.Remove(current);
                PublishChange(new CastleChange3D(current, piece, reason, 0,
                    current.transform.position));
                Destroy(current.gameObject);
            }
            _floodPreview.RequestRefresh();
            return connected.Count;
        }

        /// <summary>폭발 시 오크통은 유지하고 주변 기물에 거리 감쇠가 있는 물리 충격을 준다.</summary>
        public void Explode(CastlePiece3D barrel)
        {
            if (barrel == null || !_pieceSet.Contains(barrel)) return;

            Vector3 origin = barrel.Body.worldCenterOfMass;
            HashSet<CastlePiece3D> targets = new HashSet<CastlePiece3D>();
            foreach (Collider collider in Physics.OverlapSphere(origin,
                BARREL_EXPLOSION_RADIUS, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                CastlePiece3D target = collider.GetComponentInParent<CastlePiece3D>();
                if (target == null || target == barrel || target.MaterialId == "foundation"
                    || !_pieceSet.Contains(target))
                    continue;

                Rigidbody body = target.Body;
                if (body == null || body.isKinematic) continue;
                targets.Add(target);
            }

            foreach (CastlePiece3D target in targets)
            {
                // Move the actual Rigidbody instead of destroying its GameObject.
                // One impulse per piece, even when a prefab has multiple colliders.
                target.Body.AddExplosionForce(barrelExplosionImpulse, origin,
                    BARREL_EXPLOSION_RADIUS, barrelExplosionUpwardsModifier,
                    ForceMode.Impulse);
            }

            // The barrel itself also remains in the game world.
            _status = "오크통 폭발: " + targets.Count + "개 주변 기물을 날렸습니다.";
        }

        /// <summary>원본 자재의 내구도 효과를 접촉 기반 인접 그래프에서 재계산한다.</summary>
        private void RecalculateScores()
        {
            var previousScores = new Dictionary<CastlePiece3D, int>(_score);
            var previous = new Dictionary<(CastlePiece3D, CastlePiece3D), int>(_contributions);
            _contributions.Clear();
            _score.Clear();
            foreach (CastlePiece3D piece in _pieces)
                if (piece != null)
                {
                    int baseline = _catalog.Get(piece.MaterialId).baseReinforcement + piece.PermanentBonus;
                    _score[piece] = baseline;
                    _contributions[(piece, null)] = baseline;
                }

            foreach (CastlePiece3D piece in _score.Keys.ToArray())
            {
                if (IsAbilityDisabled(piece)) continue;
                _effectSource = piece;
                CastlePiece3D[] neighbors = Adjacent(piece).ToArray();
                int Count(string category) => neighbors.Count(n => _catalog.Get(n.MaterialId).category == category);
                switch (piece.MaterialId)
                {
                    case "large_stone": if (Count("석재") >= 2) AddBonus(piece, 4); break;
                    case "fossil": if (neighbors.Count(n => n.MaterialId == "fossil") >= 3) AddBonus(piece, 10); break;
                    case "gem":
                        if (neighbors.Any(n => n.MaterialId == "pebble") &&
                            neighbors.Any(n => n.MaterialId == "large_stone") &&
                            neighbors.Any(n => n.MaterialId == "fossil")) AddBonus(piece, 15);
                        break;
                    case "log": if (neighbors.Any(n => n.MaterialId == "mud")) AddBonus(piece, 2); break;
                    case "broken_game_controller":
                        AddBonus(piece, neighbors.Where(n => _catalog.Get(n.MaterialId).category == "잡동사니")
                            .Select(n => n.MaterialId).Distinct().Count());
                        break;
                }
                if (piece.MaterialId == "bucket")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "석재"))
                        AddBonus(n, 1);
                if (piece.MaterialId == "watering_can")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "식물"))
                        AddBonus(n, 1);
                if (piece.MaterialId == "loose_work_glove")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "목재"))
                        AddBonus(n, 1);
                if (piece.MaterialId == "life_jacket")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "잡동사니"))
                        AddBonus(n, 1);
            }

            foreach (CastlePiece3D piece in _score.Keys.ToArray())
            {
                if (IsAbilityDisabled(piece)) continue;
                _effectSource = piece;
                if (piece.MaterialId != "waste_bag_small") continue;
                foreach (CastlePiece3D neighbor in PiecesInRange(piece))
                    if (_catalog.Get(neighbor.MaterialId).category != "쓰레기")
                        AddBonus(neighbor, -2);
            }
            _effectSource = null;
            foreach (var contribution in _contributions)
            {
                CastlePiece3D target = contribution.Key.Item1;
                if (!previousScores.ContainsKey(target)) continue;
                previous.TryGetValue(contribution.Key, out int oldAmount);
                int delta = contribution.Value - oldAmount;
                if (delta != 0)
                    PublishChange(new CastleChange3D(target, contribution.Key.Item2,
                         CastleChangeKind.Durability, delta, target.transform.position));
            }
            foreach (var contribution in previous)
            {
                CastlePiece3D target = contribution.Key.Item1;
                if (!_score.ContainsKey(target) || _contributions.ContainsKey(contribution.Key) ||
                    contribution.Value == 0) continue;
                PublishChange(new CastleChange3D(target, contribution.Key.Item2,
                    CastleChangeKind.Durability, -contribution.Value, target.transform.position));
            }
        }

        /// <summary>재료의 현재 내구도에 임시 또는 인접 효과를 더한다.</summary>
        private void AddBonus(CastlePiece3D target, int amount)
        {
            if (_score.ContainsKey(target))
            {
                _score[target] += amount;
                var key = (target, _effectSource);
                _contributions.TryGetValue(key, out int previous);
                _contributions[key] = previous + amount;
            }
        }

        /// <summary>현재 내구도로 스테이지를 판정하고 결과 팝업을 표시한다.</summary>
        private void FinishStage()
        {
            SetOutlineTargets(null);
            HideDropPreview();
            _stagePassed = Durability >= StageTarget(_stage);
            _status = _stagePassed ? "닫기 버튼을 누르면 홍수가 시작됩니다."
                : "목표 내구도에 도달하지 못했습니다.";
            _awaitingStage = true;
            _resolving = false;
        }

        /// <summary>
        /// Advances the flood, collects pieces whose colliders overlap each newly filled slab,
        /// and removes the contacted pieces after the water finishes filling.
        /// </summary>
        private IEnumerator ResolveStageFlood()
        {
            CastleFloodVolume3D volume = CaptureFloodVolume();
            var floodedPieces = new HashSet<CastlePiece3D>();
            float previousFilledLength = 0f;

            _floodEffect.Begin(volume);

            float elapsed = 0f;
            while (elapsed < floodFillDuration)
            {
                elapsed += Time.deltaTime;
                float fraction = Mathf.Clamp01(elapsed / floodFillDuration);
                float eased = floodFillCurve == null ? Mathf.SmoothStep(0f, 1f, fraction)
                    : Mathf.Clamp01(floodFillCurve.Evaluate(fraction));
                _floodEffect.SetFill(eased);
                CollectFloodContacts(volume, floodedPieces, previousFilledLength);
                previousFilledLength = _floodEffect.FilledLength;
                yield return null;
            }
            _floodEffect.SetFill(1f);
            CollectFloodContacts(volume, floodedPieces, previousFilledLength);

            foreach (CastlePiece3D piece in floodedPieces)
            {
                if (piece == null || !_pieceSet.Contains(piece)) continue;
                RemovePiece(piece, CastleChangeKind.Flooded);
            }

            // Let Destroy() flush before the flood quickly drops away.
            yield return null;

            float drain = 0f;
            while (drain < floodDrainDuration)
            {
                drain += Time.deltaTime;
                float fraction = Mathf.Clamp01(drain / floodDrainDuration);
                _floodEffect.SetDrain(fraction * fraction);
                yield return null;
            }
            _floodEffect.Hide();

            RecalculateScores();
            _stageTransitioning = false;
            _stageFloodRoutine = null;
            _resolving = false;
            _stage++;
            if (_stage == STAGE_COUNT) { _finished = true; yield break; }
            _rerolls = 3;
            DealOffers();
            _status = "홍수가 지나갔습니다. 다음 스테이지를 시작하세요.";
        }

        /// <summary>
        /// 공통 접촉 판정으로 현재 새로 채워진 구간의 비기반 자재를 결과에 추가한다.
        /// 이전 채움 길이와 표시기의 현재 길이를 사용해 실제 홍수의 접촉을 누적한다.
        /// </summary>
        private void CollectFloodContacts(CastleFloodVolume3D volume,
            HashSet<CastlePiece3D> floodedPieces, float previousFilledLength)
        {
            _floodQuery.CollectContacts(volume, previousFilledLength, _floodEffect.FilledLength,
                _pieceSet, floodedPieces);
        }

        /// <summary>
        /// 현재 생존 자재, 강바닥, 카메라와 고정 수면 높이로 공통 홍수 영역을 반환한다.
        /// 실제 홍수와 미리보기가 같은 공간 범위를 사용하도록 한다.
        /// </summary>
        internal CastleFloodVolume3D CaptureFloodVolume()
        {
            return CastleFloodQuery3D.Capture(_pieces, _camera, _placementBounds,
                _sceneContext.FloodSurfaceY, groundY);
        }

        /// <summary>
        /// 전달된 홍수 영역에서 직접 접촉하거나 영구 부착으로 함께 제거될 자재를 수집한다.
        /// 결과 집합을 현재 생존 자재 기준으로 갱신하며 게임 상태는 변경하지 않는다.
        /// </summary>
        internal void CollectFloodPreviewTargets(CastleFloodVolume3D volume,
            HashSet<CastlePiece3D> result)
        {
            _floodQuery.CollectDestructionTargets(volume, _pieceSet, result);
        }

        /// <summary>실패 시 재시작하고 통과 시 팝업을 닫아 홍수와 다음 스테이지를 진행한다.</summary>
        public void ContinueStage()
        {
            if (!_awaitingStage || _stageTransitioning) return;
            if (!_stagePassed || _finished) { ResetGame(); return; }
            _floodPreview.Hide();
            _awaitingStage = false;
            _stageTransitioning = true;
            _resolving = true;
            _status = "홍수 발생: 물건들이 휩쓸려 갑니다...";
            _stageFloodRoutine = StartCoroutine(ResolveStageFlood());
        }

        /// <summary>첫 스테이지 목표 30부터 스테이지마다 30씩 증가한 내구도를 반환한다.</summary>
        private static int StageTarget(int stage) => 30 * (stage + 1);
    }
}
