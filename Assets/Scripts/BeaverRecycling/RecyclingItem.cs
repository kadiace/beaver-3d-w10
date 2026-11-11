using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace BeaverRecycling
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RecyclingItem : MonoBehaviour
    {
        [Header("Wood marker")]
        [SerializeField] private GameObject _woodBadge;
        private readonly Dictionary<RecyclingItem, FixedJoint> _links = new Dictionary<RecyclingItem, FixedJoint>();
        private Rigidbody _body;
        private Collider _collider;
        private Renderer[] _renderers;
        private RecyclingGame3D _game;
        private RecyclingDefinition _definition;
        private Vector3 _originalScale;
        private bool _held;
        private bool _resolved;
        private float _attachAllowedAt;
        private float _lastFlowTime = float.NegativeInfinity;

        public Rigidbody Body => _body;
        public Collider Collider => _collider;
        public RecyclingDefinition Definition => _definition;
        public bool IsHeld => _held;
        public bool IsResolved => _resolved;
        public bool CanJoin => !_resolved && !_held && Time.time >= _attachAllowedAt;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<Collider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalScale = transform.localScale;
        }

        void OnEnable()
        {
            _held = false;
            _resolved = false;
            _attachAllowedAt = 0f;
            _lastFlowTime = float.NegativeInfinity;
            transform.localScale = _originalScale;
            _collider.enabled = true;
            _body.isKinematic = false;
            _body.useGravity = true;
            _woodBadge.SetActive(false);
            SetOutline(0u);
        }

        void OnDisable()
        {
            StopAllCoroutines();
            DetachAll();
            if (_game != null) _game.ForgetItem(this);
            _game = null;
        }

        /// <summary>생성 시 게임과 기물 정의를 받아 이번 생애의 분류·정산 상태를 연결한다.</summary>
        public void Initialize(RecyclingGame3D game, RecyclingDefinition definition)
        {
            _game = game;
            _definition = definition;
            _woodBadge.SetActive(definition.IsWood);
            _body.linearDamping = 0.4f;
            _body.angularDamping = 4f;
            _body.maxDepenetrationVelocity = 2f;
        }

        /// <summary>출발 기물의 실제 연결망을 탐색하여 중복 없는 묶음 목록을 반환한다.</summary>
        public static List<RecyclingItem> Collect(RecyclingItem start)
        {
            var result = new List<RecyclingItem>();
            var pending = new Stack<RecyclingItem>();
            var visited = new HashSet<RecyclingItem>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                RecyclingItem item = pending.Pop();
                if (item == null || item._resolved || !item.isActiveAndEnabled || !visited.Add(item)) continue;
                result.Add(item);
                foreach (RecyclingItem neighbor in item._links.Keys) pending.Push(neighbor);
            }
            return result;
        }

        /// <summary>상대 기물과 접촉 위치를 받아 양방향 그래프와 파단되지 않는 고정 조인트 하나를 만든다.</summary>
        public void Attach(RecyclingItem other, Vector3 worldPoint)
        {
            if (_links.ContainsKey(other)) return;
            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = other.Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = transform.InverseTransformPoint(worldPoint);
            joint.connectedAnchor = other.transform.InverseTransformPoint(worldPoint);
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            _links.Add(other, joint);
            other._links.Add(this, joint);
        }

        /// <summary>이 기물에 닿은 모든 연결 기록과 조인트를 양쪽에서 제거한다.</summary>
        public void DetachAll()
        {
            var snapshot = new List<KeyValuePair<RecyclingItem, FixedJoint>>(_links);
            _links.Clear();
            foreach (var link in snapshot)
            {
                if (link.Key != null) link.Key._links.Remove(this);
                if (link.Value != null) Destroy(link.Value);
            }
        }

        /// <summary>운반 여부를 받아 Rigidbody의 물리 이동 방식을 갱신한다.</summary>
        public void SetHeld(bool held)
        {
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _held = held;
            _body.isKinematic = held;
            _body.useGravity = !held;
        }

        /// <summary>분리 유예 시간을 받아 그 시각까지 재접착을 막는다.</summary>
        public void BlockAttachment(float seconds) => _attachAllowedAt = Time.time + seconds;

        /// <summary>외곽선 렌더링 레이어를 받아 기존 기본 렌더링을 유지하며 강조 색을 바꾼다.</summary>
        public void SetOutline(uint layer)
        {
            foreach (Renderer renderer in _renderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~30u) | (_resolved ? 0u : layer);
        }

        /// <summary>처리 완료 상태를 확정하여 결합·충돌·조작을 해제하고 목적지로 흡수하거나 즉시 반환한다.</summary>
        public void Resolve(Vector3 destination, bool animate)
        {
            if (_resolved) return;
            _resolved = true;
            _held = false;
            DetachAll();
            _collider.enabled = false;
            _body.isKinematic = true;
            SetOutline(0u);
            if (animate) StartCoroutine(Absorb(destination));
            else ReturnNow();
        }

        /// <summary>연출 목적지를 받아 위치와 크기를 보간한 뒤 기물을 풀에 반환한다.</summary>
        private IEnumerator Absorb(Vector3 destination)
        {
            Vector3 start = _body.position;
            float progress = 0f;
            while (progress < 1f)
            {
                progress += Time.unscaledDeltaTime / 0.4f;
                _body.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, progress));
                transform.localScale = _originalScale * Mathf.Lerp(1f, 0.04f, progress);
                yield return null;
            }
            ReturnNow();
        }

        /// <summary>코루틴과 연결을 정리하고 현재 기물을 리소스 풀로 반환한다.</summary>
        public void ReturnNow()
        {
            DetachAll();
            Managers.Resource.Destroy(gameObject);
        }

        void Update()
        {
            if (_game == null || !_game.IsWorking || _held || _resolved) return;
            if (transform.position.z <= _game.WaterfallZ) _game.ResolveExit(this, true, true);
            else if (transform.position.y < _game.FallbackY || Mathf.Abs(transform.position.x) > 25f)
                _game.ResolveExit(this, false, true);
        }

        void FixedUpdate()
        {
            if (_game == null || !_game.IsWorking || _held || _resolved || _lastFlowTime == Time.fixedTime) return;
            List<RecyclingItem> group = Collect(this);
            bool touchesWater = false;
            foreach (RecyclingItem member in group)
                touchesWater |= _game.IsInsideRiver(member._body.position) && member._collider.bounds.min.y <= _game.WaterY + 0.2f;
            foreach (RecyclingItem member in group)
            {
                member._lastFlowTime = Time.fixedTime;
                if (member._held || member._body.isKinematic) continue;
                Vector3 velocity = member._body.linearVelocity;
                velocity.x *= 0.9f;
                velocity.z = touchesWater ? -_game.FlowSpeed : 0f;
                if (_game.IsInsideRiver(member._body.position) && member._collider.bounds.min.y <= _game.WaterY + 0.2f)
                {
                    float centerY = _game.WaterY + member._collider.bounds.extents.y * 0.25f;
                    velocity.y = Mathf.Clamp((centerY - member._collider.bounds.center.y) * 5f, -3f, 3f);
                    member._body.AddForce(-Physics.gravity, ForceMode.Acceleration);
                }
                member._body.linearVelocity = velocity;
            }
        }
    }
}
