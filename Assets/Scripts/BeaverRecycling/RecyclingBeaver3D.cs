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

        [Header("Eye glow and motion trails")]
        [SerializeField] private Transform _leftEye;
        [SerializeField] private Transform _rightEye;
        [SerializeField] private Material _eyeGlowMaterial;
        [SerializeField, Min(0f)] private float _eyeTrailBaseDuration = 0.18f;
        [SerializeField, Min(0f)] private float _eyeTrailDurationPerItem = 0.05f;
        [SerializeField, Min(0f)] private float _eyeGlowIntensityPerItem = 0.32f;
        [SerializeField, Min(0f)] private float _eyeTrailWidthPerItem = 0.01f;
        private const float EYE_CORE_DIAMETER = 0.2f;
        private const float EYE_HALO_DIAMETER = 0.46f;
        private const float EYE_CAMERA_OFFSET = 0.12f;
        private const float EYE_TRAIL_BASE_WIDTH = 0.15f;
        private const int MAX_EYE_EFFECT_ITEMS = 12;
        private static readonly int EyeGlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");
        private static readonly int UseRadialFalloffId = Shader.PropertyToID("_UseRadialFalloff");
        // Warm amber contrasts with the blue river even when Bloom is disabled.
        private static readonly Color EyeGlowBaseColor = new Color(1f, 0.65f, 0.14f, 1f);
        private MaterialPropertyBlock _eyeGlowProperties;
        private MaterialPropertyBlock _eyeHaloProperties;
        private MaterialPropertyBlock _eyeTrailProperties;
        private Transform[] _eyeAnchors;
        private Transform[] _eyeEffectHeads;
        private Transform[] _eyeGlowBillboards;
        private Transform[] _eyeHaloBillboards;
        private MeshRenderer[] _eyeGlowMeshes;
        private MeshRenderer[] _eyeHaloMeshes;
        private TrailRenderer[] _eyeTrails;
        private int _lastEyeEffectCount = -1;
        private Vector3 _previousPosition;
        private Vector3 _currentPosition;
        private Quaternion _previousRotation;
        private Quaternion _currentRotation;
        private float _previousBobHeight;
        private float _currentBobHeight;
        private float _lastMoveFixedTime;

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
        public Vector2 MovementMinimum => _minimum;
        public Vector2 MovementMaximum => _maximum;
        public Vector3 VisualPosition => Vector3.Lerp(_previousPosition, _currentPosition, InterpolationFactor);
        private float InterpolationFactor => Mathf.Clamp01((Time.time - _lastMoveFixedTime) / Time.fixedDeltaTime);

        void Awake()
        {
            _eyeGlowProperties = new MaterialPropertyBlock();
            _eyeHaloProperties = new MaterialPropertyBlock();
            _eyeTrailProperties = new MaterialPropertyBlock();
            _startingPosition = transform.position;
            _stunStarCenterLocalPosition = _stunStar.localPosition;
            _stunStarStartingRotation = _stunStar.localRotation;
            _stunCamera = Camera.main.transform;
            _stunStar.gameObject.SetActive(false);
            _previousPosition = _currentPosition = transform.position;
            _previousRotation = _currentRotation = transform.rotation;
            _lastMoveFixedTime = Time.time - Time.fixedDeltaTime;
            CreateEyeTrails();
            SetCarriedItemCount(0);
        }

        void LateUpdate()
        {
            _visual.position = VisualPosition + Vector3.up * Mathf.Lerp(_previousBobHeight, _currentBobHeight, InterpolationFactor);
            _visual.rotation = Quaternion.Slerp(_previousRotation, _currentRotation, InterpolationFactor);
            UpdateEyeTrailPositions();
            AnimateStunStar(Time.deltaTime);
        }

        /// <summary>이동 입력, 이동 속도, deltaTime을 사용해 작업 구역 안에서 비버의 위치·방향·걸음 표현을 갱신한다.</summary>
        public void Move(Vector2 input, float speed, float deltaTime)
        {
            _previousPosition = _currentPosition;
            _previousRotation = _currentRotation;
            _previousBobHeight = _currentBobHeight;
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
            _currentPosition = transform.position;
            _currentRotation = transform.rotation;
            _lastMoveFixedTime = Time.fixedTime;
            _stride += deltaTime * speed * 5f;
            _currentBobHeight = movement.sqrMagnitude > 0f ? Mathf.Abs(Mathf.Sin(_stride)) * 0.045f : 0f;
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


        /// <summary>씬에 있는 양쪽 눈에 카메라를 향하는 발광 코어, 후광과 이동 잔상을 생성한다.</summary>
        private void CreateEyeTrails()
        {
            if (_leftEye == null || _rightEye == null || _eyeGlowMaterial == null) return;

            _eyeAnchors = new[] { _leftEye, _rightEye };
            _eyeEffectHeads = new Transform[_eyeAnchors.Length];
            _eyeGlowBillboards = new Transform[_eyeAnchors.Length];
            _eyeHaloBillboards = new Transform[_eyeAnchors.Length];
            _eyeGlowMeshes = new MeshRenderer[_eyeAnchors.Length];
            _eyeHaloMeshes = new MeshRenderer[_eyeAnchors.Length];
            _eyeTrails = new TrailRenderer[_eyeAnchors.Length];

            for (int i = 0; i < _eyeAnchors.Length; i++)
            {
                GameObject head = new GameObject(i == 0 ? "Left Eye Glow + Trail" : "Right Eye Glow + Trail");
                head.layer = gameObject.layer;
                head.transform.SetParent(_visual, false);
                _eyeEffectHeads[i] = head.transform;

                // Soft-edged, camera-facing quads remain legible at the game's orthographic zoom.
                _eyeGlowBillboards[i] = CreateEyeBillboard(head.transform, "Glow Core",
                    EYE_CORE_DIAMETER, out _eyeGlowMeshes[i]);
                _eyeHaloBillboards[i] = CreateEyeBillboard(head.transform, "Glow Halo",
                    EYE_HALO_DIAMETER, out _eyeHaloMeshes[i]);

                TrailRenderer trail = head.AddComponent<TrailRenderer>();
                trail.sharedMaterial = _eyeGlowMaterial;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.alignment = LineAlignment.View;
                trail.emitting = true;
                trail.minVertexDistance = 0.012f;
                trail.numCornerVertices = 4;
                trail.numCapVertices = 4;
                trail.startWidth = EYE_TRAIL_BASE_WIDTH;
                trail.endWidth = 0f;
                trail.time = _eyeTrailBaseDuration;
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.75f, 0.25f), 1f)
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                };
                _eyeTrails[i] = trail;
            }

            UpdateEyeTrailPositions();
            foreach (TrailRenderer trail in _eyeTrails) trail.Clear();
        }

        /// <summary>빛번짐 효과를 가진 쿼드를 눈 이펙트 중심에 설치한다. 물리 충돌은 생성하지 않는다.</summary>
        private Transform CreateEyeBillboard(Transform parent, string name, float diameter,
            out MeshRenderer renderer)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.layer = gameObject.layer;
            quad.transform.SetParent(parent, false);
            quad.transform.localScale = Vector3.one * diameter;

            Collider collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _eyeGlowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad.transform;
        }

        /// <summary>보간된 눈 위치로 트레일 시작점을 옮기고 빛나는 눈의 평면을 카메라에 정렬한다.</summary>
        private void UpdateEyeTrailPositions()
        {
            if (_eyeEffectHeads == null) return;
            for (int i = 0; i < _eyeEffectHeads.Length; i++)
            {
                // The camera-facing lift preserves the eye's screen position without burying it in the face.
                _eyeEffectHeads[i].position = _eyeAnchors[i].position -
                    _stunCamera.forward * EYE_CAMERA_OFFSET;
                _eyeGlowBillboards[i].rotation = _stunCamera.rotation;
                _eyeHaloBillboards[i].rotation = _stunCamera.rotation;
            }
        }

        /// <summary>운반 물체가 많아질수록 눈과 후광은 더 밝게, 잔상은 더 길고 굵게 만든다.</summary>
        public void SetCarriedItemCount(int count)
        {
            int effectCount = Mathf.Clamp(count, 0, MAX_EYE_EFFECT_ITEMS);
            if (effectCount == _lastEyeEffectCount) return;
            _lastEyeEffectCount = effectCount;
            if (_eyeTrails == null) return;

            float duration = _eyeTrailBaseDuration + _eyeTrailDurationPerItem * effectCount;
            float intensity = 1.6f + _eyeGlowIntensityPerItem * effectCount;
            Color coreColor = EyeGlowBaseColor * intensity;
            coreColor.a = 1f;
            Color haloColor = EyeGlowBaseColor * (intensity * 0.8f);
            haloColor.a = 0.28f;

            _eyeGlowProperties.SetColor(EyeGlowColorId, coreColor);
            _eyeGlowProperties.SetFloat(UseVertexColorId, 0f);
            _eyeGlowProperties.SetFloat(UseRadialFalloffId, 1f);
            _eyeHaloProperties.SetColor(EyeGlowColorId, haloColor);
            _eyeHaloProperties.SetFloat(UseVertexColorId, 0f);
            _eyeHaloProperties.SetFloat(UseRadialFalloffId, 1f);
            _eyeTrailProperties.SetColor(EyeGlowColorId, coreColor);
            _eyeTrailProperties.SetFloat(UseVertexColorId, 1f);
            _eyeTrailProperties.SetFloat(UseRadialFalloffId, 0f);

            for (int i = 0; i < _eyeTrails.Length; i++)
            {
                _eyeTrails[i].time = duration;
                _eyeTrails[i].startWidth = EYE_TRAIL_BASE_WIDTH + _eyeTrailWidthPerItem * effectCount;
                _eyeTrails[i].SetPropertyBlock(_eyeTrailProperties);
                _eyeGlowMeshes[i].SetPropertyBlock(_eyeGlowProperties);
                _eyeHaloMeshes[i].SetPropertyBlock(_eyeHaloProperties);
            }
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
            _previousPosition = _currentPosition = transform.position;
            _previousRotation = _currentRotation = transform.rotation;
            _previousBobHeight = _currentBobHeight = 0f;
            _lastMoveFixedTime = Time.time - Time.fixedDeltaTime;
            _visual.localPosition = Vector3.zero;
            _visual.localRotation = Quaternion.identity;
            SetCarriedItemCount(0);
            UpdateEyeTrailPositions();
            if (_eyeTrails != null)
                foreach (TrailRenderer trail in _eyeTrails) trail.Clear();
        }
    }
}
