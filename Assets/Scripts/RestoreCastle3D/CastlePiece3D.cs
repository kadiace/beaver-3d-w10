using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CastlePiece3D : MonoBehaviour
    {
        private const float CREEK_SPEED = 3f;
        private const float SPRING_STRENGTH = 10000f;
        private const float SPRING_DAMPER = 500f;
        private const float SPRING_DISTANCE = 0f;
        private const float SPRING_TOLERANCE = 0f;
        private const float SPRING_BREAK_FORCE = 10000f;
        private const float SPRING_BREAK_TORQUE = 10000f;
        private const float SPRING_BREAK_DISTANCE = 0.08f;
        private const float BARREL_FUSE_SECONDS = 1f;
        private const float BARREL_BLAST_RADIUS = 2f;
        private const float BARREL_BLAST_IMPULSE = 30f;
        private const float BLAST_DEBRIS_LIFETIME = 2f;
        private const float DESPAWN_Z = -15f;
        private const float DESPAWN_Y = -10f;

        [SerializeField] private string materialId;

        private readonly HashSet<CastlePiece3D> _contacts = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _attachedPieces = new HashSet<CastlePiece3D>();
        private CastlePiece3D _attachmentTarget;
        private CastlePiece3D _springTarget;
        private SpringJoint _springJoint;
        private float _springDistanceLimit;
        private CastleGame3D _game;
        private Rigidbody _body;
        private Renderer[] _renderers;
        private bool _fuseStarted;
        private bool _fusePending;
        private int _growthCount;
        private bool _continuousDrop;
        private bool _continuousBeforeSpring;
        private bool _thrown;
        private bool _throwOrigin;
        private bool _held;
        private bool _removalScheduled;
        private float _blastDespawnAt = -1f;
        private float _attachBlockedUntil;
        private bool _settling;
        private bool _originalDetectCollisions;
        private readonly Dictionary<Collider, bool> _settlementColliders = new Dictionary<Collider, bool>();
        private int _initialChildCount;
        private Vector3 _initialColliderCenter;
        private Vector3 _initialColliderSize;
        private Vector3 _initialScale;

        public string MaterialId => materialId;
        public Rigidbody Body => _body;
        public IEnumerable<CastlePiece3D> Contacts => _contacts;
        public IEnumerable<CastlePiece3D> AttachedPieces => _attachedPieces;
        public bool HasAttachmentTarget => _attachmentTarget != null;
        public bool IsVine => materialId == "growing_vine" || materialId == "vine";
        public bool AbilitySuppressed { get; private set; }
        public bool FuseStarted => _fuseStarted;
        public bool FusePending => _fusePending;
        public int PlacedTurn { get; private set; }
        public int PermanentBonus { get; set; }
        public int GrowthCount { get => _growthCount; set => _growthCount = value; }
        public bool CanPickUp => !_settling && !_thrown && !_body.isKinematic && _attachedPieces.Count == 0;
        public bool WasThrown => _thrown;
        /// <summary>Mirror the collision attachment eligibility for a held-piece preview.</summary>
        public bool CanAttachWhenThrownTo(CastlePiece3D other)
        {
            if (other == null || other == this || materialId == "barrel" ||
                other.materialId == "barrel" || _settling || other._settling ||
                Time.time < _attachBlockedUntil || Time.time < other._attachBlockedUntil ||
                _attachedPieces.Count != 0) return false;
            return (!other._thrown && other._attachedPieces.Count == 0) ||
                (other._thrown && other._attachedPieces.Count != 0 && _springTarget == null);
        }
        public bool IsSettling => _settling;
        /// <summary>Finalize scoring, but keep physics and joints intact for the cliff fall.</summary>
        public void MarkSettling()
        {
            if (_settling) return;
            _settling = true;
            _removalScheduled = true;
            StopAllCoroutines();
            _held = false;
            // Do not reset velocities, gravity, Rigidbody state, colliders or joints.
            // Score/hover/pickup/joint creation are independently blocked by IsSettling.
            SetOutlineLayer(0);
            SetPenaltyOutline(false);
        }
        public void BlockReattachment(float seconds) =>
            _attachBlockedUntil = Mathf.Max(_attachBlockedUntil, Time.time + seconds);

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _originalDetectCollisions = _body.detectCollisions;
            _initialChildCount = transform.childCount;
            _initialScale = transform.localScale;
            if (TryGetComponent(out BoxCollider box))
            {
                _initialColliderCenter = box.center;
                _initialColliderSize = box.size;
            }
            _renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true),
                renderer => renderer.gameObject.name != "Penalty Range");
            SetOutlineLayer(0);
        }

        /// <summary>자식 렌더러의 외곽선 레이어만 교체하고 기본 렌더링 레이어는 보존한다.</summary>
        public void SetOutlineLayer(uint layer)
        {
            foreach (Renderer renderer in _renderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~30u) |
                    (_settling ? 0u :
                     layer != 0 ? layer : _springTarget != null ? 16u : _throwOrigin ? 4u : 0u);
        }

        /// <summary>드랍 가이드의 폐기물 디메리트 대상 여부에 따라 빨간 외곽선 레이어를 켜거나 끈다.</summary>
        public void SetPenaltyOutline(bool enabled)
        {
            enabled &= !_settling;
            foreach (Renderer renderer in _renderers)
                renderer.renderingLayerMask = enabled
                    ? renderer.renderingLayerMask | 8u
                    : renderer.renderingLayerMask & ~8u;
        }

        /// <summary>생성된 기물에 게임 소유자 및 설치된 턴을 연결한다.</summary>
        public void Initialize(CastleGame3D game, int turn, string id = null)
        {
            _game = game;
            PlacedTurn = turn;
            if (id != null) materialId = id;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        /// <summary>자동 낙하 모드로 전환하여 기존 충돌 능력을 비활성화하고 중력을 켠다.</summary>
        public void EnableContinuousDrop()
        {
            if (_settling) return;
            _continuousDrop = true;
            _body.isKinematic = false;
            _body.useGravity = true;
        }

        /// <summary>기물을 들어 올려 중력과 물리 이동을 잠시 멈춘다.</summary>
        public void PickUp()
        {
            if (_settling) return;
            _held = true;
            _continuousDrop = false;
            _removalScheduled = false;
            StopAllCoroutines();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        /// <summary>커서의 XZ 좌표를 따라 기물을 옮기고 물리 충돌 좌표를 동기화한다.</summary>
        public void MoveHeld(Vector3 position)
        {
            if (_settling) return;
            _body.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>집고 있던 기물을 수평 속도 없이 중력으로 놓고 이후 다시 집지 못하도록 한다.</summary>
        public void Throw()
        {
            if (_settling) return;
            _held = false;
            _throwOrigin = true;
            MarkAttachmentGroupThrown();
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        private void Update()
        {
            // Cleanup must run even while gameplay is disabled and coroutines are stopped.
            if (_blastDespawnAt >= 0f)
            {
                if (Time.unscaledTime >= _blastDespawnAt)
                {
                    _blastDespawnAt = -1f;
                    Managers.Resource.Destroy(gameObject);
                }
                return;
            }
            if (_settling) return;
            // The scoring boundary wins over the Y fallback if both are crossed this frame.
            if (transform.position.z <= DESPAWN_Z && !_removalScheduled && !_held)
            {
                if (CastleCreekScore3D.Active != null)
                {
                    CastleCreekScore3D.Active.Settle(this);
                    return;
                }
                if (_continuousDrop)
                {
                    _removalScheduled = true;
                    StartCoroutine(ReturnContinuousDrop());
                    return;
                }
            }
            if (transform.position.y < DESPAWN_Y)
            {
                if (CastleCreekScore3D.Active != null)
                    CastleCreekScore3D.Active.Discard(this);
                else Despawn();
            }
        }

        private void FixedUpdate()
        {
            if (_settling) return;
            if (_springJoint == null || _springTarget == null || _springDistanceLimit <= 0f) return;
            Vector3 anchor = transform.TransformPoint(_springJoint.anchor);
            Vector3 connectedAnchor = _springTarget.transform.TransformPoint(_springJoint.connectedAnchor);
            if ((anchor - connectedAnchor).sqrMagnitude <= _springDistanceLimit * _springDistanceLimit) return;
            Destroy(_springJoint);
            RemoveSpringAttachment();
        }

        /// <summary>배치 기물은 게임의 추적 목록에서 제거하고 자동 낙하 기물은 리소스 풀로 반환한다.</summary>
        public void Despawn()
        {
            if (_settling) return;
            if (CastleCreekScore3D.Active != null)
            {
                CastleCreekScore3D.Active.Discard(this);
                return;
            }
            if (_game != null) _game.RemovePiece(this);
            else Managers.Resource.Destroy(gameObject);
        }

        /// <summary>하천을 벗어난 자동 낙하 기물을 1초 뒤 리소스 풀로 반환한다.</summary>
        private IEnumerator ReturnContinuousDrop()
        {
            yield return new WaitForSeconds(1f);
            Managers.Resource.Destroy(gameObject);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            RemoveSpringAttachment();
            foreach (CastlePiece3D other in new List<CastlePiece3D>(_attachedPieces))
            {
                if (other == null) continue;
                if (other._springTarget == this) other.RemoveSpringAttachment();
                else other._attachedPieces.Remove(this);
            }
            foreach (CastlePiece3D other in _contacts)
                if (other != null) other._contacts.Remove(this);
            _attachedPieces.Clear();
            _contacts.Clear();
            _attachmentTarget = null;
            _springTarget = null;
            _springJoint = null;
            foreach (FixedJoint joint in GetComponents<FixedJoint>())
            {
                joint.connectedBody = null;
                Destroy(joint);
            }
            foreach (SpringJoint joint in GetComponents<SpringJoint>())
            {
                joint.connectedBody = null;
                Destroy(joint);
            }
            for (int i = transform.childCount - 1; i >= _initialChildCount; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                child.transform.SetParent(null);
                Destroy(child);
            }
            if (TryGetComponent(out BoxCollider box))
            {
                box.center = _initialColliderCenter;
                box.size = _initialColliderSize;
            }
            CastleCreekScore3D.Active?.Unregister(this);
            foreach (var pair in _settlementColliders)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _settlementColliders.Clear();
            _body.detectCollisions = _originalDetectCollisions;
            _game = null;
            _attachBlockedUntil = 0f;
            _settling = false;
            _fuseStarted = false;
            _fusePending = false;
            _growthCount = 0;
            _continuousDrop = false;
            _continuousBeforeSpring = false;
            _thrown = false;
            _throwOrigin = false;
            _held = false;
            _removalScheduled = false;
            _blastDespawnAt = -1f;
            AbilitySuppressed = false;
            PermanentBonus = 0;
            PlacedTurn = 0;
            transform.localScale = _initialScale;
            Transform penaltyRange = transform.Find("Penalty Range");
            if (penaltyRange != null) penaltyRange.gameObject.SetActive(false);
            SetOutlineLayer(0);
            SetPenaltyOutline(false);
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_settling || _continuousDrop) return;
            AttachThrownPiece(collision);
            Register(collision);
            if (materialId == "barrel" && _throwOrigin && !_fuseStarted &&
                (collision.collider.GetComponentInParent<CastlePiece3D>() != null ||
                 collision.gameObject.name == "Flat Muddy Creek Bed" ||
                 collision.gameObject.name == "Upper Flat Muddy Creek Bed" ||
                 collision.gameObject.name == "Flat Muddy Creek Bed After Cliff"))
            {
                _fuseStarted = true;
                _fusePending = true;
                StartCoroutine(Detonate());
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (_settling) return;
            if ((_continuousDrop || _thrown) &&
                (collision.gameObject.name == "Flat Muddy Creek Bed" ||
                 collision.gameObject.name == "Upper Flat Muddy Creek Bed") && !_body.isKinematic)
            {
                ApplyCreekFlow();
            }
            if (_continuousDrop) return;
            AttachThrownPiece(collision);
            Register(collision);
        }

        /// <summary>하천에 닿은 기물과 스프링으로 연결된 기물에 같은 -Z 속도를 적용한다.</summary>
        private void ApplyCreekFlow()
        {
            CastlePiece3D origin = _springTarget != null ? _springTarget : this;
            Vector3 velocity = origin._body.linearVelocity;
            if (origin._settling) return;
            if (!origin._body.isKinematic)
                origin._body.linearVelocity = new Vector3(0f, velocity.y, -CREEK_SPEED);

            foreach (CastlePiece3D attached in origin._attachedPieces)
            {
                if (attached == null || attached._springTarget != origin || attached._body.isKinematic)
                    continue;
                velocity = attached._body.linearVelocity;
                attached._body.linearVelocity = new Vector3(0f, velocity.y, -CREEK_SPEED);
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (_settling || _continuousDrop) return;
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this) return;
            _contacts.Remove(other);
            other._contacts.Remove(this);
        }

        private void OnDestroy()
        {
            foreach (CastlePiece3D other in _attachedPieces)
                if (other != null) other._attachedPieces.Remove(this);
            _attachedPieces.Clear();
            foreach (CastlePiece3D other in _contacts)
                if (other != null) other._contacts.Remove(this);
            _contacts.Clear();
        }

        private void OnJointBreak(float breakForce)
        {
            if (_settling) return;
            RemoveSpringAttachment();
        }

        /// <summary>스프링 파단 시 부착 기록과 재집기 상태를 일반 기물 상태로 복구한다.</summary>
        private void RemoveSpringAttachment()
        {
            if (_springTarget == null) return;
            CastlePiece3D previous = _springTarget;
            _springTarget._attachedPieces.Remove(this);
            _attachedPieces.Remove(_springTarget);
            _springTarget = null;
            _springJoint = null;
            _thrown = false;
            _continuousDrop = _continuousBeforeSpring;
            _continuousBeforeSpring = false;
            SetOutlineLayer(0);
            if (CastleCreekScore3D.Active != null && !_settling && !previous._settling)
                CastleCreekScore3D.Active.Refresh(this, previous);
        }

        /// <summary>충돌한 두 기물을 양방향 접촉 그래프에 기록한다. 바닥 충돌은 제외한다.</summary>
        private void Register(Collision collision)
        {
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this || other._settling ||
                materialId == "barrel" || other.materialId == "barrel") return;
            _contacts.Add(other);
            other._contacts.Add(this);
            if (_thrown || other._thrown) return;
            if (CastleCreekScore3D.Active != null) return;
            _game?.TryAttachVines(this, other);
            _game?.TryAttachMud(this, other);
        }

        /// <summary>직접 던진 기물에만 아직 부착되지 않은 다른 기물을 스프링으로 결합한다.</summary>
        private void AttachThrownPiece(Collision collision)
        {
            if (!_throwOrigin || _held || _settling || materialId == "barrel" ||
                Time.time < _attachBlockedUntil) return;
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this || other._settling ||
                other.materialId == "barrel" ||
                Time.time < other._attachBlockedUntil || _attachedPieces.Contains(other)) return;

            // A newly thrown piece can join any existing attached chunk, not only a barrel.
            // Add one Joint to the collided member: the entire connected graph becomes one chunk.
            bool joinsExistingChunk = other._thrown && other._attachedPieces.Count != 0 &&
                _springTarget == null && _attachedPieces.Count == 0;
            if (joinsExistingChunk)
            {
                SpringJoint link = gameObject.AddComponent<SpringJoint>();
                link.connectedBody = other.Body;
                link.autoConfigureConnectedAnchor = false;
                link.anchor = transform.InverseTransformPoint(collision.GetContact(0).point);
                link.connectedAnchor = other.transform.InverseTransformPoint(collision.GetContact(0).point);
                ConfigureSpring(link);
                _springTarget = other;
                _springJoint = link;
                _springDistanceLimit = SPRING_BREAK_DISTANCE;
                _attachedPieces.Add(other);
                other._attachedPieces.Add(this);
                CastleCreekScore3D.Active?.Refresh(this, other);
                return;
            }
            if (other._thrown || other._attachedPieces.Count != 0) return;
            other._continuousBeforeSpring = other._continuousDrop;
            if (other._continuousDrop)
            {
                other._continuousDrop = false;
                other._removalScheduled = false;
                other.StopAllCoroutines();
            }
            other._body.isKinematic = false;
            other._body.useGravity = true;
            SpringJoint joint = other.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = _body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = other.transform.InverseTransformPoint(collision.GetContact(0).point);
            joint.connectedAnchor = transform.InverseTransformPoint(collision.GetContact(0).point);
            ConfigureSpring(joint);
            other._springTarget = this;
            other._springJoint = joint;
            other._springDistanceLimit = SPRING_BREAK_DISTANCE;
            _attachedPieces.Add(other);
            other._attachedPieces.Add(this);
            other.MarkAttachmentGroupThrown();
            CastleCreekScore3D.Active?.Refresh(this, other);
        }

        private static void ConfigureSpring(SpringJoint joint)
        {
            joint.spring = SPRING_STRENGTH;
            joint.damper = SPRING_DAMPER;
            joint.minDistance = SPRING_DISTANCE;
            joint.maxDistance = SPRING_DISTANCE;
            joint.tolerance = SPRING_TOLERANCE;
            joint.breakForce = SPRING_BREAK_FORCE;
            joint.breakTorque = SPRING_BREAK_TORQUE;
            joint.enableCollision = true;
        }

        /// <summary>스프링과 기존 고정 조인트로 연결된 모든 기물을 재투척 불가 상태로 만든다.</summary>
        private void MarkAttachmentGroupThrown()
        {
            var pending = new Stack<CastlePiece3D>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                CastlePiece3D piece = pending.Pop();
                if (piece == null || piece._thrown) continue;
                piece._thrown = true;
                piece.SetOutlineLayer(0);
                foreach (CastlePiece3D attached in piece._attachedPieces)
                    pending.Push(attached);
            }
        }

        /// <summary>폭발 대상의 스프링·고정 조인트와 부착 그래프를 모두 분리한다.</summary>
        public void BreakAllAttachments()
        {
            if (_settling) return;
            foreach (CastlePiece3D other in new List<CastlePiece3D>(_attachedPieces))
            {
                if (other == null) continue;
                CastlePiece3D child = _springTarget == other ? this :
                    other._springTarget == this ? other : null;
                if (child == null) continue;
                if (child._springJoint != null)
                {
                    child._springJoint.connectedBody = null;
                    Destroy(child._springJoint);
                }
                child.RemoveSpringAttachment();
            }

            foreach (CastlePiece3D other in new List<CastlePiece3D>(_attachedPieces))
            {
                if (other == null) continue;
                foreach (FixedJoint joint in GetComponents<FixedJoint>())
                {
                    if (joint.connectedBody != other.Body) continue;
                    joint.connectedBody = null;
                    Destroy(joint);
                }
                foreach (FixedJoint joint in other.GetComponents<FixedJoint>())
                {
                    if (joint.connectedBody != _body) continue;
                    joint.connectedBody = null;
                    Destroy(joint);
                }
                _attachedPieces.Remove(other);
                other._attachedPieces.Remove(this);
                if (_attachmentTarget == other) _attachmentTarget = null;
                if (other._attachmentTarget == this) other._attachmentTarget = null;
                if (CastleCreekScore3D.Active != null && !other._settling)
                    CastleCreekScore3D.Active.Refresh(other);
            }
        }

        /// <summary>오크통 중심 2m의 기물마다 고정 30 임펄스를 주고 2초 뒤 제거하도록 만든다.</summary>
        public List<CastlePiece3D> ExplodeNearby()
        {
            var targets = new HashSet<CastlePiece3D>();
            Vector3 origin = _body.worldCenterOfMass;
            foreach (Collider collider in Physics.OverlapSphere(origin, BARREL_BLAST_RADIUS,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                CastlePiece3D target = collider.GetComponentInParent<CastlePiece3D>();
                if (target == null || target == this || target._settling ||
                    target.materialId == "foundation" || !target.isActiveAndEnabled)
                    continue;
                targets.Add(target);
            }

            foreach (CastlePiece3D target in targets)
                target.BreakAllAttachments();
            foreach (CastlePiece3D target in targets)
            {
                Vector3 direction = target._body.worldCenterOfMass - origin;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
                else direction.y = Mathf.Max(direction.y, 0.5f);
                target.BeginBlastDebris();
                target._body.AddForce(direction.normalized * BARREL_BLAST_IMPULSE,
                    ForceMode.Impulse);
            }
            return new List<CastlePiece3D>(targets);
        }

        /// <summary>점수·상호작용·특수 효과를 중단하되 Rigidbody 물리 시뮬레이션은 유지한다.</summary>
        private void BeginBlastDebris()
        {
            MarkSettling();
            _body.isKinematic = false;
            _body.useGravity = true;
            _blastDespawnAt = Time.unscaledTime + BLAST_DEBRIS_LIFETIME;
        }

        /// <summary>진흙에 접촉한 목재를 물리적으로 고정하고 부착 관계를 기록한다.</summary>
        public void AttachWood(CastlePiece3D wood)
        {
            if (!_attachedPieces.Add(wood)) return;
            wood._attachedPieces.Add(this);
            FixedJoint joint = wood.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = _body;
            joint.enableCollision = false;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }

        /// <summary>첫 접촉 기물과 한 번만 FixedJoint 결합하고 고유 능력을 봉인한다.</summary>
        public bool TryAttachTo(CastlePiece3D target)
        {
            if (!IsVine || AbilitySuppressed || HasAttachmentTarget ||
                target == null || target == this || target.MaterialId == "foundation" ||
                target.MaterialId == "barrel" || target._settling ||
                _body == null || target.Body == null)
                return false;

            _attachmentTarget = target;
            _attachedPieces.Add(target);
            target._attachedPieces.Add(this);

            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target.Body;
            joint.enableCollision = false;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            target.SuppressAbilities();
            return true;
        }

        /// <summary>대상의 기본 보강력은 유지하되 기존·미래의 특수능력 효과를 없앤다.</summary>
        private void SuppressAbilities()
        {
            AbilitySuppressed = true;
            PermanentBonus = 0;
        }

        /// <summary>첫 충돌부터 1초 경과 시 소유자에게 폭발을 요청한다.</summary>
        private IEnumerator Detonate()
        {
            yield return new WaitForSeconds(BARREL_FUSE_SECONDS);
            if (!_settling && !AbilitySuppressed)
            {
                if (CastleCreekScore3D.Active != null)
                    CastleCreekScore3D.Active.Detonate(this);
                else if (_game != null && !_game.IsAbilityDisabled(this))
                    _game.Explode(this);
            }
            _fusePending = false;
        }
    }
}
