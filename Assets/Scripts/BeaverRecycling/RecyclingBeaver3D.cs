using UnityEngine;

namespace BeaverRecycling
{
    public sealed class RecyclingBeaver3D : MonoBehaviour
    {
        private const float KNOCKBACK_DECELERATION = 18f;
        private const float STUN_STAR_ROTATION_SPEED = 360f;

        [Header("Movement")]
        [SerializeField] private Vector2 _minimum = new Vector2(-11.2f, 17f);
        [SerializeField] private Vector2 _maximum = new Vector2(11.2f, 43f);
        [SerializeField] private Transform _carryPoint;
        [SerializeField] private Transform _visual;
        [SerializeField, Min(0f)] private float _knockbackSpinSpeed = 720f;
        [SerializeField] private Transform _stunStar;
        private Vector3 _startingPosition;
        private Vector3 _knockbackVelocity;
        private Quaternion _stunStarStartingRotation;
        private float _stride;

        public Transform CarryPoint => _carryPoint;

        void Awake()
        {
            _startingPosition = transform.position;
            _stunStarStartingRotation = _stunStar.localRotation;
            _stunStar.gameObject.SetActive(false);
        }

        /// <summary>이동 입력, 이동 속도, deltaTime을 사용해 작업 구역 안에서 비버의 위치·방향·걸음 표현을 갱신한다.</summary>
        public void Move(Vector2 input, float speed, float deltaTime)
        {
            Vector2 movement = Vector2.ClampMagnitude(input, 1f);
            Vector3 direction = new Vector3(movement.x, 0f, movement.y);
            Vector3 position = transform.position + direction * speed * deltaTime + _knockbackVelocity * deltaTime;
            float unclampedX = position.x;
            float unclampedZ = position.z;
            position.x = Mathf.Clamp(position.x, _minimum.x, _maximum.x);
            position.z = Mathf.Clamp(position.z, _minimum.y, _maximum.y);
            if (!Mathf.Approximately(position.x, unclampedX)) _knockbackVelocity.x = 0f;
            if (!Mathf.Approximately(position.z, unclampedZ)) _knockbackVelocity.z = 0f;
            _knockbackVelocity = Vector3.MoveTowards(_knockbackVelocity, Vector3.zero,
                KNOCKBACK_DECELERATION * deltaTime);
            transform.position = position;
            if (_knockbackVelocity.sqrMagnitude > 0.01f)
                transform.Rotate(Vector3.up, _knockbackSpinSpeed * deltaTime, Space.World);
            else if (direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 540f * deltaTime);
            AnimateStunStar(deltaTime);
            _stride += deltaTime * speed * 5f;
            _visual.localPosition = Vector3.up * (movement.sqrMagnitude > 0f ? Mathf.Abs(Mathf.Sin(_stride)) * 0.045f : 0f);
        }

        /// <summary>기절 상태를 받아 머리 위 별 이펙트를 켜거나 끈다.</summary>
        public void SetStunned(bool stunned)
        {
            if (_stunStar.gameObject.activeSelf == stunned) return;
            _stunStar.gameObject.SetActive(stunned);
            if (!stunned) _stunStar.localRotation = _stunStarStartingRotation;
        }

        /// <summary>deltaTime을 사용해 활성 상태인 기절 별을 회전시킨다.</summary>
        private void AnimateStunStar(float deltaTime)
        {
            if (!_stunStar.gameObject.activeSelf) return;
            _stunStar.Rotate(Vector3.forward, STUN_STAR_ROTATION_SPEED * deltaTime, Space.Self);
        }

        /// <summary>수평 속도 변화를 받아 작업 경계 안에서 비버의 넉백을 갱신한다.</summary>
        public void ApplyKnockback(Vector3 velocityChange)
        {
            velocityChange.y = 0f;
            _knockbackVelocity += velocityChange;
        }

        /// <summary>출근·재시도 시 시작 위치와 기본 방향으로 비버와 운반 기준점을 복원한다.</summary>
        public void ResetPose()
        {
            _knockbackVelocity = Vector3.zero;
            SetStunned(false);
            transform.SetPositionAndRotation(_startingPosition, Quaternion.identity);
            _visual.localPosition = Vector3.zero;
        }
    }
}
