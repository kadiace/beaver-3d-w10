using System;
using System.Collections.Generic;
using UnityEngine;

namespace RestoreCastle3D
{
    [Serializable]
    public sealed class CastleMaterial3D
    {
        public string id;
        public string name;
        public string category;
        public bool startOnly;
        public bool spawnOnly;
        public int baseReinforcement;
        public float bondStrength;
        public string[] abilities;
    }

    [Serializable]
    public sealed class CastleMaterialFile3D
    {
        public int schemaVersion;
        public CastleMaterial3D[] materials;
    }

    public sealed class CastleCatalog3D
    {
        private readonly Dictionary<string, CastleMaterial3D> _materials = new Dictionary<string, CastleMaterial3D>();
        public CastleMaterial3D[] All { get; private set; }

        /// <summary>2D 원본에서 이관한 자재 정의를 읽어 ID 조회 테이블을 구성한다.</summary>
        public CastleCatalog3D()
        {
            TextAsset text = Resources.Load<TextAsset>("Data/materials");
            if (text == null) throw new InvalidOperationException("Missing Resources/Data/materials.json");
            CastleMaterialFile3D file = JsonUtility.FromJson<CastleMaterialFile3D>(text.text);
            if (file == null || file.materials == null || file.materials.Length == 0)
                throw new InvalidOperationException("Material catalog is empty");
            All = file.materials;
            foreach (CastleMaterial3D info in All) _materials.Add(info.id, info);
        }

        /// <summary>
        /// material이 현재 게임의 자재 선택지에 등장할 수 있는지 반환한다.
        /// 시작 전용, 자동 생성 전용, 성장 덩쿨 및 만능 접착제는 제외한다.
        /// </summary>
        public static bool CanAppearAsOffer(CastleMaterial3D material)
        {
            return !material.startOnly && !material.spawnOnly &&
                material.id != "growing_vine" && material.id != "all_purpose_glue";
        }

        /// <summary>자재 ID에 일치하는 항목을 반환한다.</summary>
        public CastleMaterial3D Get(string id) => _materials.TryGetValue(id, out CastleMaterial3D info) ? info : null;
    }
}
