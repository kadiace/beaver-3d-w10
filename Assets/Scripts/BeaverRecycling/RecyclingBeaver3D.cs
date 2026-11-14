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
        private Vector3 _previousPosition;
        private Vector3 _currentPosition;
        private Quaternion _previousRotation;
        private Quaternion _currentRotation;
        private float _previousBobHeight;
        private float _currentBobHeight;
        private float _lastMoveFixedTime;

        [Header("Eye fire")]
        [SerializeField] private MeshRenderer _leftEye;
        [SerializeField] private MeshRenderer _rightEye;
        private Material _eyeGlowMaterial;
        private Material _outerFlameMaterial;
        private Material _innerFlameMaterial;
        private Transform[] _outerFlames;
        private Transform[] _innerFlames;
        private int _carriedItemCount = -1;

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
            _startingPosition = transform.position;
            _stunStarCenterLocalPosition = _stunStar.localPosition;
            _stunStarStartingRotation = _stunStar.localRotation;
            _stunCamera = Camera.main.transform;
            _stunStar.gameObject.SetActive(false);
            _previousPosition = _currentPosition = transform.position;
            _previousRotation = _currentRotation = transform.rotation;
            _lastMoveFixedTime = Time.time - Time.fixedDeltaTime;
            _eyeGlowMaterial = new Material(_leftEye.sharedMaterial);
            _outerFlameMaterial = new Material(_leftEye.sharedMaterial);
            _innerFlameMaterial = new Material(_leftEye.sharedMaterial);
            _eyeGlowMaterial.EnableKeyword("_EMISSION");
            _outerFlameMaterial.EnableKeyword("_EMISSION");
            _innerFlameMaterial.EnableKeyword("_EMISSION");
            _outerFlameMaterial.SetColor("_BaseColor", new Color(1f, 0.22f, 0.015f));
            _outerFlameMaterial.SetColor("_EmissionColor", new Color(3f, 0.35f, 0.02f));
            _innerFlameMaterial.SetColor("_BaseColor", new Color(1f, 0.86f, 0.18f));
            _innerFlameMaterial.SetColor("_EmissionColor", new Color(3f, 2f, 0.18f));
            _leftEye.sharedMaterial = _eyeGlowMaterial;
            _rightEye.sharedMaterial = _eyeGlowMaterial;
            _outerFlames = new[] { CreateFlame(_leftEye, "Left outer flame", _outerFlameMaterial),
                CreateFlame(_rightEye, "Right outer flame", _outerFlameMaterial) };
            _innerFlames = new[] { CreateFlame(_leftEye, "Left inner flame", _innerFlameMaterial),
                CreateFlame(_rightEye, "Right inner flame", _innerFlameMaterial) };
            SetCarriedItemCount(0);
        }

        void LateUpdate()
        {
            _visual.position = VisualPosition + Vector3.up * Mathf.Lerp(_previousBobHeight, _currentBobHeight, InterpolationFactor);
            _visual.rotation = Quaternion.Slerp(_previousRotation, _currentRotation, InterpolationFactor);
            AnimateStunStar(Time.deltaTime);
            AnimateEyeFire();
        }

        /// <summary>눈 위치에 충돌 없는 불꽃 메시를 만들고 운반량에 따라 켜질 수 있도록 보관한다.</summary>
        private Transform CreateFlame(MeshRenderer eye, string objectName, Material material)
        {
            GameObject flame = new GameObject(objectName);
            flame.transform.SetParent(_visual, false);
            flame.transform.localPosition = eye.transform.localPosition + new Vector3(0f, 0.09f, 0.015f);
            flame.AddComponent<MeshFilter>().sharedMesh = eye.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = flame.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            flame.SetActive(false);
            return flame.transform;
        }

        /// <summary>현재 운반 중인 아이템 수에 맞춰 눈의 안광과 불꽃 표시 상태를 갱신한다.</summary>
        public void SetCarriedItemCount(int count)
        {
            if (_carriedItemCount == count) return;
            _carriedItemCount = count;
            float intensity = Mathf.Clamp01((count - 1f) / 5f);
            _eyeGlowMaterial.SetColor("_BaseColor", count > 0
                ? Color.Lerp(new Color(1f, 0.48f, 0.08f), new Color(1f, 0.16f, 0.02f), intensity)
                : new Color(0.035f, 0.045f, 0.05f));
            _eyeGlowMaterial.SetColor("_EmissionColor", count > 0
                ? new Color(2f + 2f * intensity, 0.55f + 0.35f * intensity, 0.04f)
                : Color.black);
            bool showFlames = count > 1;
            foreach (Transform flame in _outerFlames) flame.gameObject.SetActive(showFlames);
            foreach (Transform flame in _innerFlames) flame.gameObject.SetActive(showFlames);
        }

        /// <summary>운반량과 시간에 따라 눈 위 불꽃의 높이와 폭을 흔들리게 한다.</summary>
        private void AnimateEyeFire()
        {
            if (_carriedItemCount < 2) return;
            float strength = Mathf.Clamp01((_carriedItemCount - 1f) / 5f);
            for (int i = 0; i < _outerFlames.Length; i++)
            {
                float flicker = 0.85f + 0.15f * Mathf.Sin(Time.time * 17f + i * 2.3f);
                float height = (0.1f + 0.19f * strength) * flicker;
                _outerFlames[i].localScale = new Vector3(0.075f + 0.035f * strength, height, 0.055f);
                _outerFlames[i].localPosition = (_outerFlames[i].localPosition.x < 0f
                    ? _leftEye.transform.localPosition : _rightEye.transform.localPosition)
                    + new Vector3(Mathf.Sin(Time.time * 11f + i) * 0.012f,
                        0.1f + height * 0.45f, 0.015f);
                _innerFlames[i].localScale = new Vector3(0.035f, height * 0.7f, 0.04f);
                _innerFlames[i].localPosition = _outerFlames[i].localPosition + new Vector3(0f, -0.02f, 0.015f);
            }
        }

        void OnDestroy()
        {
            Destroy(_eyeGlowMaterial);
            Destroy(_outerFlameMaterial);
            Destroy(_innerFlameMaterial);
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
        }
    }
}
