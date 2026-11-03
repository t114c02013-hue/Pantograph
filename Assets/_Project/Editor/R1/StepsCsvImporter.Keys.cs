using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>R1.5b b4：Data/Source/BlackboardKeys.json（鍵的種類 LiveState／Event）→ Resources/BlackboardKeys.asset。kind 只能是這兩種，寫錯就匯入失敗。</summary>
    public static partial class StepsCsvImporter
    {
        public static string BlackboardKeysPath => SourceFolder + "/BlackboardKeys.json";
        public const string BlackboardKeysAssetPath = "Assets/_Project/Modules/Procedures/Resources/BlackboardKeys.asset";

        [Serializable] class KeyJson { public string pattern, kind, note; }
        [Serializable] class KeysFile { public KeyJson[] keys = Array.Empty<KeyJson>(); }

        /// <summary>讀 JSON 建成 BlackboardKeyDef（不存檔）；kind 不是 Event／LiveState 的寫進 errors。</summary>
        public static BlackboardKeyDef BuildKeyDef(string json, List<string> errors)
        {
            var src = JsonUtility.FromJson<KeysFile>(json);
            var def = ScriptableObject.CreateInstance<BlackboardKeyDef>();
            def.keys = src.keys.Select((k, i) =>
            {
                if (!Enum.TryParse<BlackboardKeyKind>(k.kind, out var kind) || !Enum.IsDefined(typeof(BlackboardKeyKind), kind))
                    errors.Add($"BlackboardKeys.json 第 {i + 1} 項（{k.pattern}）：kind 只能是 Event 或 LiveState，寫的是「{k.kind}」");
                return new BlackboardKeyDef.Entry { pattern = k.pattern, kind = kind, note = k.note ?? "" };
            }).ToArray();
            return def;
        }

        public static BlackboardKeyDef LoadKeyDefSource(List<string> errors) => BuildKeyDef(File.ReadAllText(BlackboardKeysPath, Encoding.UTF8), errors);
    }
}
