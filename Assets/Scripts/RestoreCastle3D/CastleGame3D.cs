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
        private const float BARREL_EXPLOSION_RADIUS = 4f;
        private const float CAMERA_DRAG_THRESHOLD_PIXELS = 6f;
        private const float CAMERA_MIN_TARGET_Z = -5f;
        private const float CAMERA_MAX_TARGET_Z = 15f;
        private const float WINDOWS_SCROLL_UNITS_PER_TICK = 120f;
        private const float FLOOD_SURFACE_Y = -1.95f;
        private const float BOUNDARY_DESTRUCTION_DELAY = 1f;

        [Header("Barrel explosion")]
        [SerializeField, Min(0f)] private float barrelExplosionImpulse = 40f;
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
        private readonly Dictionary<CastlePiece3D, int> _score = new Dictionary<CastlePiece3D, int>();
        private readonly Dictionary<(CastlePiece3D, CastlePiece3D), int> _contributions =
            new Dictionary<(CastlePiece3D, CastlePiece3D), int>();
        private readonly HashSet<string> _consumedContacts = new HashSet<string>();
        private readonly CastleMaterial3D[] _offers = new CastleMaterial3D[3];
        private GameSceneContext _sceneContext;
        private CastleCatalog3D _catalog;
        private CastleHud3D _hud;
        private CastleDropPreview3D _dropPreview;
        private CastleFloodEffect3D _floodEffect;
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

        [Header("Camera input")]
        private Vector2 _rightDragDisplacement;
        private bool _rightGestureActive;
        private bool _rightGestureMoved;
        private bool _rightGestureScrolled;

        internal float DestructionBoundaryZ => _sceneContext.DestructionBoundaryZ;

        private void Start()
        {
            _sceneContext = GetComponent<GameSceneContext>();
            _catalog = new CastleCatalog3D();
            ConfigureScene();
            CreateHud();
            _dropPreview = new CastleDropPreview3D();
            _floodEffect = new CastleFloodEffect3D();
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
                if (keyboard.qKey.isPressed) _objectYaw -= RotationSpeed * Time.deltaTime;
                if (keyboard.eKey.isPressed) _objectYaw += RotationSpeed * Time.deltaTime;
            }

            UpdateDropPreview();

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.isPressed || mouse.rightButton.wasReleasedThisFrame ||
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

        /// <summary>화면 커서가 배치 가능한 월드 XZ에 있으면 그 위치를 반환한다.</summary>
        private bool TryGetPlacementXZ(Vector2 screen, out Vector3 position)
        {
            if (!TryGetCursorXZ(screen, out position)) return false;
            return CanPlaceAt(position);
        }

        /// <summary>화면 안이고 UI 밖인 커서를 기준 평면 위 월드 XZ 위치로 변환한다.</summary>
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

        /// <summary>월드 XZ가 강바닥 안이고 Z가 15 미만이면 배치를 허용한다.</summary>
        private bool CanPlaceAt(Vector3 position)
        {
            return position.z < 15f && _placementBounds.Contains(
                new Vector3(position.x, groundY, position.z));
        }

        /// <summary>커서 위치의 배치 가능 여부에 따라 실제 자재의 낙하 미리보기 색을 바꾼다.</summary>
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
                Quaternion.Euler(0f, _objectYaw, 0f), dropHeight, canPlace,
                _placementBounds.max.y);

            if (_dropPreview.IsVisible)
            {
                Vector3 launchTarget = _camera.WorldToScreenPoint(
                    new Vector3(placement.x, dropHeight, placement.z));
                Vector3 screenTarget = _camera.WorldToScreenPoint(_dropPreview.LandingPosition);
                if (launchTarget.z > 0f && screenTarget.z > 0f)
                    _hud.ShowDropGuide(new Vector2(launchTarget.x, launchTarget.y),
                        new Vector2(screenTarget.x, screenTarget.y));
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
            _hud?.HideDropGuide();
        }

        private void OnDestroy()
        {
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
                if (_outlinedPiece != null) _outlinedPiece.SetOutlineLayer(0);
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
        public float CameraYawPerScrollStep { get; set; } = 5f;
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

        /// <summary>
        /// 우클릭 드래그로 카메라 주시점을 강의 Z축을 따라 이동하고 휠로 회전 또는 줌을 처리한다.
        /// UI 밖에서 시작한 짧은 우클릭은 버튼을 놓을 때 자재 후보를 연다.
        /// </summary>
        private void UpdateCameraInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                _rightGestureActive = false;
                return;
            }

            var rightButton = mouse.rightButton;
            bool pointerOverUi = EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
            if (rightButton.wasPressedThisFrame)
            {
                _rightGestureActive = !pointerOverUi;
                _rightGestureMoved = false;
                _rightGestureScrolled = false;
                _rightDragDisplacement = Vector2.zero;
            }

            bool gestureInProgress = _rightGestureActive &&
                (rightButton.isPressed || rightButton.wasReleasedThisFrame);
            if (gestureInProgress)
            {
                Vector2 delta = rightButton.wasPressedThisFrame
                    ? Vector2.zero : mouse.delta.ReadValue();
                if (!_rightGestureMoved)
                {
                    _rightDragDisplacement += delta;
                    if (_rightDragDisplacement.sqrMagnitude >=
                        CAMERA_DRAG_THRESHOLD_PIXELS * CAMERA_DRAG_THRESHOLD_PIXELS)
                    {
                        _rightGestureMoved = true;
                        delta = _rightDragDisplacement;
                    }
                    else
                        delta = Vector2.zero;
                }

                if (_rightGestureMoved && delta.sqrMagnitude > 0f)
                {
                    float unit = 2f * _camera.orthographicSize / Mathf.Max(Screen.height, 1);
                    Vector3 right = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;
                    Vector3 up = Vector3.ProjectOnPlane(_camera.transform.up, Vector3.up).normalized;
                    float movementZ = unit * (right.z * delta.x + up.z * delta.y);
                    _cameraTarget.z = Mathf.Clamp(_cameraTarget.z - movementZ,
                        CAMERA_MIN_TARGET_Z, CAMERA_MAX_TARGET_Z);
                }
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (gestureInProgress)
                {
                    _rightGestureScrolled = true;
                    if (!pointerOverUi)
                        _camera.transform.Rotate(Vector3.up,
                            NormalizeScrollSteps(scroll) * CameraYawPerScrollStep, Space.World);
                }
                else if (!rightButton.isPressed && !rightButton.wasReleasedThisFrame && !pointerOverUi)
                {
                    _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize *
                        Mathf.Exp(-scroll * 0.03f), 6f, 12.5f);
                }
            }

            if (rightButton.wasReleasedThisFrame)
            {
                if (_rightGestureActive && !_rightGestureMoved &&
                    !_rightGestureScrolled && !pointerOverUi)
                    OpenCandidatePalette();
                _rightGestureActive = false;
            }

            UpdateCameraPosition();
        }

        /// <summary>
        /// Input System의 휠 델타를 입력 설정과 실행 플랫폼에 맞춰 휠 칸 수로 환산한다.
        /// 정규화된 설정은 그대로 사용하고 Windows의 플랫폼 고유 값은 120으로 나눈다.
        /// </summary>
        private static float NormalizeScrollSteps(float scroll)
        {
            if (InputSystem.settings.scrollDeltaBehavior ==
                InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                (Application.platform == RuntimePlatform.WindowsEditor ||
                 Application.platform == RuntimePlatform.WindowsPlayer))
                return scroll / WINDOWS_SCROLL_UNITS_PER_TICK;

            return scroll;
        }

        /// <summary>현재 회전 방향과 주시 위치를 사용해 카메라 위치를 갱신한다.</summary>
        private void UpdateCameraPosition()
        {
            _camera.transform.position = _cameraTarget - _camera.transform.forward * 40f;
        }

        /// <summary>모든 배치와 소모품 상태를 초기화하고 기본 발판을 배치한다.</summary>
        private void ResetGame()
        {
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
            _rightGestureActive = false;
            UpdateCameraPosition();
            DealOffers();
            _status = "재료를 떨어뜨려 집을 쌓으세요. Q/E: 재료 회전";
        }

        /// <summary>Resources/Prefabs에서 3D 기물을 로드하고 Rigidbody를 통한 중력 낙하를 준비한다.</summary>
        private CastlePiece3D Spawn(CastleMaterial3D material, Vector3 position, bool anchored = false)
        {
            GameObject prefab = Resources.Load<GameObject>("Prefabs/" + material.id);
            if (prefab == null) throw new InvalidOperationException("Missing 3D prefab: " + material.id);
            GameObject instance = Instantiate(prefab, position, Quaternion.Euler(0f, _objectYaw, 0f));
            instance.name = material.name;
            CastlePiece3D piece = instance.GetComponent<CastlePiece3D>();
            piece.Initialize(this, _turn);
            piece.Body.isKinematic = anchored;
            piece.Body.useGravity = !anchored;
            _pieces.Add(piece);
            _pieceSet.Add(piece);
            if (material.id == "vine")
                PublishChange(new CastleChange3D(piece, null, CastleChangeKind.Created, 0,
                    position, "덩쿨 생성!"));
            return piece;
        }

        /// <summary>서로 다른 자재 3개를 뽑되 살아있는 나무의 등장 확률을 절반으로 낮춘다.</summary>
        private void DealOffers()
        {
            List<CastleMaterial3D> available = _catalog.All.Where(CastleCatalog3D.CanAppearAsOffer).ToList();
            for (int i = 0; i < _offers.Length; i++)
            {
                int index = _random.Next(available.Count);
                _offers[i] = available[index];
                available.RemoveAt(index);
                if (_offers[i].id == "living_tree" && _random.Next(2) == 0)
                {
                    index = _random.Next(available.Count);
                    _offers[i] = available[index];
                    available.RemoveAt(index);
                }
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

            if (material.id == "bucket")
            {
                dropped.PermanentBonus += 6;
                PublishChange(new CastleChange3D(dropped, dropped, CastleChangeKind.Durability,
                    6, dropped.transform.position));
            }
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
            while (elapsed < _sceneContext.MaxDropWait)
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

            yield return ResolveBarrelExplosions();

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

        /// <summary>
        /// 모든 낙하가 정산된 뒤 예약된 오크통을 점화하고 폭발로 생긴 물리 변화를 기다린다.
        /// 추가 충돌로 예약된 오크통도 처리하며 마지막 점화 후에는 씬에 설정된 최대 시간까지 안정화를 기다린다.
        /// </summary>
        private IEnumerator ResolveBarrelExplosions()
        {
            bool started = false;
            float settleElapsed = 0f;
            while (true)
            {
                foreach (CastlePiece3D piece in _pieces.ToArray())
                    if (piece != null && piece.BeginScheduledDetonation()) started = true;
                if (!started) yield break;

                bool pending = _pieces.Any(piece => piece != null && piece.FusePending);
                if (!pending && (settleElapsed >= _sceneContext.MaxExplosionSettleWait ||
                    (settleElapsed >= 0.3f && _pieces.All(piece => piece == null ||
                        piece.Body.isKinematic || piece.Body.IsSleeping()))))
                    yield break;

                yield return new WaitForFixedUpdate();
                settleElapsed = pending ? 0f : settleElapsed + Time.fixedDeltaTime;
            }
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
                if (piece.MaterialId == "living_tree" && age % 2 == 0)
                {
                    piece.PermanentBonus++;
                    piece.transform.localScale *= 1.1f;
                    PublishChange(new CastleChange3D(piece, piece, CastleChangeKind.Growth,
                        0, piece.transform.position));
                }
                if (piece.MaterialId == "bamboo")
                {
                    BoxCollider collider = piece.GetComponent<BoxCollider>();
                    Vector3 previousTop = collider.transform.TransformPoint(collider.center
                        + Vector3.up * collider.size.y * 0.5f);
                    Vector3 extensionCenter = previousTop + piece.transform.up * 0.5f;
                    foreach (Collider hit in Physics.OverlapBox(extensionCenter,
                        new Vector3(collider.size.x * 0.5f, 0.5f, collider.size.z * 0.5f),
                        piece.transform.rotation, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                    {
                        CastlePiece3D blocked = hit.GetComponentInParent<CastlePiece3D>();
                        if (blocked != null && blocked != piece && blocked.MaterialId != "foundation")
                            RemovePiece(blocked);
                    }
                    collider.center += Vector3.up * 0.5f;
                    collider.size += Vector3.up;
                    foreach (Transform child in piece.transform)
                    {
                        child.localPosition += Vector3.up * 0.5f;
                        Vector3 scale = child.localScale;
                        scale.y += 1f;
                        child.localScale = scale;
                    }
                    piece.PermanentBonus++;
                    PublishChange(new CastleChange3D(piece, piece, CastleChangeKind.Growth,
                        0, piece.transform.position));
                }
                if (piece.MaterialId == "growing_vine" && age % 3 == 0)
                    Spawn(_catalog.Get("vine"), piece.transform.position + new Vector3(1f, 2.2f, 0.3f));
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
        }

        /// <summary>
        /// 충돌한 a와 b가 살아 있으면 두 자재의 결합강도 합으로 일반 조인트를 생성한다.
        /// 생성 주체는 인스턴스 ID로 정하며 홍수 중에도 결합할 수 있다.
        /// </summary>
        public void TryAttachGlue(CastlePiece3D a, CastlePiece3D b)
        {
            if (a == null || b == null || !_pieceSet.Contains(a) || !_pieceSet.Contains(b))
                return;

            CastlePiece3D owner = a.GetInstanceID() > b.GetInstanceID() ? a : b;
            CastlePiece3D target = owner == a ? b : a;
            float strength = _catalog.Get(a.MaterialId).bondStrength +
                _catalog.Get(b.MaterialId).bondStrength;
            owner.TryGlueTo(target, strength);
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

                foreach (CastlePiece3D neighbor in Adjacent(lump).ToArray())
                {
                    if (neighbor == null || !_pieceSet.Contains(neighbor) ||
                        neighbor.IsVine || neighbor.MaterialId == "foundation" ||
                        neighbor.MaterialId == "foul_smelling_lump")
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
            return piece.Contacts.Where(n => n != null && _pieceSet.Contains(n));
        }

        /// <summary>덩쿨 봉인 또는 활성 라바콘 꼭대기의 접촉으로 특수능력이 막혔는지 확인한다.</summary>
        public bool IsAbilityDisabled(CastlePiece3D piece)
        {
            return piece.AbilitySuppressed || Adjacent(piece).Any(n =>
                n.MaterialId == "traffic_cone" && !n.AbilitySuppressed &&
                piece.transform.position.y > n.transform.position.y + 0.15f);
        }

        /// <summary>
        /// Z 경계를 넘은 기물만 제거하고 스테이지 전환 밖에서는 내구도를 갱신한다.
        /// piece는 경계를 넘은 기물이다.
        /// </summary>
        internal void RemoveOutsideBoundary(CastlePiece3D piece)
        {
            if (RemovePiece(piece, CastleChangeKind.Destroyed, BOUNDARY_DESTRUCTION_DELAY,
                false) > 0 && !_stageTransitioning)
                RecalculateScores();
        }

        /// <summary>기물을 제거하고 요청된 경우에만 연결 기물까지 연쇄 제거한다.</summary>
        private int RemovePiece(CastlePiece3D piece, CastleChangeKind reason = CastleChangeKind.Destroyed,
            float destructionDelay = 0f, bool removeAttached = true)
        {
            if (piece == null || !_pieceSet.Contains(piece)) return 0;

            HashSet<CastlePiece3D> connected = new HashSet<CastlePiece3D>();
            Stack<CastlePiece3D> pending = new Stack<CastlePiece3D>();
            pending.Push(piece);
            while (pending.Count > 0)
            {
                CastlePiece3D current = pending.Pop();
                if (current == null || !_pieceSet.Contains(current) || !connected.Add(current))
                    continue;
                if (removeAttached)
                    foreach (CastlePiece3D attached in current.AttachedPieces)
                        if (attached != null && !connected.Contains(attached))
                            pending.Push(attached);
            }

            foreach (CastlePiece3D current in connected)
            {
                if (current == _outlinedPiece) SetOutlineTargets(null);
                _pieceSet.Remove(current);
                current.ReleaseGlueJoints();
                _pieces.Remove(current);
                _score.Remove(current);
                PublishChange(new CastleChange3D(current, piece, reason, 0,
                    current.transform.position));
                Destroy(current.gameObject, destructionDelay);
            }
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
                    case "large_stone": if (Count("석재") >= 2) AddBonus(piece, 2); break;
                    case "fossil": if (neighbors.Count(n => n.MaterialId == "fossil") >= 3) AddBonus(piece, 10); break;
                    case "tire": AddBonus(piece, -Count("쓰레기")); break;
                    case "umbrella": if (_turn - piece.PlacedTurn <= 3) AddBonus(piece, 3); break;
                    case "life_ring": AddBonus(piece, Mathf.Min(2, Count("쓰레기"))); break;
                    case "rubber_duck": if (neighbors.Any(n => n.MaterialId == "life_ring")) AddBonus(piece, 4); break;
                    case "dented_can": AddBonus(piece, Count("식물")); break;
                    case "broken_game_controller":
                        AddBonus(piece, neighbors.Where(n => _catalog.Get(n.MaterialId).category == "쓰레기")
                            .Select(n => n.MaterialId).Distinct().Count());
                        break;
                    case "all_purpose_glue": AddBonus(piece, neighbors.Select(n => n.MaterialId).Distinct().Count()); break;
                }
                if (piece.MaterialId == "watering_can")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "식물"))
                        AddBonus(n, 1);
                if (piece.MaterialId == "loose_work_glove")
                    foreach (CastlePiece3D n in neighbors.Where(n => _catalog.Get(n.MaterialId).category == "목재"))
                        AddBonus(n, 1);
            }

            foreach (CastlePiece3D piece in _score.Keys.ToArray())
            {
                if (IsAbilityDisabled(piece)) continue;
                _effectSource = piece;
                int penalty = piece.MaterialId == "waste_bag_small" ? 1 :
                    piece.MaterialId == "waste_bag_medium" ? 2 : piece.MaterialId == "waste_bag_large" ? 3 : 0;
                if (penalty == 0) continue;
                foreach (CastlePiece3D neighbor in Adjacent(piece))
                    if (_catalog.Get(neighbor.MaterialId).category != "쓰레기" &&
                        _catalog.Get(neighbor.MaterialId).category != "폐기물")
                        AddBonus(neighbor, -penalty);
            }
            Dictionary<CastlePiece3D, int> beforeLog = new Dictionary<CastlePiece3D, int>(_score);
            foreach (CastlePiece3D piece in _score.Keys.ToArray())
                if (!IsAbilityDisabled(piece) && piece.MaterialId == "log")
                {
                    _effectSource = piece;
                    foreach (CastlePiece3D n in Adjacent(piece))
                        if (_catalog.Get(n.MaterialId).category == "목재")
                        {
                            _effectSource = n;
                            AddBonus(piece, beforeLog[n]);
                        }
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
        /// 메쉬 범위를 캡처하고 물의 전면이 지나가는 동안 닿은 기물에 충격을 준다.
        /// </summary>
        private IEnumerator ResolveStageFlood()
        {
            StageCullSnapshot snapshot = CaptureStageCullSnapshot();
            var pushed = new HashSet<CastlePiece3D>();

            _floodEffect.Begin(snapshot.CenterX, snapshot.Width,
                snapshot.StartZ, snapshot.EndZ, snapshot.Cutoff, groundY);

            float elapsed = 0f;
            while (elapsed < floodFillDuration)
            {
                elapsed += Time.deltaTime;
                float fraction = Mathf.Clamp01(elapsed / floodFillDuration);
                float eased = floodFillCurve == null ? Mathf.SmoothStep(0f, 1f, fraction)
                    : Mathf.Clamp01(floodFillCurve.Evaluate(fraction));
                _floodEffect.SetFill(eased);
                PushFloodedPieces(snapshot, snapshot.StartZ +
                    (snapshot.EndZ - snapshot.StartZ) * eased, pushed);
                yield return null;
            }
            _floodEffect.SetFill(1f);
            PushFloodedPieces(snapshot, snapshot.EndZ, pushed);

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

        /// <summary>살아 있는 기물의 렌더러 메쉬 범위와 물의 진행 범위를 캡처한다.</summary>
        private StageCullSnapshot CaptureStageCullSnapshot()
        {
            var pieces = new List<CastlePiece3D>(_pieces.Count);
            var bounds = new List<Bounds>(_pieces.Count);
            float leftX = float.PositiveInfinity;
            float rightX = float.NegativeInfinity;
            float nearZ = float.PositiveInfinity;
            float farZ = float.NegativeInfinity;

            foreach (CastlePiece3D piece in _pieces)
            {
                if (piece == null) continue;
                Vector3 position = piece.transform.position;
                leftX = Mathf.Min(leftX, position.x);
                rightX = Mathf.Max(rightX, position.x);
                nearZ = Mathf.Min(nearZ, position.z);
                farZ = Mathf.Max(farZ, position.z);

                if (piece.MaterialId == "foundation") continue;
                Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds meshBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    meshBounds.Encapsulate(renderers[i].bounds);
                pieces.Add(piece);
                bounds.Add(meshBounds);
            }

            // Include the whole camera viewport even when the pile is tiny.
            Plane waterPlane = new Plane(Vector3.up, new Vector3(0f, FLOOD_SURFACE_Y, 0f));
            Vector2[] corners = { Vector2.zero, new Vector2(Screen.width, 0f),
                new Vector2(0f, Screen.height), new Vector2(Screen.width, Screen.height) };
            foreach (Vector2 corner in corners)
            {
                Ray ray = _camera.ScreenPointToRay(corner);
                if (!waterPlane.Raycast(ray, out float distance)) continue;
                Vector3 pos = ray.GetPoint(distance);
                leftX = Mathf.Min(leftX, pos.x);
                rightX = Mathf.Max(rightX, pos.x);
                nearZ = Mathf.Min(nearZ, pos.z);
                farZ = Mathf.Max(farZ, pos.z);
            }

            if (float.IsPositiveInfinity(leftX))
            {
                leftX = nearZ = -10f;
                rightX = farZ = 10f;
            }

            const float margin = 8f;
            return new StageCullSnapshot(pieces.ToArray(), bounds.ToArray(),
                FLOOD_SURFACE_Y, (leftX + rightX) * 0.5f,
                Mathf.Max(1f, rightX - leftX + margin * 2f),
                farZ + margin, nearZ - margin);
        }

        /// <summary>snapshot과 frontZ로 파도 접촉을 확인하고 씬의 충격량을 -Z 방향으로 한 번 적용한다.</summary>
        private void PushFloodedPieces(StageCullSnapshot snapshot, float frontZ,
            HashSet<CastlePiece3D> pushed)
        {
            for (int i = 0; i < snapshot.Pieces.Length; i++)
            {
                CastlePiece3D piece = snapshot.Pieces[i];
                Bounds mesh = snapshot.Bounds[i];
                if (piece == null || pushed.Contains(piece) ||
                    mesh.min.y > snapshot.Cutoff ||
                    mesh.min.z > snapshot.StartZ || mesh.max.z < snapshot.EndZ ||
                    mesh.max.z < frontZ)
                    continue;
                pushed.Add(piece);
                if (!piece.Body.isKinematic)
                    piece.Body.AddForce(Vector3.back * _sceneContext.FloodImpulse,
                        ForceMode.Impulse);
            }
        }

        private sealed class StageCullSnapshot
        {
            public readonly CastlePiece3D[] Pieces;
            public readonly Bounds[] Bounds;
            public readonly float Cutoff;
            public readonly float CenterX;
            public readonly float Width;
            public readonly float StartZ;
            public readonly float EndZ;

            /// <summary>파도 접촉 검사에 필요한 기물 메쉬 범위와 물의 경계를 저장한다.</summary>
            public StageCullSnapshot(CastlePiece3D[] pieces, Bounds[] bounds, float cutoff,
                float centerX, float width, float startZ, float endZ)
            {
                Pieces = pieces;
                Bounds = bounds;
                Cutoff = cutoff;
                CenterX = centerX;
                Width = width;
                StartZ = startZ;
                EndZ = endZ;
            }
        }

        /// <summary>실패 시 재시작하고 통과 시 팝업을 닫아 홍수와 다음 스테이지를 진행한다.</summary>
        public void ContinueStage()
        {
            if (!_awaitingStage || _stageTransitioning) return;
            if (!_stagePassed || _finished) { ResetGame(); return; }
            _awaitingStage = false;
            _stageTransitioning = true;
            _resolving = true;
            _status = "홍수 발생: 물건들이 휩쓸려 갑니다...";
            _stageFloodRoutine = StartCoroutine(ResolveStageFlood());
        }

        /// <summary>스테이지별 목표 내구도를 50씩 증가시켜 반환한다.</summary>
        private static int StageTarget(int stage) => 50 * (stage + 1);
    }
}
