using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

namespace RestoreCastle3D.Editor
{
    public sealed class CastleBondStrengthWindow : EditorWindow
    {
        private const string MATERIALS_PATH = "Assets/Resources/Data/materials.json";
        private const string OBJECT_PATTERN = @"\{(?:[^{}""]|""(?:\\.|[^""\\])*"")*\}";
        private const string STRENGTH_PATTERN = @"(""bondStrength""\s*:\s*)(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)";

        private float _bondStrength = 100f;
        private string _status;
        private MessageType _statusType;
        private List<string> _offerNames = new List<string>();
        private Vector2 _scroll;

        /// <summary>자재 결합강도 편집 창을 열어 입력과 대상 목록을 표시한다.</summary>
        [MenuItem("Tools/Restore Castle/Set Offer Bond Strength")]
        private static void OpenWindow()
        {
            GetWindow<CastleBondStrengthWindow>("결합강도 변경");
        }

        private void OnEnable()
        {
            RefreshOffers();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("선택지 자재 결합강도", EditorStyles.boldLabel);
            _bondStrength = EditorGUILayout.FloatField("자재별 결합강도", _bondStrength);
            EditorGUILayout.HelpBox("선택지에 나오는 자재만 변경합니다. 두 자재의 결합강도 합이 조인트의 파괴 강도가 됩니다.", MessageType.Info);

            if (GUILayout.Button("대상 목록 새로고침")) RefreshOffers();
            EditorGUILayout.LabelField("변경 대상", _offerNames.Count + "종");
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(100f));
            foreach (string name in _offerNames) EditorGUILayout.LabelField(name);
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Play Mode를 종료한 뒤 변경하세요.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("결합강도 적용")) ApplyStrength();
            }
        }

        /// <summary>자재 데이터 파일을 읽어 게임의 선택지 규칙에 맞는 이름과 상태를 갱신한다.</summary>
        private void RefreshOffers()
        {
            try
            {
                CastleMaterialFile3D file = ReadMaterials(File.ReadAllText(MATERIALS_PATH, Encoding.UTF8));
                _offerNames = GetOfferNames(file);
                _status = null;
            }
            catch (Exception exception)
            {
                _offerNames.Clear();
                _status = "자재 목록을 읽지 못했습니다: " + exception.Message;
                _statusType = MessageType.Error;
            }
        }

        /// <summary>json에서 자재 목록을 읽고 비어 있거나 ID가 중복되면 오류를 반환한다.</summary>
        private static CastleMaterialFile3D ReadMaterials(string json)
        {
            CastleMaterialFile3D file = JsonUtility.FromJson<CastleMaterialFile3D>(json);
            if (file == null || file.materials == null || file.materials.Length == 0)
                throw new InvalidDataException("자재 목록이 비어 있습니다.");

            HashSet<string> ids = new HashSet<string>();
            foreach (CastleMaterial3D material in file.materials)
                if (material == null || string.IsNullOrEmpty(material.id) || !ids.Add(material.id))
                    throw new InvalidDataException("자재 ID가 없거나 중복되어 있습니다.");
            return file;
        }

        /// <summary>file에서 실제 선택지 대상의 이름을 순서대로 반환한다.</summary>
        private static List<string> GetOfferNames(CastleMaterialFile3D file)
        {
            List<string> names = new List<string>();
            foreach (CastleMaterial3D material in file.materials)
                if (CastleCatalog3D.CanAppearAsOffer(material)) names.Add(material.name);
            return names;
        }

        /// <summary>
        /// 입력한 결합강도를 현재 선택지 자재에만 저장하고 에셋을 다시 가져온다.
        /// 원본 JSON의 나머지 필드와 서식은 유지하며 잘못된 데이터는 저장하지 않는다.
        /// </summary>
        private void ApplyStrength()
        {
            try
            {
                if (float.IsNaN(_bondStrength) || float.IsInfinity(_bondStrength) || _bondStrength < 0f)
                    throw new ArgumentException("결합강도는 0 이상의 유한한 값이어야 합니다.");

                byte[] bytes = File.ReadAllBytes(MATERIALS_PATH);
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
                string json = File.ReadAllText(MATERIALS_PATH, Encoding.UTF8);
                CastleMaterialFile3D file = ReadMaterials(json);
                string updated = ReplaceOfferStrengths(json, file, _bondStrength, out int count);
                if (count == 0) throw new InvalidDataException("변경할 선택지 자재가 없습니다.");
                File.WriteAllText(MATERIALS_PATH, updated, new UTF8Encoding(hasBom));
                AssetDatabase.ImportAsset(MATERIALS_PATH, ImportAssetOptions.ForceUpdate);
                _offerNames = GetOfferNames(file);
                _status = count + "종의 결합강도를 " + _bondStrength.ToString(CultureInfo.InvariantCulture) + "(으)로 변경했습니다.";
                _statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _status = "변경하지 못했습니다: " + exception.Message;
                _statusType = MessageType.Error;
            }
        }

        /// <summary>
        /// json의 자재별 객체와 file을 대조해 선택지의 결합강도만 strength로 치환한다.
        /// 치환된 JSON과 변경 대상 count를 반환하며 예상과 다른 구조는 거부한다.
        /// </summary>
        private static string ReplaceOfferStrengths(string json, CastleMaterialFile3D file, float strength, out int count)
        {
            MatchCollection objects = Regex.Matches(json, OBJECT_PATTERN);
            if (objects.Count != file.materials.Length)
                throw new InvalidDataException("자재 JSON 구조를 확인할 수 없습니다.");

            HashSet<string> seen = new HashSet<string>();
            string value = strength.ToString("R", CultureInfo.InvariantCulture);
            StringBuilder output = new StringBuilder(json.Length);
            int lastIndex = 0;
            int updatedCount = 0;
            for (int index = 0; index < objects.Count; index++)
            {
                Match item = objects[index];
                CastleMaterial3D material = JsonUtility.FromJson<CastleMaterial3D>(item.Value);
                if (material == null || material.id != file.materials[index].id || !seen.Add(material.id))
                    throw new InvalidDataException("자재 목록이 일치하지 않습니다.");

                output.Append(json, lastIndex, item.Index - lastIndex);
                if (CastleCatalog3D.CanAppearAsOffer(material))
                {
                    MatchCollection fields = Regex.Matches(item.Value, STRENGTH_PATTERN);
                    if (fields.Count != 1) throw new InvalidDataException(material.id + "의 결합강도를 확인할 수 없습니다.");
                    Match field = fields[0];
                    Group number = field.Groups[2];
                    output.Append(item.Value, 0, number.Index);
                    output.Append(value);
                    output.Append(item.Value, number.Index + number.Length, item.Value.Length - number.Index - number.Length);
                    updatedCount++;
                }
                else output.Append(item.Value);
                lastIndex = item.Index + item.Length;
            }
            output.Append(json, lastIndex, json.Length - lastIndex);
            count = updatedCount;
            return output.ToString();
        }
    }
}
