using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

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

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 4.5f;
        [SerializeField, Min(1f)] private float _swimSpeedMultiplier = 1.6f;
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
        private CastlePiece3D _heldPiece;
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
        }

        private void FixedUpdate()
        {
            float speed = _moveSpeed * (_isSwimming ? _swimSpeedMultiplier : 1f);
            _beaver.Move(_movement, speed, Time.fixedDeltaTime);
            UpdateSurfaceHeightAndSwimming();
            if (_heldPiece != null) _heldPiece.MoveHeld(_beaver.CarryPoint.position);
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
            bool climbingBank = inRiver &&
                TryGetBankContactHeight(position, riverBounds, out float bankSurfaceY);
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

            if (_isSwimming == swimming) return;
            _isSwimming = swimming;
            _beaver.SetCarriedItemEffect(_swimmingEyeTrailCount, swimming ? 1f : 0f);
        }

        /// <summary>기존 카메라의 각도와 거리 및 X/Y 주시점을 유지하며 Z 주시점만 이동한다.</summary>
        private void UpdateCameraPosition()
        {
            _camera.transform.position = _cameraTarget - _camera.transform.forward * CAMERA_DISTANCE;
        }

        /// <summary>Z 또는 패드 South로 가까운 기물을 집거나, 투입구에 제출하거나, 바닥에 내려놓는다.</summary>
        private void Interact()
        {
            if (_heldPiece != null)
            {
                PlayableChute3D chute = FindNearbyChute();
                CastlePiece3D held = _heldPiece;
                _heldPiece = null;
                if (chute != null)
                    chute.Submit(held);
                else
                    held.ReleaseFromBeaver(Vector3.zero);
                return;
            }

            Vector3 carryPoint = _beaver.CarryPoint.position;
            Collider[] contacts = Physics.OverlapSphere(carryPoint, _pickupRadius,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float closest = _pickupRadius * _pickupRadius;
            CastlePiece3D chosen = null;
            foreach (Collider contact in contacts)
            {
                CastlePiece3D piece = contact.GetComponentInParent<CastlePiece3D>();
                if (piece == null || !piece.CanPickUp) continue;
                float sqrDistance = (piece.transform.position - carryPoint).sqrMagnitude;
                if (sqrDistance >= closest) continue;
                closest = sqrDistance;
                chosen = piece;
            }
            if (chosen == null) return;
            chosen.PickUp();
            _heldPiece = chosen;
            chosen.MoveHeld(carryPoint);
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
