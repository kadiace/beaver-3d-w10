using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class CastlePiece3D : MonoBehaviour
    {
        [SerializeField] private string materialId;

        private readonly HashSet<CastlePiece3D> _contacts = new HashSet<CastlePiece3D>();
        private readonly HashSet<CastlePiece3D> _attachedPieces = new HashSet<CastlePiece3D>();
        private readonly Dictionary<CastlePiece3D, FixedJoint> _glueJoints = new Dictionary<CastlePiece3D, FixedJoint>();
        private readonly HashSet<CastlePiece3D> _brokenContactBlocks = new HashSet<CastlePiece3D>();
        private readonly List<CastlePiece3D> _brokenJointBuffer = new List<CastlePiece3D>();
        private CastlePiece3D _attachmentTarget;
        private CastleGame3D _game;
        private Rigidbody _body;
        private Renderer[] _renderers;
        private bool _fuseStarted;
        private bool _fusePending;

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

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            SetOutlineLayer(0);
        }

        /// <summary>자식 렌더러의 외곽선 레이어만 교체하고 기본 렌더링 레이어는 보존한다.</summary>
        public void SetOutlineLayer(uint layer)
        {
            foreach (Renderer renderer in _renderers)
                renderer.renderingLayerMask = (renderer.renderingLayerMask & ~6u) | layer;
        }

        /// <summary>생성된 기물에 게임 소유자 및 설치된 턴을 연결한다.</summary>
        public void Initialize(CastleGame3D game, int turn)
        {
            _game = game;
            PlacedTurn = turn;
        }

        private void FixedUpdate()
        {
            CleanupBrokenJoints();
            if (_game != null && materialId != "foundation" &&
                transform.position.z <= _game.DestructionBoundaryZ)
                _game.RemoveOutsideBoundary(this);
        }

        private void OnCollisionEnter(Collision collision)
        {
            Register(collision, true);
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
            Register(collision, false);
        }

        private void OnCollisionExit(Collision collision)
        {
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this) return;
            _contacts.Remove(other);
            other._contacts.Remove(this);
            _brokenContactBlocks.Remove(other);
            other._brokenContactBlocks.Remove(this);
        }

        private void OnDestroy()
        {
            foreach (CastlePiece3D other in _attachedPieces)
                if (other != null) other._attachedPieces.Remove(this);
            _attachedPieces.Clear();
            ReleaseGlueJoints();
            foreach (CastlePiece3D other in _brokenContactBlocks)
                if (other != null) other._brokenContactBlocks.Remove(this);
            _brokenContactBlocks.Clear();
            foreach (CastlePiece3D other in _contacts)
                if (other != null) other._contacts.Remove(this);
            _contacts.Clear();
        }

        /// <summary>
        /// collision의 기물을 양방향 접촉 목록에 기록하고 최초 충돌이면 공통 결합을 시도한다.
        /// isNewCollision은 새 충돌 여부이며 접촉 유지 중에는 재결합하지 않는다.
        /// </summary>
        private void Register(Collision collision, bool isNewCollision)
        {
            CastlePiece3D other = collision.collider.GetComponentInParent<CastlePiece3D>();
            if (other == null || other == this) return;
            CleanupBrokenJoints();
            other.CleanupBrokenJoints();
            bool firstContact = _contacts.Add(other);
            other._contacts.Add(this);
            _game?.TryAttachVines(this, other);
            if (isNewCollision && firstContact && !_brokenContactBlocks.Contains(other))
                _game?.TryAttachGlue(this, other);
        }

        /// <summary>
        /// target이 다른 연결망에 있으면 strength를 파괴 강도로 사용해 조인트를 생성한다.
        /// 양쪽 연결 목록을 갱신하고 생성 여부를 반환한다.
        /// </summary>
        public bool TryGlueTo(CastlePiece3D target, float strength)
        {
            if (target == null || target == this || _body == null || target.Body == null ||
                _brokenContactBlocks.Contains(target) || target._brokenContactBlocks.Contains(this) ||
                _glueJoints.ContainsKey(target) || IsJointConnectedTo(target))
                return false;

            FixedJoint joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target.Body;
            joint.enableCollision = true;
            joint.breakForce = strength;
            joint.breakTorque = strength;
            _glueJoints.Add(target, joint);
            target._glueJoints.Add(this, joint);
            return true;
        }

        /// <summary>
        /// 파괴되어 사라진 접착 조인트를 양쪽 목록에서 제거한다.
        /// 여전히 접촉 중인 대상은 접촉이 끝날 때까지 재결합을 차단한다.
        /// </summary>
        private void CleanupBrokenJoints()
        {
            _brokenJointBuffer.Clear();
            foreach (KeyValuePair<CastlePiece3D, FixedJoint> bond in _glueJoints)
                if (bond.Key == null || bond.Value == null)
                    _brokenJointBuffer.Add(bond.Key);

            foreach (CastlePiece3D other in _brokenJointBuffer)
            {
                _glueJoints.Remove(other);
                if (other == null) continue;
                other._glueJoints.Remove(this);
                if (_contacts.Contains(other) || other._contacts.Contains(this))
                {
                    _brokenContactBlocks.Add(other);
                    other._brokenContactBlocks.Add(this);
                }
            }
            _brokenJointBuffer.Clear();
        }

        /// <summary>
        /// 이 기물의 모든 일반 접착 조인트를 제거하고 상대 기물의 연결 목록을 갱신한다.
        /// 기물이 게임에서 제거되거나 파괴될 때 사용한다.
        /// </summary>
        internal void ReleaseGlueJoints()
        {
            foreach (KeyValuePair<CastlePiece3D, FixedJoint> bond in _glueJoints)
            {
                if (bond.Key != null) bond.Key._glueJoints.Remove(this);
                if (bond.Value != null) Destroy(bond.Value);
            }
            _glueJoints.Clear();
        }

        /// <summary>접착제와 덩굴의 기존 조인트를 따라 대상까지 이어지는 경로가 있는지 확인한다.</summary>
        private bool IsJointConnectedTo(CastlePiece3D target)
        {
            HashSet<CastlePiece3D> visited = new HashSet<CastlePiece3D> { this };
            Stack<CastlePiece3D> pending = new Stack<CastlePiece3D>();
            pending.Push(this);

            while (pending.Count > 0)
            {
                CastlePiece3D current = pending.Pop();
                foreach (KeyValuePair<CastlePiece3D, FixedJoint> bond in current._glueJoints)
                {
                    CastlePiece3D neighbor = bond.Key;
                    if (neighbor == null || bond.Value == null) continue;
                    if (neighbor == target) return true;
                    if (visited.Add(neighbor)) pending.Push(neighbor);
                }

                foreach (CastlePiece3D neighbor in current._attachedPieces)
                {
                    if (neighbor == null) continue;
                    if (neighbor == target) return true;
                    if (visited.Add(neighbor)) pending.Push(neighbor);
                }
            }

            return false;
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
