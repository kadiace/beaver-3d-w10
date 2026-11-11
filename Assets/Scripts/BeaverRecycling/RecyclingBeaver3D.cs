using UnityEngine;

namespace BeaverRecycling
{
    public sealed class RecyclingBeaver3D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private Vector2 _minimum = new Vector2(-11.2f, 17f);
        [SerializeField] private Vector2 _maximum = new Vector2(11.2f, 43f);
        [SerializeField] private Transform _carryPoint;
        [SerializeField] private Transform _visual;
        private Vector3 _startingPosition;
        private float _stride;

        public Transform CarryPoint => _carryPoint;

        void Awake()
        {
            _startingPosition = transform.position;
        }

        /// <summary>이동 입력과 속도를 받아 작업 구역 안에서 비버의 위치·방향·걸음 표현을 갱신한다.</summary>
        public void Move(Vector2 input, float speed, float deltaTime)
        {
            Vector2 movement = Vector2.ClampMagnitude(input, 1f);
            Vector3 direction = new Vector3(movement.x, 0f, movement.y);
            Vector3 position = transform.position + direction * speed * deltaTime;
            position.x = Mathf.Clamp(position.x, _minimum.x, _maximum.x);
            position.z = Mathf.Clamp(position.z, _minimum.y, _maximum.y);
            transform.position = position;
            if (direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 540f * deltaTime);
            _stride += deltaTime * speed * 5f;
            _visual.localPosition = Vector3.up * (movement.sqrMagnitude > 0f ? Mathf.Abs(Mathf.Sin(_stride)) * 0.045f : 0f);
        }

        /// <summary>출근·재시도 시 시작 위치와 기본 방향으로 비버와 운반 기준점을 복원한다.</summary>
        public void ResetPose()
        {
            transform.SetPositionAndRotation(_startingPosition, Quaternion.identity);
            _visual.localPosition = Vector3.zero;
        }
    }
}
