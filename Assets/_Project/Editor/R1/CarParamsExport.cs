using System.IO;
using System.Text;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5a a2：Tools/Worksite/Export Car Params：RoofSceneDef → Assets/_Project/Worksite/Data/car_params.json（Blender 只讀這份）。
    /// 另寫 car_params.hash（RoofSceneDef.ParamsHash）：測試比對資料和匯出檔是否一致。
    /// 產生模型："&lt;Blender&gt;\blender.exe" -b --factory-startup -P Tools/Blender/gen_700t/gen_700t.py -- --params "Assets/_Project/Worksite/Data/car_params.json" --out "Assets/_Project/Worksite/Models/Generated"
    /// </summary>
    public static class CarParamsExport
    {
        public const string JsonPath = "Assets/_Project/Worksite/Data/car_params.json";
        public const string HashPath = "Assets/_Project/Worksite/Data/car_params.hash.txt";

        [MenuItem("Tools/Worksite/Export Car Params")]
        public static void Export()
        {
            var def = R1Data.RoofScene;
            Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
            File.WriteAllText(JsonPath, def.ToParamsJson(), new UTF8Encoding(false));
            File.WriteAllText(HashPath, def.ParamsHash(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(JsonPath);
            AssetDatabase.ImportAsset(HashPath);
            Debug.Log($"[CarParams] {JsonPath}（hash {def.ParamsHash()}）");
        }
    }
}
