using UnityEngine;

namespace BeaverRecycling
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BombExplosion3D : MonoBehaviour
    {
        [Header("Explosion")]
        [SerializeField, Min(0.1f)] private float _explosionRadius = 3f;
        [SerializeField, Min(0f)] private float _itemKnockbackSpeed = 5f;
        [SerializeField, Min(0f)] private float _beaverKnockbackSpeed = 6f;
        [SerializeField, Min(0f)] private float _stunDuration = 1f;

        private Rigidbody _body;
        private BoxCollider _collider;
        private RecyclingGame3D _game;
        private Vector3 _knockbackVelocity;
        private bool _exploded;

        public float ExplosionRadius => _explosionRadius;
        public float ItemKnockbackSpeed => _itemKnockbackSpeed;
        public float BeaverKnockbackSpeed => _beaverKnockbackSpeed;
        public float StunDuration => _stunDuration;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<BoxCollider>();
        }

        void OnEnable()
        {
            _exploded = false;
            _knockbackVelocity = Vector3.zero;
        }

        void OnDisable()
        {
            if (_game != null) _game.ForgetBomb(this);
            _game = null;
        }

        /// <summary>활성화한 게임을 받아 물리 상태를 초기화하고 폭탄 이동을 연결한다.</summary>
        public void Initialize(RecyclingGame3D game)
        {
            _game = game;
            _exploded = false;
            _knockbackVelocity = Vector3.zero;
            _collider.enabled = true;
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.linearDamping = 0.4f;
            _body.angularDamping = 4f;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.maxDepenetrationVelocity = 2f;
        }

        /// <summary>폭발 중복 여부를 확인하고 폭발 종료를 위해 물리 충돌을 비활성화한다.</summary>
        public bool TryBeginExplosion()
        {
            if (_exploded) return false;
            _exploded = true;
            _collider.enabled = false;
            _body.isKinematic = true;
            return true;
        }

        /// <summary>폭발 속도 변화를 받아 다른 폭발 없이 폭탄의 수평 이동에 반영한다.</summary>
        public void ApplyKnockback(Vector3 velocityChange)
        {
            if (_exploded) return;
            velocityChange.y = 0f;
            _knockbackVelocity += velocityChange;
        }

        /// <summary>폭발 또는 강 이탈 후 폭탄 오브젝트를 리소스 풀에 반환한다.</summary>
        public void ReturnNow()
        {
            Managers.Resource.Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            if (_game == null || !_game.IsWorking) return;
            if (other.GetComponentInParent<RecyclingBeaver3D>() == null) return;
            _game.ExplodeBomb(this);
        }

        void Update()
        {
            if (_game == null || !_game.IsWorking || _exploded) return;
            if (transform.position.z <= _game.WaterfallZ ||
                transform.position.y < _game.FallbackY || Mathf.Abs(transform.position.x) > 25f)
                _game.ResolveBombExit(this);
        }

        void FixedUpdate()
        {
            if (_game == null || !_game.IsWorking || _exploded) return;

            bool touchesWater = _game.IsInsideRiver(_body.position) &&
                _collider.bounds.min.y <= _game.WaterY + 0.2f;
            Vector3 velocity = _body.linearVelocity;
            Vector3 knockbackVelocity = _knockbackVelocity;
            _knockbackVelocity = Vector3.MoveTowards(_knockbackVelocity, Vector3.zero,
                18f * Time.fixedDeltaTime);
            velocity.x = velocity.x * 0.9f + knockbackVelocity.x;
            velocity.z = (touchesWater ? -_game.FlowSpeed : 0f) + knockbackVelocity.z;
            if (touchesWater)
            {
                float centerY = _game.WaterY + _collider.bounds.extents.y * 0.25f;
                velocity.y = Mathf.Clamp((centerY - _collider.bounds.center.y) * 5f, -3f, 3f);
                _body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            }
            _body.linearVelocity = velocity;
        }
    }
}
