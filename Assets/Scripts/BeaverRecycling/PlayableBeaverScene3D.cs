using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

using RestoreCastle3D;

namespace BeaverRecycling
{
    /// <summary>W11 비버를 W12 연속 스폰 하천에서 조종하고 기존 카메라 각도로 추적한다.</summary>
    public sealed class PlayableBeaverScene3D : MonoBehaviour
    {
        private const float CAMERA_MIN_TARGET_Z = -15f;
        private const float CAMERA_MAX_TARGET_Z = 30f;
        private const float CAMERA_DISTANCE = 20f;
        private const float GRASS_RAYCAST_START_Y = 80f;
        private const float GRASS_RAYCAST_DISTANCE = 200f;
        private const int MAX_CARRIED_ITEMS = 6;
        private const float DASH_DURATION_SECONDS = 3f;
        private const float DASH_RECOVERY_SECONDS = 3f;
        private const float CARRIED_ITEM_GAP = 0.12f;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 4.5f;
        [SerializeField, Min(1f)] private float _swimSpeedMultiplier = 1.5f;
        [SerializeField, Min(1f)] private float _dashSpeedMultiplier = 2f;
        [SerializeField, Min(0.1f)] private float _pickupRadius = 2.4f;

        [Header("River swimming")]
        [SerializeField, Range(0f, 1f)] private float _submergedBodyFraction = 0.6f;
        [SerializeField, Min(0f)] private float _swimmingEyeTrailCount = 1f;
        [SerializeField, Min(0f)] private float _bankContactPadding = 0.25f;
        [SerializeField, Min(0.1f)] private float _bankGrassProbeDistance = 2.5f;
        [SerializeField, Min(0.1f)] private float _bankClimbSpeed = 14f;

        [Header("Camera follow")]
        [SerializeField, Min(0f)] private float _lookAheadDistance = 3f;
        [SerializeField, Min(0.1f)] private float _followSharpness = 6f;

        private RecyclingBeaver3D _beaver;
        private PlayableChute3D[] _chutes;
        private readonly List<CastlePiece3D> _held = new List<CastlePiece3D>();
        private readonly List<float> _carryDistances = new List<float>();
        private readonly List<FixedJoint> _carryJoints = new List<FixedJoint>();
        private RectTransform _dashFill;
        private Image _dashFillImage;
        private TextMeshProUGUI _dashLabel;
        private float _dashRemaining = DASH_DURATION_SECONDS;
        private bool _isDashing;
        private Camera _camera;
        private Vector2 _movement;
        private Vector3 _cameraTarget;
        private RiverWaterSurface3D[] _riverSurfaces;
        private MeshCollider[] _grassColliders;
        private BoxCollider _beaverCollider;
        private float _bodyBottomOffset;
        private float _bodyHeight;
        private bool _isSwimming;

        private void Start()
        {
            _beaver = FindFirstObjectByType<RecyclingBeaver3D>();
            _chutes = FindObjectsByType<PlayableChute3D>(FindObjectsSortMode.None);
            _camera = Camera.main;
            _beaverCollider = _beaver.GetComponent<BoxCollider>();
            Bounds bodyBounds = _beaver.BodyBounds;
            _bodyBottomOffset = bodyBounds.min.y - _beaver.transform.position.y;
            _bodyHeight = bodyBounds.size.y;
            _riverSurfaces = FindObjectsByType<RiverWaterSurface3D>(FindObjectsSortMode.None);
            MeshCollider[] sceneColliders = FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
            List<MeshCollider> grassColliders = new List<MeshCollider>();
            foreach (MeshCollider collider in sceneColliders)
                if (collider.name.Contains("Grass")) grassColliders.Add(collider);
            _grassColliders = grassColliders.ToArray();
            GetComponent<CastleGame3D>().PlayerCameraFollowEnabled = true;
            UpdateSurfaceHeightAndSwimming();
            _cameraTarget = new Vector3(0f, 0f,
                Mathf.Clamp(_beaver.transform.position.z, CAMERA_MIN_TARGET_Z, CAMERA_MAX_TARGET_Z));
            UpdateCameraPosition();
            CreateDashGauge();
        }

        private void Update()
        {
            _movement = Managers.Input.MoveInput;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float steps = scroll / 120f;
                    _camera.orthographicSize = Mathf.Clamp(
                        _camera.orthographicSize * Mathf.Exp(-steps * 0.12f), 6f, 13.5f);
                }
            }
            bool keyboardInteract = Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame;
            bool gamepadInteract = Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
            if (keyboardInteract || gamepadInteract) Interact();
            RefreshOutlines();
            RefreshDashGauge();
        }

        private void FixedUpdate()
        {
            UpdateDash(Time.fixedDeltaTime);
            float speed = _moveSpeed * (_isSwimming ? _swimSpeedMultiplier : 1f) *
                (_isDashing ? _dashSpeedMultiplier : 1f);
            _beaver.Move(_movement, speed, Time.fixedDeltaTime);
            UpdateSurfaceHeightAndSwimming();
            UpdateCarriedItems();
        }

        private void LateUpdate()
        {
            float targetZ = Mathf.Clamp(
                _beaver.transform.position.z + _movement.y * _lookAheadDistance,
                CAMERA_MIN_TARGET_Z, CAMERA_MAX_TARGET_Z);
            float blend = 1f - Mathf.Exp(-_followSharpness * Time.unscaledDeltaTime);
            _cameraTarget.z = Mathf.Lerp(_cameraTarget.z, targetZ, blend);
            UpdateCameraPosition();
        }

        /// <summary>비버 중심이 속한 river의 수면 높이와 월드 경계를 구한다.</summary>
        private bool TryGetRiverSurfaceY(Vector3 position, out float surfaceY, out Bounds riverBounds)
        {
            surfaceY = 0f;
            riverBounds = default;
            float closestHeightDifference = float.PositiveInfinity;
            bool found = false;
            foreach (RiverWaterSurface3D river in _riverSurfaces)
            {
                Bounds bounds = river.GetComponent<BoxCollider>().bounds;
                if (position.x < bounds.min.x || position.x > bounds.max.x ||
                    position.z < bounds.min.z || position.z > bounds.max.z) continue;

                float candidateY = river.SurfaceY;
                float difference = Mathf.Abs(candidateY - position.y);
                if (difference >= closestHeightDifference) continue;
                closestHeightDifference = difference;
                surfaceY = candidateY;
                riverBounds = bounds;
                found = true;
            }
            return found;
        }

        /// <summary>Grass 메시 콜라이더에 수직 레이를 쏘아 비버 위치의 땅 높이를 구한다.</summary>
        private bool TryGetGrassSurfaceY(Vector3 position, out float surfaceY)
        {
            surfaceY = float.NegativeInfinity;
            Ray ray = new Ray(new Vector3(position.x, GRASS_RAYCAST_START_Y, position.z),
                Vector3.down);
            bool found = false;
            foreach (MeshCollider grass in _grassColliders)
            {
                Bounds bounds = grass.bounds;
                if (position.x < bounds.min.x || position.x > bounds.max.x ||
                    position.z < bounds.min.z || position.z > bounds.max.z) continue;
                if (!grass.Raycast(ray, out RaycastHit hit, GRASS_RAYCAST_DISTANCE)) continue;
                if (hit.point.y <= surfaceY) continue;
                surfaceY = hit.point.y;
                found = true;
            }
            return found;
        }

        /// <summary>강의 좌우 경계와 비버 충돌 몸체가 닿으면 강둑 바깥쪽 Grass 높이를 찾아낸다.</summary>
        private bool TryGetBankContactHeight(Vector3 position, Bounds riverBounds,
            out float grassSurfaceY)
        {
            grassSurfaceY = 0f;
            float contactRadius = _beaverCollider.bounds.extents.x + _bankContactPadding;
            float leftDistance = Mathf.Abs(position.x - riverBounds.min.x);
            float rightDistance = Mathf.Abs(riverBounds.max.x - position.x);
            float nearestDistance = Mathf.Min(leftDistance, rightDistance);
            if (nearestDistance > contactRadius) return false;

            float shoreX = leftDistance <= rightDistance ? riverBounds.min.x : riverBounds.max.x;
            float side = leftDistance <= rightDistance ? -1f : 1f;
            const float PROBE_STEP = 0.25f;
            for (float distance = PROBE_STEP; distance <= _bankGrassProbeDistance;
                distance += PROBE_STEP)
            {
                Vector3 probe = new Vector3(shoreX + side * distance, position.y, position.z);
                if (TryGetGrassSurfaceY(probe, out grassSurfaceY)) return true;
            }
            return false;
        }

        /// <summary>강둑에 몸체가 닿으면 미리 Grass 높이로 올라가며,
        /// 물 한가운데서는 몸통을 잠기게 하고 육지에서는 지면 높이로 이동한다.</summary>
        private void UpdateSurfaceHeightAndSwimming()
        {
            Vector3 position = _beaver.transform.position;
            bool inRiver = TryGetRiverSurfaceY(position, out float waterSurfaceY,
                out Bounds riverBounds);
            float bankSurfaceY = 0f;
            bool climbingBank = inRiver &&
                TryGetBankContactHeight(position, riverBounds, out bankSurfaceY);
            bool swimming = inRiver && !climbingBank;

            if (climbingBank)
            {
                float nextHeight = Mathf.MoveTowards(position.y, bankSurfaceY,
                    _bankClimbSpeed * Time.fixedDeltaTime);
                _beaver.SetWorldHeight(nextHeight);
            }
            else if (swimming)
            {
                float waterlineOffset = _bodyBottomOffset + _bodyHeight * _submergedBodyFraction;
                _beaver.SetWorldHeight(waterSurfaceY - waterlineOffset);
            }
            else if (TryGetGrassSurfaceY(position, out float grassSurfaceY))
            {
                _beaver.SetWorldHeight(grassSurfaceY);
            }

            _isSwimming = swimming;
        }

        /// <summary>W11의 Shift/패드 X 대시를 적용한다. 누르고 이동 중 소모하고 버튼을 놓으면 3초 동안 재충전한다.</summary>
        private void UpdateDash(float deltaTime)
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool dashHeld = (keyboard != null &&
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)) ||
                (pad != null && pad.buttonWest.isPressed);
            _isDashing = dashHeld && _movement.sqrMagnitude > 0.0001f &&
                _dashRemaining > 0f;
            if (_isDashing)
                _dashRemaining = Mathf.Max(0f, _dashRemaining - deltaTime);
            else if (!dashHeld)
                _dashRemaining = Mathf.Min(DASH_DURATION_SECONDS,
                    _dashRemaining + deltaTime *
                    (DASH_DURATION_SECONDS / DASH_RECOVERY_SECONDS));

            // Eye Trail은 물 위 이동이 아닌 대시 상태일 때만 표현한다.
            _beaver.SetCarriedItemEffect(Mathf.Max(1f, _held.Count),
                _isDashing ? 1f : 0f);
        }

        /// <summary>W11 형식의 하단 대시 게이지를 런타임 UI로 생성한다.</summary>
        private void CreateDashGauge()
        {
            GameObject canvasObject = new GameObject("Playable Dash Canvas",
                typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 16;

            GameObject gaugeObject = new GameObject("Dash Gauge", typeof(RectTransform));
            RectTransform gauge = gaugeObject.GetComponent<RectTransform>();
            gauge.SetParent(canvasObject.transform, false);
            gauge.anchorMin = gauge.anchorMax = new Vector2(0.5f, 0f);
            gauge.pivot = new Vector2(0.5f, 0f);
            gauge.anchoredPosition = new Vector2(0f, 160f);
            gauge.sizeDelta = new Vector2(500f, 56f);

            GameObject trackObject = new GameObject("Gauge Track",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform track = trackObject.GetComponent<RectTransform>();
            track.SetParent(gauge, false);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.anchoredPosition = Vector2.zero;
            track.sizeDelta = new Vector2(0f, 24f);
            Image trackImage = trackObject.GetComponent<Image>();
            trackImage.color = new Color(0.035f, 0.075f, 0.1f, 0.95f);
            trackImage.raycastTarget = false;

            GameObject insetObject = new GameObject("Gauge Inset", typeof(RectTransform));
            RectTransform inset = insetObject.GetComponent<RectTransform>();
            inset.SetParent(track, false);
            inset.anchorMin = Vector2.zero;
            inset.anchorMax = Vector2.one;
            inset.offsetMin = new Vector2(3f, 3f);
            inset.offsetMax = new Vector2(-3f, -3f);

            GameObject fillObject = new GameObject("Gauge Fill",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _dashFill = fillObject.GetComponent<RectTransform>();
            _dashFill.SetParent(inset, false);
            _dashFill.anchorMin = Vector2.zero;
            _dashFill.anchorMax = Vector2.one;
            _dashFill.offsetMin = Vector2.zero;
            _dashFill.offsetMax = Vector2.zero;
            _dashFillImage = fillObject.GetComponent<Image>();
            _dashFillImage.raycastTarget = false;

            GameObject labelObject = new GameObject("Gauge Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(gauge, false);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, 26f);
            _dashLabel = labelObject.GetComponent<TextMeshProUGUI>();
            _dashLabel.font = TMP_Settings.defaultFontAsset;
            _dashLabel.fontSize = 19f;
            _dashLabel.alignment = TextAlignmentOptions.Center;
            _dashLabel.color = new Color(0.92f, 0.97f, 1f, 1f);
            _dashLabel.raycastTarget = false;
        }

        /// <summary>대시 잔량과 입력 장치에 따라 게이지 길이·색상·문구를 갱신한다.</summary>
        private void RefreshDashGauge()
        {
            float fill = Mathf.Clamp01(_dashRemaining / DASH_DURATION_SECONDS);
            _dashFill.anchorMax = new Vector2(fill, 1f);
            _dashFillImage.color = _isDashing
                ? new Color(1f, 0.68f, 0.24f, 1f)
                : new Color(0.35f, 0.85f, 0.9f, 1f);
            string key = Gamepad.current != null ? "X" : "SHIFT";
            _dashLabel.text = _isDashing
                ? key + " · 대시 " + _dashRemaining.ToString("0.0") + "초"
                : fill >= 0.999f
                    ? key + " · 대시 준비"
                    : key + " · 충전 " + _dashRemaining.ToString("0.0") +
                      " / " + DASH_DURATION_SECONDS.ToString("0") + "초";
        }

        /// <summary>최대 6개의 기물을 비버가 바라보는 forward 방향으로 앞뒤 간격을 유지해 운반한다.</summary>
        private void UpdateCarriedItems()
        {
            Vector3 start = _beaver.CarryPoint.position;
            Vector3 forward = _beaver.transform.forward;
            Quaternion rotation = _beaver.transform.rotation;
            for (int i = 0; i < _held.Count; i++)
            {
                CastlePiece3D piece = _held[i];
                if (piece == null) continue;
                piece.transform.rotation = rotation;
                piece.MoveHeld(start + forward * _carryDistances[i]);
            }
        }

        /// <summary>재질 안내는 노랑(레이어4), 현재 Z로 집을 수 있는 대상은 우선 주황(레이어2)으로 표시한다.</summary>
        private void RefreshOutlines()
        {
            CastleCreekScore3D score = CastleCreekScore3D.Active;
            if (score == null) return;
            int selectedCategory = -1;
            if (_held.Count > 0)
                PlayableRecyclingRules3D.TryGetCategory(_held[0].MaterialId,
                    out selectedCategory);

            float radiusSquared = _pickupRadius * _pickupRadius;
            Vector3 carryPoint = _beaver.CarryPoint.position;
            foreach (CastlePiece3D piece in score.RegisteredPieces)
            {
                if (piece == null || !piece.isActiveAndEnabled) continue;
                if (_held.Contains(piece)) { piece.SetOutlineLayer(0u); continue; }
                if (!PlayableRecyclingRules3D.TryGetCategory(piece.MaterialId,
                    out int category)) continue;
                Vector3 delta = piece.transform.position - carryPoint;
                bool pickupAvailable = _held.Count < MAX_CARRIED_ITEMS &&
                    piece.CanPickUp && delta.sqrMagnitude <= radiusSquared;
                piece.SetOutlineLayer(pickupAvailable ? 2u :
                    selectedCategory >= 0 && selectedCategory == category ? 4u : 0u);
            }
            foreach (PlayableChute3D chute in _chutes)
                chute.SetHighlighted(chute.Category == selectedCategory);
        }

        /// <summary>기존 카메라의 각도와 거리 및 X/Y 주시점을 유지하며 Z 주시점만 이동한다.</summary>
        private void UpdateCameraPosition()
        {
            _camera.transform.position = _cameraTarget - _camera.transform.forward * CAMERA_DISTANCE;
        }

        /// <summary>Z 또는 패드 South로 주변 기물을 최대 6개 집거나, 가까운 chute에 일괄 제출하거나 놓는다.</summary>
        private void Interact()
        {
            if (_held.Count > 0)
            {
                PlayableChute3D chute = FindNearbyChute();
                if (chute != null)
                {
                    List<CastlePiece3D> submitted = new List<CastlePiece3D>(_held);
                    ClearCarryJoints();
                    _held.Clear();
                    _carryDistances.Clear();
                    chute.Submit(submitted);
                    return;
                }
            }

            if (_held.Count < MAX_CARRIED_ITEMS)
            {
                CastlePiece3D target = FindNearbyPickable();
                if (target != null)
                {
                    AddCarriedItem(target);
                    return;
                }
            }
            if (_held.Count > 0) ReleaseBundle();
        }

        /// <summary>입 주변에서 가장 가까운 현재 집기 가능 재활용 오브젝트를 찾는다.</summary>
        private CastlePiece3D FindNearbyPickable()
        {
            Vector3 carryPoint = _beaver.CarryPoint.position;
            Collider[] contacts = Physics.OverlapSphere(carryPoint, _pickupRadius,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float closest = _pickupRadius * _pickupRadius;
            CastlePiece3D chosen = null;
            foreach (Collider contact in contacts)
            {
                CastlePiece3D piece = contact.GetComponentInParent<CastlePiece3D>();
                if (piece == null || !piece.CanPickUp || _held.Contains(piece) ||
                    !PlayableRecyclingRules3D.IsRecycling(piece.MaterialId)) continue;
                float distance = (piece.transform.position - carryPoint).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                chosen = piece;
            }
            return chosen;
        }

        /// <summary>신규 기물을 현재 운반 묶음의 마지막 기물 forward에 정렬하고 고정 조인트로 연결한다.</summary>
        private void AddCarriedItem(CastlePiece3D piece)
        {
            CastlePiece3D previous = _held.Count > 0 ? _held[_held.Count - 1] : null;
            float distance = 0f;
            if (previous != null)
            {
                Vector3 direction = _beaver.transform.forward;
                Collider previousCollider = previous.GetComponent<Collider>();
                Collider currentCollider = piece.GetComponent<Collider>();
                Vector3 prevExtent = previousCollider.bounds.extents;
                Vector3 currExtent = currentCollider.bounds.extents;
                Vector3 absForward = new Vector3(Mathf.Abs(direction.x),
                    Mathf.Abs(direction.y), Mathf.Abs(direction.z));
                float reach = Vector3.Dot(prevExtent + currExtent, absForward);
                distance = _carryDistances[_carryDistances.Count - 1] + reach +
                    CARRIED_ITEM_GAP;
            }

            piece.PickUp();
            piece.transform.rotation = _beaver.transform.rotation;
            piece.MoveHeld(_beaver.CarryPoint.position + _beaver.transform.forward * distance);
            FixedJoint joint = null;
            if (previous != null)
            {
                joint = piece.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = previous.Body;
                joint.breakForce = Mathf.Infinity;
                joint.breakTorque = Mathf.Infinity;
                joint.enableCollision = false;
                joint.enablePreprocessing = false;
            }
            _held.Add(piece);
            _carryDistances.Add(distance);
            _carryJoints.Add(joint);
        }

        /// <summary>운반 묶음의 고정 조인트를 제거해 제출·놓기 시 풀링된 기물에 남지 않도록 한다.</summary>
        private void ClearCarryJoints()
        {
            foreach (FixedJoint joint in _carryJoints)
                if (joint != null)
                {
                    joint.connectedBody = null;
                    Destroy(joint);
                }
            _carryJoints.Clear();
        }

        /// <summary>운반 중인 모든 기물을 제자리에 내려놓고 물리 시뮬레이션을 재개한다.</summary>
        private void ReleaseBundle()
        {
            ClearCarryJoints();
            foreach (CastlePiece3D piece in _held)
                if (piece != null) piece.ReleaseFromBeaver(Vector3.zero);
            _held.Clear();
            _carryDistances.Clear();
        }

        /// <summary>비버에서 수평으로 닿을 수 있는 가장 가까운 분리수거 chute를 찾는다.</summary>
        private PlayableChute3D FindNearbyChute()
        {
            PlayableChute3D nearest = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 position = _beaver.transform.position;
            foreach (PlayableChute3D chute in _chutes)
            {
                if (!chute.CanUse(position)) continue;
                Vector3 offset = chute.InteractionPosition - position;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                nearest = chute;
            }
            return nearest;
        }
    }
}
