using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CastlePiece3D : MonoBehaviour
    {
        private const float ATTACHED_MAX_DEPENETRATION_SPEED = 2f;
        private const float BARREL_FUSE_SECONDS = 1f;
        private const float BARREL_BLAST_RADIUS = 2f;
        private const float BARREL_BLAST_IMPULSE = 30f;
        private const float BLAST_DEBRIS_LIFETIME = 1f;
        private const float DESPAWN_Z = 15f;
        private const float DESPAWN_Y = -10f;

        [SerializeField] private string materialId;

        private readonly HashSet<CastlePiece3D> _contacts = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _attachedPieces = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _ignoredAttachmentCollisions = new HashSet<CastlePiece3D>();
        private readonly HashSet<Collider> _creekContacts = new HashSet<Collider>();
        private CastlePiece3D _attachmentTarget;
        private CastlePiece3D _springTarget;
        private CastlePiece3D _magnetRoot;
        private CastlePiece3D _barrelIgnitionTarget;
        private FixedJoint _springJoint;
        private float _lastCreekFlowTime = float.NegativeInfinity;
        private float _originalLinearDamping;
        private float _originalMaxDepenetrationVelocity;
        private CastleGame3D _game;
        private Rigidbody _body;
        private Renderer[] _renderers;
        private bool _persistentTrashOutline;
        private bool _penaltyOutline;
        private uint _requestedOutlineLayer;
        private bool _fuseStarted;
        private bool _fusePending;
        private float _fuseStartedAt;
        private int _growthCount;
        private bool _continuousDrop;
        private bool _continuousBeforeSpring;
        private bool _thrown;
        private bool _throwOrigin;
        private bool _held;
        private bool _removalScheduled;
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
                Time.time < _attachBlockedUntil || Time.time < other._attachBlockedUntil)
                return false;
            if (_held && _magnetRoot == this)
                return other._magnetRoot == null && !other._held;
            if (_attachedPieces.Count != 0) return false;
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
            // Keep the existing horizontal momentum while gravity accelerates the cliff fall.
            _body.linearDamping = 0f;
            StopAllCoroutines();
            _held = false;
            _magnetRoot = null;
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
            _originalLinearDamping = _body.linearDamping;
            _originalMaxDepenetrationVelocity = _body.maxDepenetrationVelocity;
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

        /// <summary>Trash keeps a red outline even when hovered or dragged.</summary>
        public void SetPersistentTrashOutline(bool enabled)
        {
            _persistentTrashOutline = enabled;
            SetOutlineLayer(_requestedOutlineLayer);
        }

        /// <summary>Preserve base rendering layers and prioritize red trash outlines.</summary>
        public void SetOutlineLayer(uint layer)
        {
            _requestedOutlineLayer = layer;
            uint outline = _settling ? 0u :
                _persistentTrashOutline || _penaltyOutline ? 8u :
                layer != 0 ? layer : _springTarget != null ? 16u : _throwOrigin ? 4u : 0u;
            foreach (Renderer renderer in _renderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~30u) | outline;
        }

        public void SetPenaltyOutline(bool enabled)
        {
            _penaltyOutline = enabled && !_settling;
            SetOutlineLayer(_requestedOutlineLayer);
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
            if (CastleCreekScore3D.Active != null) _magnetRoot = this;
            _continuousDrop = false;
            _removalScheduled = false;
            StopAllCoroutines();
            if (_fusePending && _fuseStarted) StartCoroutine(Detonate());
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        /// <summary>잡은 기물을 XZ로 이동시키되 빠른 드래그 경로의 충돌도 따라가며 자석으로 연결한다.</summary>
        public void MoveHeld(Vector3 position)
        {
            if (_settling) return;
            Vector3 displacement = position - _body.position;
            if (_held && _magnetRoot == this && displacement.sqrMagnitude > 0.0001f)
            {
                Vector3 start = _body.position;
                Vector3 direction = displacement.normalized;
                RaycastHit[] hits = _body.SweepTestAll(direction, displacement.magnitude,
                    QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit hit in hits)
                {
                    CastlePiece3D other = hit.collider.GetComponentInParent<CastlePiece3D>();
                    if (other == null || other == this || other._magnetRoot == this) continue;
                    _body.position = start + direction * hit.distance;
                    Physics.SyncTransforms();
                    TryMagnetAttach(other, hit.point);
                }
            }
            _body.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>집고 있던 기물을 수평 속도 없이 중력으로 놓고 이후 다시 집지 못하도록 한다.</summary>
        public void Throw()
        {
            if (_settling) return;
            _held = false;
            if (_magnetRoot == this)
                foreach (CastlePiece3D member in CastleSynergy3D.Collect(this))
                    if (member._magnetRoot == this) member._magnetRoot = null;
            _throwOrigin = true;
            MarkAttachmentGroupThrown();
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (_settling || (CastleCreekScore3D.Active != null &&
                CastleCreekScore3D.Active.IsRunPaused)) return;
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
            if (CastleCreekScore3D.Active != null) ApplyCreekVelocity();
        }

        /// <summary>하천에 닿은 결합체 전체에 고정 유속을 주고 나머지 기물의 수평 속도를 제거한다.</summary>
        private void ApplyCreekVelocity()
        {
            if (_lastCreekFlowTime == Time.fixedTime ||
                (_magnetRoot != null && _magnetRoot._held)) return;
            var members = new HashSet<CastlePiece3D>();
            var pending = new Stack<CastlePiece3D>();
            pending.Push(this);
            bool touchesCreek = false;
            while (pending.Count > 0)
            {
                CastlePiece3D piece = pending.Pop();
                if (piece == null || piece._settling || !members.Add(piece)) continue;
                touchesCreek |= piece._creekContacts.Count > 0;
                foreach (CastlePiece3D attached in piece._attachedPieces)
                    if (attached != null && !members.Contains(attached)) pending.Push(attached);
            }

            float zSpeed = touchesCreek ? -CastleCreekScore3D.Active.CreekSpeed : 0f;
            foreach (CastlePiece3D piece in members)
            {
                piece._lastCreekFlowTime = Time.fixedTime;
                if (piece._body.isKinematic) continue;
                Vector3 velocity = piece._body.linearVelocity;
                velocity.x = 0f;
                velocity.z = zSpeed;
                piece._body.linearVelocity = velocity;
            }
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
            RefreshAttachmentCollisions();
            _contacts.Clear();
            _creekContacts.Clear();
            _attachmentTarget = null;
            _springTarget = null;
            _springJoint = null;
            _magnetRoot = null;
            _barrelIgnitionTarget = null;
            foreach (FixedJoint joint in GetComponents<FixedJoint>())
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
            _persistentTrashOutline = false;
            _penaltyOutline = false;
            _requestedOutlineLayer = 0u;
            foreach (var pair in _settlementColliders)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _settlementColliders.Clear();
            _body.detectCollisions = _originalDetectCollisions;
            _body.linearDamping = _originalLinearDamping;
            _body.maxDepenetrationVelocity = _originalMaxDepenetrationVelocity;
            _lastCreekFlowTime = float.NegativeInfinity;
            _game = null;
            _attachBlockedUntil = 0f;
            _settling = false;
            _fuseStarted = false;
            _fusePending = false;
            _fuseStartedAt = 0f;
            _growthCount = 0;
            _continuousDrop = false;
            _continuousBeforeSpring = false;
            _thrown = false;
            _throwOrigin = false;
            _held = false;
            _removalScheduled = false;
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
            if (collision.gameObject.name == "Flat Muddy Creek Bed" ||
                collision.gameObject.name == "Upper Flat Muddy Creek Bed")
                _creekContacts.Add(collision.collider);
            if (_settling) return;
            TryStartBarrelFuse(collision.collider);
            TryAttachDraggedPiece(collision);
            if (_continuousDrop) return;
            AttachThrownPiece(collision);
            Register(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (_settling) return;
            if (collision.gameObject.name == "Flat Muddy Creek Bed" ||
                collision.gameObject.name == "Upper Flat Muddy Creek Bed")
                _creekContacts.Add(collision.collider);
            TryStartBarrelFuse(collision.collider);
            TryAttachDraggedPiece(collision);
            if (_continuousDrop) return;
            AttachThrownPiece(collision);
            Register(collision);
        }

        /// <summary>오크통이 하천 바닥이나 다른 기물에 닿으면 정확히 한 번 도화선을 점화한다.</summary>
        private void TryStartBarrelFuse(Collider collider)
        {
            if (materialId != "barrel") return;
            CastlePiece3D other = collider.GetComponentInParent<CastlePiece3D>();
            if (other != null && other != this ||
                collider.gameObject.name == "Flat Muddy Creek Bed" ||
                collider.gameObject.name == "Upper Flat Muddy Creek Bed" ||
                collider.gameObject.name == "Flat Muddy Creek Bed After Cliff")
                IgniteBarrel(other);
        }

        /// <summary>최초 접촉한 기물을 기억하고 오크통 폭발을 1초 후 예약한다.</summary>
        private void IgniteBarrel(CastlePiece3D touched = null)
        {
            if (materialId != "barrel" || _fuseStarted || _settling ||
                AbilitySuppressed) return;
            _barrelIgnitionTarget = touched;
            _fuseStarted = true;
            _fuseStartedAt = Time.time;
            _fusePending = true;
            StartCoroutine(Detonate());
        }

        /// <summary>자석으로 잡고 있는 덩어리의 접촉을 FixedJoint 부착으로 확장한다.</summary>
        private void TryAttachDraggedPiece(Collision collision)
        {
            if (_magnetRoot == null || !_magnetRoot._held ||
                CastleCreekScore3D.Active == null || collision.contactCount == 0) return;
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            TryMagnetAttach(other, collision.GetContact(0).point);
        }

        /// <summary>드래그하는 덩어리에 닿은 독립 기물 또는 연결 덩어리를 현재 접촉 위치에 고정한다.</summary>
        private void TryMagnetAttach(CastlePiece3D other, Vector3 contactPoint)
        {
            if (_magnetRoot == null || !_magnetRoot._held || other == null ||
                other == this || _settling || other._settling || other._held ||
                Time.time < _attachBlockedUntil || Time.time < other._attachBlockedUntil)
                return;
            if (materialId == "barrel") { IgniteBarrel(other); return; }
            if (other.materialId == "barrel") { other.IgniteBarrel(this); return; }
            List<CastlePiece3D> incoming = CastleSynergy3D.Collect(other);
            if (incoming.Contains(_magnetRoot) || incoming.Exists(piece =>
                piece._settling || piece._held || piece._magnetRoot != null))
                return;

            foreach (CastlePiece3D member in incoming)
            {
                member._continuousBeforeSpring = member._continuousDrop;
                if (member._continuousDrop)
                {
                    member._continuousDrop = false;
                    member._removalScheduled = false;
                    member.StopAllCoroutines();
                }
                member._magnetRoot = _magnetRoot;
                member._thrown = true;
                member.SetOutlineLayer(0);
            }

            if (other._springTarget == null) other.CreateSpringAttachment(this, contactPoint);
            else if (_springTarget == null) CreateSpringAttachment(other, contactPoint);
            else
            {
                // Both components already have parent joints; an extra edge joins the two graphs.
                other.AddSpringAt(this, contactPoint);
                other._body.maxDepenetrationVelocity = ATTACHED_MAX_DEPENETRATION_SPEED;
                _body.maxDepenetrationVelocity = ATTACHED_MAX_DEPENETRATION_SPEED;
            }
            _attachedPieces.Add(other);
            other._attachedPieces.Add(this);
            RefreshAttachmentCollisions();
            CastleCreekScore3D.Active.Refresh(this, other);
        }

        private void OnCollisionExit(Collision collision)
        {
            _creekContacts.Remove(collision.collider);
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
            CastlePiece3D previous = _springTarget;
            DestroySpringLinks();
            if (previous == null) return;
            previous._attachedPieces.Remove(this);
            _attachedPieces.Remove(previous);
            RefreshAttachmentCollisions();
            previous.RefreshAttachmentCollisions();
            _springTarget = null;
            _thrown = false;
            if (_attachedPieces.Count == 0)
                _body.maxDepenetrationVelocity = _originalMaxDepenetrationVelocity;
            if (previous._attachedPieces.Count == 0)
                previous._body.maxDepenetrationVelocity = previous._originalMaxDepenetrationVelocity;
            _continuousDrop = _continuousBeforeSpring;
            _continuousBeforeSpring = false;
            SetOutlineLayer(0);
            if (CastleCreekScore3D.Active != null && !_settling && !previous._settling)
                CastleCreekScore3D.Active.Refresh(this, previous);
        }

        private void DestroySpringLinks()
        {
            if (_springJoint != null)
            {
                _springJoint.connectedBody = null;
                Destroy(_springJoint);
            }
            _springJoint = null;
        }

        /// <summary>결합체 내부 충돌을 끄고 연결이 끊긴 기물 사이의 충돌을 복원한다.</summary>
        private void RefreshAttachmentCollisions()
        {
            var members = new HashSet<CastlePiece3D>();
            var pending = new Stack<CastlePiece3D>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                CastlePiece3D piece = pending.Pop();
                if (piece == null || !members.Add(piece)) continue;
                foreach (CastlePiece3D attached in piece._attachedPieces)
                    if (attached != null && !members.Contains(attached)) pending.Push(attached);
            }

            foreach (CastlePiece3D piece in members)
            {
                foreach (CastlePiece3D other in new List<CastlePiece3D>(piece._ignoredAttachmentCollisions))
                {
                    if (other != null && members.Contains(other)) continue;
                    if (other != null) piece.SetAttachmentCollision(other, false);
                    else piece._ignoredAttachmentCollisions.Remove(other);
                }
                foreach (CastlePiece3D other in members)
                    if (other != piece && !piece._ignoredAttachmentCollisions.Contains(other))
                        piece.SetAttachmentCollision(other, true);
            }
        }

        /// <summary>두 기물의 모든 콜라이더 사이 충돌을 결합 상태에 맞게 설정한다.</summary>
        private void SetAttachmentCollision(CastlePiece3D other, bool attached)
        {
            foreach (Collider ownCollider in GetComponentsInChildren<Collider>())
                foreach (Collider otherCollider in other.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(ownCollider, otherCollider, attached);
            if (attached)
            {
                _ignoredAttachmentCollisions.Add(other);
                other._ignoredAttachmentCollisions.Add(this);
            }
            else
            {
                _ignoredAttachmentCollisions.Remove(other);
                other._ignoredAttachmentCollisions.Remove(this);
            }
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
            if (CastleCreekScore3D.Active != null || !_throwOrigin || _held ||
                _settling || materialId == "barrel" ||
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
                if (collision.contactCount == 0) return;
                ContactPoint contact = collision.GetContact(0);
                _body.linearVelocity = other._body.linearVelocity;
                _body.angularVelocity = other._body.angularVelocity;
                CreateSpringAttachment(other, contact.point);
                _attachedPieces.Add(other);
                other._attachedPieces.Add(this);
                RefreshAttachmentCollisions();
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
            if (collision.contactCount == 0) return;
            ContactPoint firstContact = collision.GetContact(0);
            _body.linearVelocity = other._body.linearVelocity;
            _body.angularVelocity = other._body.angularVelocity;
            other.CreateSpringAttachment(this, firstContact.point);
            _attachedPieces.Add(other);
            other._attachedPieces.Add(this);
            RefreshAttachmentCollisions();
            other.MarkAttachmentGroupThrown();
            CastleCreekScore3D.Active?.Refresh(this, other);
        }

        /// <summary>
        /// Fix the pieces' relative position at the collision contact without storing
        /// impact energy in multiple stiff springs.
        /// </summary>
        private void CreateSpringAttachment(CastlePiece3D target, Vector3 worldPoint)
        {
            _springJoint = AddSpringAt(target, worldPoint);
            _body.maxDepenetrationVelocity = ATTACHED_MAX_DEPENETRATION_SPEED;
            target._body.maxDepenetrationVelocity = ATTACHED_MAX_DEPENETRATION_SPEED;
            _springTarget = target;
        }

        /// <summary>충돌 지점에 두 기물의 상대 위치를 고정하는 조인트를 만든다.</summary>
        private FixedJoint AddSpringAt(CastlePiece3D target, Vector3 worldPoint)
        {
            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target.Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = transform.InverseTransformPoint(worldPoint);
            joint.connectedAnchor = target.transform.InverseTransformPoint(worldPoint);
            ConfigureSpring(joint);
            return joint;
        }

        /// <summary>오크통 폭발 이외의 힘으로는 파단되지 않도록 고정 조인트를 설정한다.</summary>
        private static void ConfigureSpring(FixedJoint joint)
        {
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
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
                RefreshAttachmentCollisions();
                other.RefreshAttachmentCollisions();
                if (_attachmentTarget == other) _attachmentTarget = null;
                if (other._attachmentTarget == this) other._attachmentTarget = null;
                if (CastleCreekScore3D.Active != null && !other._settling)
                    CastleCreekScore3D.Active.Refresh(other);
            }
        }

        /// <summary>반경 2m에 닿은 모든 연결 덩어리를 끝까지 전파해 폭파하고 오크통도 잔해로 만든다.</summary>
        public List<CastlePiece3D> ExplodeNearby()
        {
            var targets = new HashSet<CastlePiece3D> { this };
            Vector3 origin = _body.worldCenterOfMass;
            if (_barrelIgnitionTarget != null && _barrelIgnitionTarget.isActiveAndEnabled &&
                !_barrelIgnitionTarget._settling)
                foreach (CastlePiece3D member in CastleSynergy3D.Collect(_barrelIgnitionTarget))
                    if (!member._settling && member.materialId != "foundation")
                        targets.Add(member);
            foreach (Collider collider in Physics.OverlapSphere(origin, BARREL_BLAST_RADIUS,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                CastlePiece3D target = collider.GetComponentInParent<CastlePiece3D>();
                if (target == null || target._settling || !target.isActiveAndEnabled ||
                    target.materialId == "foundation") continue;
                foreach (CastlePiece3D member in CastleSynergy3D.Collect(target))
                    if (!member._settling && member.materialId != "foundation")
                        targets.Add(member);
            }

            foreach (CastlePiece3D target in targets)
                target.BreakAllAttachments();
            foreach (CastlePiece3D target in targets)
            {
                Vector3 direction = target._body.worldCenterOfMass - origin;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
                else direction.y = Mathf.Max(direction.y, 0.5f);
                target.BeginBlastDebris();
                target._body.linearVelocity = Vector3.zero;
                target._body.angularVelocity = Vector3.zero;
                target._body.AddForce(direction.normalized * BARREL_BLAST_IMPULSE,
                    ForceMode.Impulse);
            }
            return new List<CastlePiece3D>(targets);
        }

        /// <summary>점수·상호작용·특수효과를 중단하고 물리 잔해로 1초 동안만 유지한다.</summary>
        private void BeginBlastDebris()
        {
            MarkSettling();
            _body.isKinematic = false;
            _body.useGravity = true;
            StartCoroutine(ReturnBlastDebris());
        }

        /// <summary>폭발 잔해를 1초 후 풀에 반환한다.</summary>
        private IEnumerator ReturnBlastDebris()
        {
            yield return new WaitForSeconds(BLAST_DEBRIS_LIFETIME);
            Managers.Resource.Destroy(gameObject);
        }

        /// <summary>진흙에 접촉한 목재를 물리적으로 고정하고 부착 관계를 기록한다.</summary>
        public void AttachWood(CastlePiece3D wood)
        {
            if (!_attachedPieces.Add(wood)) return;
            wood._attachedPieces.Add(this);
            RefreshAttachmentCollisions();
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
            RefreshAttachmentCollisions();

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
            yield return new WaitForSeconds(Mathf.Max(0f,
                BARREL_FUSE_SECONDS - (Time.time - _fuseStartedAt)));
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
