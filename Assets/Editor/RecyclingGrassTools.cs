using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

namespace BeaverRecycling.Editor
{
    public static class RecyclingGrassTools
    {
        private const string ENVIRONMENT_PATH = "Assets/Resources/Prefabs/CastleCreekEnvironment.prefab";
        private const string MESH_PATH = "Assets/Resources/Arts/Modeling/BankGrassConformed.asset";
        private const string MATERIAL_PATH = "Assets/Resources/Materials/Recycling/Grass.mat";

        /// <summary>환경 프리팹의 잔디 지형 삼각형을 읽어 뿌리가 표면에 닿는 장식 잔디 메시를 다시 저장한다.</summary>
        [MenuItem("Tools/Beaver/Rebuild Terrain Grass")]
        private static void Rebuild()
        {
            if (EditorApplication.isPlaying) return;
            GameObject root = PrefabUtility.LoadPrefabContents(ENVIRONMENT_PATH);
            try
            {
                MeshFilter target = null;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name == "Planted Bank Grass") target = filter;
                if (target == null) throw new System.InvalidOperationException("Planted Bank Grass is missing from the environment prefab.");
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var indices = new List<int>();
                var random = new System.Random(7917);
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter == target || !filter.name.Contains("Grass") || filter.sharedMesh == null) continue;
                    Vector3[] source = filter.sharedMesh.vertices;
                    int[] triangles = filter.sharedMesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = filter.transform.TransformPoint(source[triangles[i]]);
                        Vector3 b = filter.transform.TransformPoint(source[triangles[i + 1]]);
                        Vector3 c = filter.transform.TransformPoint(source[triangles[i + 2]]);
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        if (cross.sqrMagnitude < 0.00001f || cross.normalized.y < 0.4f) continue;
                        int count = (int)(Mathf.Abs(cross.y) * 0.5f * 0.35f + random.NextDouble());
                        for (int sample = 0; sample < count; sample++)
                        {
                            float u = Mathf.Sqrt((float)random.NextDouble());
                            float v = (float)random.NextDouble();
                            Vector3 point = (1f - u) * a + u * (1f - v) * b + u * v * c + Vector3.up * 0.012f;
                            Vector3 local = root.transform.InverseTransformPoint(point);
                            bool near = Mathf.Abs(local.x) <= 35f && local.z >= -25f && local.z <= 55f;
                            if (!near && random.NextDouble() > 0.05) continue;
                            AddTuft(target.transform, point, random, vertices, normals, uvs, indices);
                        }
                    }
                }
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
                if (mesh == null) { mesh = new Mesh { name = "BankGrassConformed" }; AssetDatabase.CreateAsset(mesh, MESH_PATH); }
                mesh.Clear();
                mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                target.sharedMesh = mesh;
                MeshRenderer renderer = target.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
                renderer.receiveShadows = true;
                EditorUtility.SetDirty(mesh);
                PrefabUtility.SaveAsPrefabAsset(root, ENVIRONMENT_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log("Rebuilt terrain-conforming bank grass: " + vertices.Count + " vertices.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>지형 표면 위치와 난수를 받아 세 장의 양면 잎으로 잔디 한 포기를 메시 버퍼에 추가한다.</summary>
        private static void AddTuft(Transform target, Vector3 point, System.Random random,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int blade = 0; blade < 3; blade++)
            {
                float direction = angle + blade * Mathf.PI / 3f;
                Vector3 side = new Vector3(Mathf.Cos(direction), 0f, Mathf.Sin(direction)) *
                    (0.045f + (float)random.NextDouble() * 0.025f);
                Vector3 a = target.InverseTransformPoint(point - side);
                Vector3 b = target.InverseTransformPoint(point + Vector3.up *
                    (0.16f + (float)random.NextDouble() * 0.22f) + side * 0.4f);
                Vector3 c = target.InverseTransformPoint(point + side);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c });
                normals.AddRange(new[] { normal, normal, normal });
                uvs.AddRange(new[] { Vector2.zero, new Vector2(0.5f, 1f), Vector2.right });
                indices.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 1, start });
            }
        }
    }
}
