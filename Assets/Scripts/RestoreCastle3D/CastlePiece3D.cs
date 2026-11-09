using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CastlePiece3D : MonoBehaviour
    {
        private const float CREEK_SPEED = 3f;
        private const float DESPAWN_Z = -15f;
        private const float DESPAWN_Y = -10f;

        [SerializeField] private string materialId;

        private readonly HashSet<CastlePiece3D> _contacts = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _attachedPieces = new HashSet<CastlePiece3D>();
        private CastlePiece3D _attachmentTarget;
        private CastleGame3D _game;
        private Rigidbody _body;
        private Renderer[] _renderers;
        private bool _fuseStarted;
        private bool _fusePending;
        private int _growthCount;
        private bool _continuousDrop;
        private bool _removalScheduled;
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
        public bool RiverAnchored { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
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
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~6u) | layer;
        }

        /// <summary>드랍 가이드의 폐기물 디메리트 대상 여부에 따라 빨간 외곽선 레이어를 켜거나 끈다.</summary>
        public void SetPenaltyOutline(bool enabled)
        {
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
            _continuousDrop = true;
            _body.isKinematic = false;
            _body.useGravity = true;
        }

        private void Update()
        {
            if (transform.position.y < DESPAWN_Y)
            {
                Despawn();
                return;
            }
            if (!_continuousDrop || _removalScheduled || transform.position.z > DESPAWN_Z) return;
            _removalScheduled = true;
            StartCoroutine(ReturnContinuousDrop());
        }

        /// <summary>배치 기물은 게임의 추적 목록에서 제거하고 자동 낙하 기물은 리소스 풀로 반환한다.</summary>
        public void Despawn()
        {
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
            foreach (CastlePiece3D other in _attachedPieces)
                if (other != null) other._attachedPieces.Remove(this);
            foreach (CastlePiece3D other in _contacts)
                if (other != null) other._contacts.Remove(this);
            _attachedPieces.Clear();
            _contacts.Clear();
            _attachmentTarget = null;
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
            _game = null;
            _fuseStarted = false;
            _fusePending = false;
            _growthCount = 0;
            _continuousDrop = false;
            _removalScheduled = false;
            AbilitySuppressed = false;
            PermanentBonus = 0;
            RiverAnchored = false;
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
            if (_continuousDrop) return;
            Register(collision);
            TryAnchorToRiver(collision);
            if (materialId == "barrel" && !_game.IsAbilityDisabled(this)
                && !_fuseStarted && _game != null)
            {
                _fuseStarted = true;
                _fusePending = true;
                StartCoroutine(Detonate());
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (_continuousDrop)
            {
                if (collision.gameObject.name == "Flat Muddy Creek Bed" ||
                    collision.gameObject.name == "Upper Flat Muddy Creek Bed")
                {
                    Vector3 velocity = _body.linearVelocity;
                    _body.linearVelocity = new Vector3(0f, velocity.y, -CREEK_SPEED);
                }
                return;
            }
            Register(collision);
            TryAnchorToRiver(collision);
        }

        /// <summary>식물의 밑면이 강바닥에 직접 닿았을 때 위치를 고정하고 홍수 보호 상태를 기록한다.</summary>
        private void TryAnchorToRiver(Collision collision)
        {
            if (RiverAnchored || (materialId != "sunflower" && materialId != "bamboo" &&
                materialId != "living_tree") || collision.gameObject.name != "Flat Muddy Creek Bed") return;
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) < 0.7f) continue;
                RiverAnchored = true;
                _body.isKinematic = true;
                _body.useGravity = false;
                return;
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (_continuousDrop) return;
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

        /// <summary>충돌한 두 기물을 양방향 접촉 그래프에 기록한다. 바닥 충돌은 제외한다.</summary>
        private void Register(Collision collision)
        {
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this) return;
            _contacts.Add(other);
            other._contacts.Add(this);
            // Called on the physics contact frame, before the lump's Update
            // destruction can run. A vine touching a foul lump always wins.
            _game?.TryAttachVines(this, other);
            _game?.TryAttachMud(this, other);
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
            // An attached barrel cannot finish a fuse or detonate later.
            if (materialId == "barrel" && _fusePending)
            {
                StopAllCoroutines();
                _fusePending = false;
            }
        }

        /// <summary>첫 충돌부터 1초 경과 시 소유자에게 폭발을 요청한다.</summary>
        private IEnumerator Detonate()
        {
            yield return new WaitForSeconds(1f);
            if (_game != null && !_game.IsAbilityDisabled(this)) _game.Explode(this);
            // The barrel persists after exploding. Do not leave it flagged as
            // an unfinished fuse during every future round's settlement.
            _fusePending = false;
        }
    }
}
