using UnityEngine;

namespace BeaverRecycling
{
    public sealed class RecyclingBeaver3D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private Vector2 _minimum = new Vector2(-12f, 17f);
        [SerializeField] private Vector2 _maximum = new Vector2(12f, 43f);
        [SerializeField] private Transform _carryPoint;
        [SerializeField] private Transform _visual;
        [SerializeField, Min(0f)] private float _knockbackSpinSpeed = 720f;

        [Header("Stun effect")]
        [SerializeField] private Transform _stunStar;
        [SerializeField, Min(0f)] private float _stunStarOrbitRadius = 0.55f;
        [SerializeField, Min(0f)] private float _stunStarOrbitSpeed = 180f;
        private Vector3 _startingPosition;
        private Vector3 _knockbackVelocity;
        private float _knockbackTimeRemaining;
        private Vector3 _stunStarCenterLocalPosition;
        private Quaternion _stunStarStartingRotation;
        private Transform _stunCamera;
        private float _stunStarOrbitAngle;
        private float _stride;

        public Transform CarryPoint => _carryPoint;

        void Awake()
        {
            _startingPosition = transform.position;
            _stunStarCenterLocalPosition = _stunStar.localPosition;
            _stunStarStartingRotation = _stunStar.localRotation;
            _stunCamera = Camera.main.transform;
            _stunStar.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            AnimateStunStar(Time.deltaTime);
        }

        /// <summary>이동 입력, 이동 속도, deltaTime을 사용해 작업 구역 안에서 비버의 위치·방향·걸음 표현을 갱신한다.</summary>
        public void Move(Vector2 input, float speed, float deltaTime)
        {
            Vector2 movement = Vector2.ClampMagnitude(input, 1f);
            Vector3 direction = new Vector3(movement.x, 0f, movement.y);
            float knockbackStep = Mathf.Min(deltaTime, _knockbackTimeRemaining);
            float knockbackFraction = _knockbackTimeRemaining > 0f ? knockbackStep / _knockbackTimeRemaining : 0f;
            Vector3 knockbackDisplacement = _knockbackVelocity * (knockbackStep * (1f - 0.5f * knockbackFraction));
            _knockbackVelocity *= 1f - knockbackFraction;
            _knockbackTimeRemaining -= knockbackStep;
            Vector3 position = transform.position + direction * speed * deltaTime + knockbackDisplacement;
            float unclampedX = position.x;
            float unclampedZ = position.z;
            position.x = Mathf.Clamp(position.x, _minimum.x, _maximum.x);
            position.z = Mathf.Clamp(position.z, _minimum.y, _maximum.y);
            if (!Mathf.Approximately(position.x, unclampedX)) _knockbackVelocity.x = 0f;
            if (!Mathf.Approximately(position.z, unclampedZ)) _knockbackVelocity.z = 0f;
            transform.position = position;
            if (knockbackDisplacement.sqrMagnitude > 0.000001f)
                transform.Rotate(Vector3.up, _knockbackSpinSpeed * deltaTime, Space.World);
            else if (direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 540f * deltaTime);
            _stride += deltaTime * speed * 5f;
            _visual.localPosition = Vector3.up * (movement.sqrMagnitude > 0f ? Mathf.Abs(Mathf.Sin(_stride)) * 0.045f : 0f);
        }

        /// <summary>기절 상태를 받아 머리 위 별 이펙트를 켜거나 끈다.</summary>
        public void SetStunned(bool stunned)
        {
            if (_stunStar.gameObject.activeSelf == stunned) return;
            _stunStar.gameObject.SetActive(stunned);
            if (stunned) AnimateStunStar(0f);
            else
            {
                _stunStarOrbitAngle = 0f;
                _stunStar.localPosition = _stunStarCenterLocalPosition;
                _stunStar.localRotation = _stunStarStartingRotation;
            }
        }

        /// <summary>deltaTime을 사용해 기절 별을 머리 위에서 공전시키고 카메라 방향으로 정렬한다.</summary>
        private void AnimateStunStar(float deltaTime)
        {
            if (!_stunStar.gameObject.activeSelf) return;
            _stunStarOrbitAngle = (_stunStarOrbitAngle + _stunStarOrbitSpeed * deltaTime) % 360f;
            float angle = _stunStarOrbitAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _stunStarOrbitRadius;
            _stunStar.position = transform.TransformPoint(_stunStarCenterLocalPosition) + offset;
            _stunStar.rotation = _stunCamera.rotation;
        }

        /// <summary>수평 속도 변화와 지속 시간을 받아 비버의 넉백 속도와 남은 시간을 갱신한다.</summary>
        public void ApplyKnockback(Vector3 velocityChange, float duration)
        {
            if (duration <= 0f) return;
            velocityChange.y = 0f;
            _knockbackVelocity += velocityChange;
            _knockbackTimeRemaining = duration;
        }

        /// <summary>출근·재시도 시 시작 위치와 기본 방향으로 비버와 운반 기준점을 복원한다.</summary>
        public void ResetPose()
        {
            _knockbackVelocity = Vector3.zero;
            _knockbackTimeRemaining = 0f;
            SetStunned(false);
            transform.SetPositionAndRotation(_startingPosition, Quaternion.identity);
            _visual.localPosition = Vector3.zero;
        }
    }
}
