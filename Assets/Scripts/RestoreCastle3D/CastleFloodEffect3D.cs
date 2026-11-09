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
        private const float MinimumDepth = 6f;

        private GameObject _water;
        private float _centerX;
        private float _width;
        private float _startZ;
        private float _endZ;
        private float _centerY;
        private float _depth;
        private float _filledLength;

        /// <summary>물 표면이 제거 높이와 일치하도록 지오메트리를 준비한다.</summary>
        public void Begin(float centerX, float width, float startZ, float endZ,
            float cutoffY, float groundY)
        {
            EnsureWater();
            _centerX = centerX;
            _width = Mathf.Max(1f, width);
            _startZ = startZ;
            _endZ = Mathf.Min(endZ, startZ - 0.1f);

            float bottomY = Mathf.Min(groundY - MinimumDepth, cutoffY - MinimumDepth);
            _depth = Mathf.Max(0.1f, cutoffY - bottomY);
            _centerY = (cutoffY + bottomY) * 0.5f;

            _water.SetActive(true);
            SetFill(0f);
        }

        /// <summary>물이 +Z에서 -Z로 확장되는 양을 0~1 사이에서 제어한다.</summary>
        public void SetFill(float fraction)
        {
            if (_water == null) return;
            _filledLength = (_startZ - _endZ) * Mathf.Clamp01(fraction);
            // A zero-length cube causes artifacts on some GPUs. Move it offscreen
            // when empty, then grow with the back edge anchored to +Z.
            float length = Mathf.Max(0.001f, _filledLength);
            _water.transform.position = new Vector3(
                _centerX, _centerY, _startZ - length * 0.5f);
            _water.transform.localScale = new Vector3(_width, _depth, length);
        }

        /// <summary>가득 찬 물을 빠르게 아래(-Y)로 내려 화면에서 제거한다.</summary>
        public void SetDrain(float fraction)
        {
            if (_water == null) return;
            float distance = _depth + 18f;
            _water.transform.position = new Vector3(
                _centerX,
                _centerY - distance * Mathf.Clamp01(fraction),
                _startZ - _filledLength * 0.5f);
        }

        public void Hide()
        {
            if (_water != null) _water.SetActive(false);
        }

        public void Dispose()
        {
            if (_water != null) Object.Destroy(_water);
            _water = null;
        }

        private void EnsureWater()
        {
            if (_water != null) return;

            _water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _water.name = "Stage Flood Water";
            _water.layer = 2; // Ignore Raycast.
            Collider collider = _water.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            Material material = Resources.Load<Material>("Arts/Material/Stage Flood Transparent Blue");

            MeshRenderer renderer = _water.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _water.SetActive(false);
        }
    }
}
