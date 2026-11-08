using UnityEngine;

namespace RestoreCastle3D
{
    [RequireComponent(typeof(CastlePiece3D))]
    public sealed class CastleBarrelVisual3D : MonoBehaviour
    {
        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int COLOR_ID = Shader.PropertyToID("_Color");

        [Header("Pending explosion")]
        [SerializeField, Min(0.05f)] private float _blinkInterval = 0.2f;
        [SerializeField] private Color _warningRed = new Color(1f, 0.05f, 0.02f, 0.9f);
        [SerializeField] private Color _warningYellow = new Color(1f, 0.9f, 0.02f, 0.9f);

        [Header("Spent barrel")]
        [SerializeField] private Color _spentTint = new Color(0.12f, 0.12f, 0.12f, 0.92f);

        private CastlePiece3D _piece;
        private Renderer[] _renderers;
        private Color[][] _originalColors;
        private int[][] _colorProperties;
        private MaterialPropertyBlock _propertyBlock;
        private float _warningStartTime;
        private int _lastTintState = -1;

        private void Awake()
        {
            _piece = GetComponent<CastlePiece3D>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalColors = new Color[_renderers.Length][];
            _colorProperties = new int[_renderers.Length][];
            _propertyBlock = new MaterialPropertyBlock();
            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Material[] materials = _renderers[rendererIndex].sharedMaterials;
                _originalColors[rendererIndex] = new Color[materials.Length];
                _colorProperties[rendererIndex] = new int[materials.Length];
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;
                    int property = material.HasProperty(BASE_COLOR_ID) ? BASE_COLOR_ID
                        : material.HasProperty(COLOR_ID) ? COLOR_ID : 0;
                    _colorProperties[rendererIndex][materialIndex] = property;
                    if (property != 0)
                        _originalColors[rendererIndex][materialIndex] = material.GetColor(property);
                }
            }
        }

        private void Update()
        {
            if (_piece.HasExploded)
            {
                if (_lastTintState != 2)
                {
                    ApplyTint(_spentTint);
                    _lastTintState = 2;
                }
                return;
            }

            if (!_piece.FusePending)
            {
                if (_lastTintState != -1)
                {
                    ApplyTint(Color.clear);
                    _lastTintState = -1;
                }
                return;
            }

            if (_lastTintState == -1) _warningStartTime = Time.time;
            int state = Mathf.FloorToInt((Time.time - _warningStartTime) / _blinkInterval) % 2;
            if (_lastTintState == state) return;
            ApplyTint(state == 0 ? _warningRed : _warningYellow);
            _lastTintState = state;
        }

        private void OnDisable()
        {
            if (_lastTintState == -1) return;
            ApplyTint(Color.clear);
            _lastTintState = -1;
        }

        /// <summary>
        /// tint의 RGB를 알파 강도만큼 각 렌더러의 원래 색에 혼합한다.
        /// 물체의 불투명도와 다른 속성을 유지하며 공유 머티리얼은 수정하지 않는다.
        /// </summary>
        private void ApplyTint(Color tint)
        {
            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer renderer = _renderers[rendererIndex];
                for (int materialIndex = 0; materialIndex < _colorProperties[rendererIndex].Length; materialIndex++)
                {
                    int property = _colorProperties[rendererIndex][materialIndex];
                    if (property == 0) continue;
                    Color original = _originalColors[rendererIndex][materialIndex];
                    Color blended = Color.Lerp(original, tint, tint.a);
                    blended.a = original.a;
                    _propertyBlock.Clear();
                    renderer.GetPropertyBlock(_propertyBlock, materialIndex);
                    _propertyBlock.SetColor(property, blended);
                    renderer.SetPropertyBlock(_propertyBlock, materialIndex);
                }
            }
        }
    }
}
