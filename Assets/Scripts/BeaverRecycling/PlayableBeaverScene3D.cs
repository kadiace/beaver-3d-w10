using UnityEngine;
using UnityEngine.InputSystem;

using RestoreCastle3D;

namespace BeaverRecycling
{
    /// <summary>W11 비버를 W12 연속 스폰 하천에서 조종하고 기존 카메라 각도로 추적한다.</summary>
    public sealed class PlayableBeaverScene3D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float _pickupRadius = 2.4f;

        [Header("Camera follow")]
        [SerializeField, Min(0f)] private float _lookAheadDistance = 3f;
        [SerializeField, Min(0.1f)] private float _followSharpness = 6f;

        private RecyclingBeaver3D _beaver;
        private PlayableChute3D[] _chutes;
        private CastlePiece3D _heldPiece;
        private Camera _camera;
        private Vector2 _movement;
        private Vector3 _cameraTarget;

        private void Start()
        {
            _beaver = FindFirstObjectByType<RecyclingBeaver3D>();
            _chutes = FindObjectsByType<PlayableChute3D>(FindObjectsSortMode.None);
            _camera = Camera.main;
            GetComponent<CastleGame3D>().PlayerCameraFollowEnabled = true;
            _cameraTarget = new Vector3(_beaver.transform.position.x, 0f, _beaver.transform.position.z);
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
            _beaver.Move(_movement, _moveSpeed, Time.fixedDeltaTime);
            if (_heldPiece != null) _heldPiece.MoveHeld(_beaver.CarryPoint.position);
        }

        private void LateUpdate()
        {
            Vector3 position = _beaver.transform.position;
            Vector3 ahead = new Vector3(_movement.x, 0f, _movement.y) * _lookAheadDistance;
            Vector3 target = new Vector3(position.x, 0f, position.z) + ahead;
            float blend = 1f - Mathf.Exp(-_followSharpness * Time.unscaledDeltaTime);
            _cameraTarget = Vector3.Lerp(_cameraTarget, target, blend);
            _cameraTarget.x = Mathf.Clamp(_cameraTarget.x, -9f, 9f);
            _cameraTarget.z = Mathf.Clamp(_cameraTarget.z, 19f, 41f);
            UpdateCameraPosition();
        }

        /// <summary>W12 기본 카메라의 회전과 줌은 그대로 두고 비버 주시점만 갱신한다.</summary>
        private void UpdateCameraPosition()
        {
            _camera.transform.position = _cameraTarget - _camera.transform.forward * 20f;
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
