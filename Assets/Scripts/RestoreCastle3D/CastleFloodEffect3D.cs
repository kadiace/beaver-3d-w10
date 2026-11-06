using UnityEngine;
using UnityEngine.Rendering;

namespace RestoreCastle3D
{
    /// <summary>
    /// 하나의 투명한 물 큐브가 +Z에서 -Z까지 차오른 후 -Y로 급강하한다.
    /// Collider를 제거하므로 게임 물리와 선택 Raycast에는 영향을 주지 않는다.
    /// </summary>
    internal sealed class CastleFloodEffect3D
    {
        [Header("Flood state")]
        private GameObject _water;
        private Material _material;
        private CastleFloodVolume3D _volume;
        private float _filledLength;

        /// <summary>
        /// 공통 판정에서 받은 영역으로 물의 표시를 준비하고 채움 길이를 0으로 초기화한다.
        /// </summary>
        public void Begin(CastleFloodVolume3D volume)
        {
            EnsureWater();
            _volume = volume;
            _water.SetActive(true);
            SetFill(0f);
        }

        /// <summary>물이 +Z에서 -Z로 확장되는 양을 0~1 사이에서 제어한다.</summary>
        public void SetFill(float fraction)
        {
            if (_water == null) return;
            _filledLength = _volume.Length * Mathf.Clamp01(fraction);
            // A zero-length cube causes artifacts on some GPUs. Move it offscreen
            // when empty, then grow with the back edge anchored to +Z.
            float length = Mathf.Max(0.001f, _filledLength);
            _water.transform.position = new Vector3(
                _volume.CenterX, _volume.CenterY, _volume.StartZ - length * 0.5f);
            _water.transform.localScale = new Vector3(_volume.Width, _volume.Depth, length);
        }

        /// <summary>
        /// Returns the current filled depth so callers can query only the newly advanced water slab.
        /// FilledLength is updated by SetFill.
        /// </summary>
        public float FilledLength => _filledLength;

        /// <summary>가득 찬 물을 빠르게 아래(-Y)로 내려 화면에서 제거한다.</summary>
        public void SetDrain(float fraction)
        {
            if (_water == null) return;
            float distance = _volume.Depth + 18f;
            _water.transform.position = new Vector3(
                _volume.CenterX,
                _volume.CenterY - distance * Mathf.Clamp01(fraction),
                _volume.StartZ - _filledLength * 0.5f);
        }

        /// <summary>홍수 표시 오브젝트를 비활성화한다. 미리보기 또는 실제 홍수의 화면 표시만 해제한다.</summary>
        public void Hide()
        {
            if (_water != null) _water.SetActive(false);
        }

        /// <summary>생성한 물 오브젝트와 재질을 해제하고 내부 참조를 비운다.</summary>
        public void Dispose()
        {
            if (_water != null) Object.Destroy(_water);
            if (_material != null) Object.Destroy(_material);
            _water = null;
            _material = null;
        }

        /// <summary>표시용 물 오브젝트와 투명한 파란 재질을 최초 한 번 준비하고 물리 콜라이더를 비활성화한다.</summary>
        private void EnsureWater()
        {
            if (_water != null) return;

            _water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _water.name = "Stage Flood Water";
            _water.layer = 2; // Ignore Raycast.
            Collider collider = _water.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _material = new Material(shader);
            _material.name = "Stage Flood Transparent Blue";
            Color blue = new Color(0.08f, 0.43f, 0.98f, 0.43f);

            if (_material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", blue);
            if (_material.HasProperty("_Color"))
                _material.SetColor("_Color", blue);
            if (_material.HasProperty("_Surface"))
                _material.SetFloat("_Surface", 1f);
            if (_material.HasProperty("_Blend"))
                _material.SetFloat("_Blend", 0f);
            if (_material.HasProperty("_SrcBlend"))
                _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (_material.HasProperty("_DstBlend"))
                _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (_material.HasProperty("_ZWrite"))
                _material.SetFloat("_ZWrite", 0f);

            _material.SetOverrideTag("RenderType", "Transparent");
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.DisableKeyword("_ALPHATEST_ON");
            _material.renderQueue = (int)RenderQueue.Transparent;

            MeshRenderer renderer = _water.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _water.SetActive(false);
        }
    }
}
