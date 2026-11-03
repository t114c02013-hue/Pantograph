using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4 匯入器（第 2 部分）：完成條件三層對應、目標 ID 檢查、產生兩門課的 StepDefinition／ProcedureDefinition。
    /// 1. R1 既有小動作（carbon_strip_slice.json，步驟 ID＋步驟內順序）沿用；定期的同一列條件全文和日常相同時跟著日常。
    /// 2. ConditionTables.json 的 patterns（正規表示式）。3. exact（全文，可加 step／target 篩選）。
    /// 三層都對不到、佔位沒有值、目標不在場景，就把行號和內容全部列進 errors，不產生資產。
    /// </summary>
    public static partial class StepsCsvImporter
    {
        public static string TablesPath => SourceFolder + "/ConditionTables.json";
        public const string CourseFolder = "Assets/_Project/Modules/Procedures/Data/Courses";
        public static string ProcedurePath(string course) => $"{CourseFolder}/PRC_{course}.asset";

        [Serializable]
        public class ConditionTables
        {
            public Reuse[] reuseExcluded = Array.Empty<Reuse>();
            public Reuse[] reuseTargetChanged = Array.Empty<Reuse>();
            public AutoCompleteAllowedDef[] autoCompleteAllowed = Array.Empty<AutoCompleteAllowedDef>();
            public CompletionOverride[] completionOverrides = Array.Empty<CompletionOverride>();
            /// <summary>R1 步驟的完成條件不能照用時（例：K3 扭力不足要能完成步驟、在完成時判錯），以這份取代。step 是前綴（_L、_R 都套）。</summary>
            [Serializable] public class CompletionOverride { public string step, courses = "both", why; public RuleJson completion; }
            public Pattern[] patterns = Array.Empty<Pattern>();
            public Exact[] exact = Array.Empty<Exact>();
            public Container[] targetContainers = Array.Empty<Container>();
            public Pending[] pendingTargets = Array.Empty<Pending>();
            [Serializable] public class Reuse { public string step, sub, why; }
            [Serializable] public class Pattern { public string re, rule, owner; }
            [Serializable] public class Exact { public string text, step, target, rule, owner; }
            [Serializable] public class Container { public string prefix, container, why; }
            [Serializable] public class Pending { public string id, owner; }
        }

        public static ConditionTables LoadTables() => JsonUtility.FromJson<ConditionTables>(File.ReadAllText(TablesPath, Encoding.UTF8));

        /// <summary>一個小動作對應到的完成條件：layer 1／2／3，owner＝寫入這些鍵的機制還沒做時由哪一段做。</summary>
        public class Resolved
        {
            public Expanded e;
            public string rule, owner = "";
            public int layer;
        }

        /// <summary>目標欄（分號分隔）去掉「（…）」部位說明後的 ID。</summary>
        public static List<string> Targets(string targetField) =>
            (targetField ?? "").Split(';').Select(t => { int i = t.IndexOf('（'); return (i < 0 ? t : t.Substring(0, i)).Trim(); }).Where(t => t.Length > 0).ToList();

        /// <summary>R1 的步驟（carbon_strip_slice.json）：v6 的 _L／_R 對到 R1 用 side 重複的同一步。</summary>
        public static StepJson R1Step(Dictionary<string, StepJson> r1, string stepId)
        {
            if (r1.TryGetValue(stepId, out var s)) return s;
            var m = Regex.Match(stepId, "^(.*)_(L|R)$");
            return m.Success && r1.TryGetValue(m.Groups[1].Value, out s) ? s : null;
        }

        public static Dictionary<string, StepJson> LoadR1() =>
            JsonUtility.FromJson<ProcedureJson>(File.ReadAllText(StepJsonImporter.JsonPath, Encoding.UTF8)).steps.ToDictionary(s => s.stepId);

        /// <summary>規則字串的佔位：$1…、{t0}{t1}、{tool}、{slot}、{n}、{side}。缺值回傳 null 並寫 error。</summary>
        public static string Fill(string rule, Expanded e, Match m, out string error)
        {
            error = null;
            var r = rule;
            if (m != null) for (int g = m.Groups.Count - 1; g >= 1; g--) r = r.Replace("$" + g, m.Groups[g].Value);
            var ts = Targets(e.targetId);
            var tools = (e.toolId ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var slot = ts.Count > 0 ? Regex.Match(ts[0], @"slot_(\d+)$") : Match.Empty;
            var vals = new Dictionary<string, string>
            {
                { "{t0}", ts.ElementAtOrDefault(0) }, { "{t1}", ts.ElementAtOrDefault(1) }, { "{tool}", tools.FirstOrDefault() },
                { "{slot}", slot.Success ? slot.Groups[1].Value : null }, { "{n}", e.n > 0 ? e.n.ToString() : null }, { "{side}", string.IsNullOrEmpty(e.side) ? null : e.side },
            };
            foreach (var kv in vals.Where(kv => r.Contains(kv.Key)))
            {
                if (kv.Value == null) { error = $"規則「{rule}」要 {kv.Key}，這一列沒有"; return null; }
                r = r.Replace(kv.Key, kv.Value);
            }
            return r;
        }

        /// <summary>三層對應（純函式，ImporterTests 直接測）。對不到的列寫進 errors（行號＋課程＋條件全文）。</summary>
        public static List<Resolved> Resolve(List<Expanded> all, Dictionary<string, StepJson> r1, ConditionTables t, List<string> errors)
        {
            var excluded = new HashSet<string>(t.reuseExcluded.Select(x => x.step + "|" + x.sub));
            var regex = t.patterns.Select(p => (p, re: new Regex(p.re))).ToList();
            var byDaily = new Dictionary<string, Resolved>();
            var list = new List<Resolved>();
            foreach (var e in all.OrderBy(x => x.course == "daily" ? 0 : 1))   // 日常先：定期同一列全文相同時跟著日常的第 1 層
            {
                var res = new Resolved { e = e };
                var r1s = R1Step(r1, e.stepId);
                if (e.course == "daily" && r1s != null && e.fullOrder <= r1s.subActions.Length && !excluded.Contains(e.stepId + "|" + e.subId))
                {
                    var src = r1s.subActions[e.fullOrder - 1];
                    res.layer = 1; res.rule = (src.done ?? "").Replace("{side}", e.side ?? "");
                    string r1t = Regex.Replace((src.target ?? "").Replace("{side}", e.side ?? ""), @"\.slot_(\*|\d+)$", ""), v6t = Targets(e.targetId).FirstOrDefault() ?? "";
                    bool same = r1t.Length == 0 || r1t == v6t || v6t.StartsWith(r1t + ".") || r1t.StartsWith(v6t + ".");
                    if (!same && !t.reuseTargetChanged.Any(x => e.stepId.StartsWith(x.step) && x.sub == e.subId))
                        { errors.Add($"{e}：沿用 R1 第 {e.fullOrder} 個小動作，但 R1 目標 {src.target} 和 v6 目標 {v6t} 不同（CSV 插列錯位？目標真的改了就加進 reuseTargetChanged）"); continue; }
                }
                else if (e.course == "periodic" && byDaily.TryGetValue(e.stepId + "|" + e.subId, out var d) && d.layer == 1 && d.e.condition == e.condition)
                { res.layer = 1; res.rule = d.rule; }
                else
                {
                    string err = null;
                    var hit = regex.Select(x => (x.p, m: x.re.Match(e.condition))).FirstOrDefault(x => x.m.Success);
                    if (hit.p != null) { res.layer = 2; res.rule = Fill(hit.p.rule, e, hit.m, out err); res.owner = hit.p.owner ?? ""; }
                    else
                    {
                        var t0 = Targets(e.targetId).FirstOrDefault() ?? "";
                        var ex = t.exact.Where(x => x.text == e.condition && (string.IsNullOrEmpty(x.step) || e.stepId.StartsWith(x.step)) && (string.IsNullOrEmpty(x.target) || x.target == t0))
                            .OrderBy(x => string.IsNullOrEmpty(x.step) && string.IsNullOrEmpty(x.target) ? 1 : 0).FirstOrDefault();
                        if (ex != null) { res.layer = 3; res.rule = Fill(ex.rule, e, null, out err); res.owner = ex.owner ?? ""; }
                        else err = "三層都對不到";
                    }
                    if (err != null) { errors.Add($"{e}：{err}；條件「{e.condition}」"); continue; }
                }
                if (string.IsNullOrEmpty(res.rule)) { errors.Add($"{e}：完成條件是空的"); continue; }
                if (e.course == "daily") byDaily[e.stepId + "|" + e.subId] = res;
                list.Add(res);
            }
            return list;
        }

        /// <summary>目標 ID 檢查：場景 ISimObject、工具、容器（控制盤、作業單面板），或 pendingTargets（還沒做，回傳 owner）。都不是就寫 error。</summary>
        public static void CheckTargets(IEnumerable<Expanded> all, ICollection<string> sceneIds, ICollection<string> toolIds, ConditionTables t, List<string> errors)
        {
            var pending = t.pendingTargets.ToDictionary(p => p.id, p => p.owner);
            foreach (var e in all)
                foreach (var id in Targets(e.targetId))
                {
                    bool ok = sceneIds.Contains(id) || toolIds.Contains(id) || pending.ContainsKey(id)
                        || t.targetContainers.Any(c => id.StartsWith(c.prefix) && sceneIds.Contains(c.container));
                    if (!ok) errors.Add($"{e}：目標 {id} 不在場景");
                }
        }

        /// <summary>
        /// 產生一門課的步驟（不存檔）：R1 有的步驟沿用 R1 的完成條件、提示、錯誤；新步驟沒有額外的完成條件（小動作依序完成就是完成）。
        /// 標題、參考時間讀 Steps.csv，預設完成一律關掉（K1）。
        /// </summary>
        public static List<StepDefinition> BuildCourse(string course, List<Resolved> resolved, List<StepMeta> metas, Dictionary<string, StepJson> r1)
        {
            var steps = new List<StepDefinition>();
            foreach (var m in metas.Where(x => InCourse(x.courses, course)).OrderBy(x => course == "periodic" ? x.orderPeriodic : x.orderDaily))
            {
                var subs = resolved.Where(x => x.e.course == course && x.e.stepId == m.stepId).OrderBy(x => x.e.order).ToList();
                var r1s = R1Step(r1, m.stepId);
                var st = r1s != null ? ProcedureJsonConverter.BuildStep(r1s) : ScriptableObject.CreateInstance<StepDefinition>();
                var side = Regex.Match(m.stepId, "_(L|R)$");
                st.name = m.stepId; st.stepId = m.stepId; st.title = m.title;
                st.side = side.Success ? side.Groups[1].Value : r1s?.side ?? "";
                st.presetDone = false; st.presetStates = Array.Empty<string>();
                st.refSecondsTutorial = course == "periodic" ? m.teachP : m.teachD;
                st.refSecondsExam = course == "periodic" ? m.examP : m.examD;
                st.subActions = subs.Select(x => new SubAction { text = x.e.text, why = "", targetId = x.e.targetId, toolId = x.e.toolId, condition = x.rule, standPointId = x.e.standPointId, subId = x.e.subId, textDesktop = x.e.textDesktop == x.e.text ? "" : x.e.textDesktop }).ToArray();
                st.hints = st.hints ?? new HintSet();
                st.hints.checklist = subs.Select(x => x.e.text + "|" + x.rule).ToArray();   // 清單跟 v6 小動作（R1 的清單少了新增的列，文字也還是車頂工具箱）
                var ov = LoadTables().completionOverrides.FirstOrDefault(o => m.stepId.StartsWith(o.step) && InCourse(o.courses, course));
                if (ov != null) st.completion = ProcedureJsonConverter.BuildRule(ov.completion);
                if (r1s == null)
                {
                    st.instruction = m.title;
                    st.targetIds = subs.SelectMany(x => Targets(x.e.targetId)).Distinct().ToArray();
                    st.completion = new ChecklistAll();   // 小動作依序完成就是完成（同時成立會矛盾：SI_06 d 兩個都扣、e 只剩車頂）
                }
                steps.Add(st);
            }
            return steps;
        }

        /// <summary>全部做一次（不存檔）：回傳兩門課的步驟；errors 有東西就不能存。sceneIds＝場景 ISimObject 的 ID。</summary>
        public static Dictionary<string, List<StepDefinition>> BuildAll(ICollection<string> sceneIds, List<string> errors, out List<Resolved> resolved)
        {
            var subRows = LoadSubActionRows(); var stepRows = LoadStepRows();
            var ex = Expand(subRows, errors);
            var metas = Steps(stepRows, errors);
            var defs = LoadErrorRules();
            CheckErrorRefs(subRows, stepRows, defs, errors);
            UnityEngine.Object.DestroyImmediate(LoadKeyDefSource(errors));   // 鍵的種類只能是 Event／LiveState
            var t = LoadTables(); var r1 = LoadR1();
            resolved = Resolve(ex, r1, t, errors);
            var toolIds = new HashSet<string>(R1Data.ToolSlots.slots.Select(s => s.toolId));
            CheckTargets(ex, sceneIds, toolIds, t, errors);
            var res = resolved;
            var courses = Courses.ToDictionary(c => c, c => BuildCourse(c, res, metas, r1));
            var stepErrors = metas.ToDictionary(m => m.stepId, m => m.errors.Select(x => x.id).ToList());
            foreach (var st in courses.Values.SelectMany(x => x)) ApplyErrorDefs(st, defs, stepErrors[st.stepId], errors);
            CheckCourses(LoadCourses(), sceneIds, courses, errors);   // 第 4 部分：課程擺放、onPass、備品架排列
            return courses;
        }

        [MenuItem("Tools/Import Steps")]
        public static void ImportMenu() => Debug.Log("[StepsCsvImporter]\n" + Import());

        /// <summary>開切片場景取 ID，對應、檢查，沒有錯誤才存資產（內容沒變的資產不重寫）。</summary>
        public static string Import()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SliceSceneBuilder.ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath, OpenSceneMode.Additive);
            List<string> ids;
            try { ids = SceneIds(scene); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            return Import(ids);
        }

        public static List<string> SceneIds(UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => ((Component)o).gameObject.scene == scene)
                .Select(o => o.Id).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

        public static string Import(ICollection<string> sceneIds)
        {
            var errors = new List<string>();
            var courses = BuildAll(sceneIds, errors, out var resolved);
            var sb = new StringBuilder();
            if (errors.Count > 0)
            {
                sb.AppendLine($"匯入失敗：{errors.Count} 個問題，沒有產生資產");
                foreach (var e in errors) sb.AppendLine("・" + e);
                return sb.ToString();
            }
            foreach (var kv in courses)
            {
                var saved = kv.Value.Select(s => SaveAsset(s, $"{CourseFolder}/{kv.Key}/{s.stepId}.asset")).ToList();
                var prc = ScriptableObject.CreateInstance<ProcedureDefinition>();
                prc.procedureId = "PRC_" + kv.Key; prc.title = kv.Key; prc.steps = saved;
                prc.requireFreshWrite = true;   // 軟體工程裁定 2026-11-03：小動作要啟用後真的做了動作才算完成
                prc.autoCompleteAllowed = LoadTables().autoCompleteAllowed;
                prc.globalErrors = GlobalErrors(kv.Key, LoadErrorRules());   // 3c：天車安全等全程監看
                var cj = LoadCourses().courses.First(x => x.courseId == kv.Key);
                prc.title = cj.title; prc.onPass = cj.onPass;   // 第 4 部分：CSR_05 合格跳轉
                var savedPrc = SaveAsset(prc, ProcedurePath(kv.Key));
                var course = ScriptableObject.CreateInstance<CourseDef>();
                course.courseId = cj.courseId; course.title = cj.title; course.procedure = savedPrc; course.placements = cj.placements; course.onPass = cj.onPass;
                course.liftCapAboveO = cj.liftCapAboveO; course.contactGapM = cj.contactGapM; course.craneParked = cj.craneParked; course.initialStates = cj.initialStates;
                SaveAsset(course, CourseDefPath(kv.Key));
                var rs = resolved.Where(r => r.e.course == kv.Key).ToList();
                sb.AppendLine($"{kv.Key}：步驟 {saved.Count}、小動作 {rs.Count}（第 1 層 {rs.Count(r => r.layer == 1)}、第 2 層 {rs.Count(r => r.layer == 2)}、第 3 層 {rs.Count(r => r.layer == 3)}；機制待做 {rs.Count(r => r.owner.Length > 0)}）→ {CourseFolder}/{kv.Key}");
            }
            const string policyPath = "Assets/_Project/Modules/Procedures/Resources/ExamPolicy.asset";   // K3：預設 k3Ready=false，組長試玩後改資料
            if (AssetDatabase.LoadAssetAtPath<ExamPolicyDef>(policyPath) == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(policyPath));
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ExamPolicyDef>(), policyPath);
            }
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<ExamPolicyDef>(policyPath));
            SaveAsset(LoadKeyDefSource(new List<string>()), BlackboardKeysAssetPath);   // 鍵的種類（LiveState／Event）
            var tr = R1Data.Teleport;   // D23：日常更換不開放 tp.platform.crane（不出現在傳送點清單、傳送擋下）
            if (!tr.blocks.Any(b => b != null && b.condition == SimIds.CourseCraneParked))
            {
                tr.blocks = tr.blocks.Concat(new[] { new TeleportBlock { condition = SimIds.CourseCraneParked, stationPrefix = "tp.platform.crane", message = "日常更換不使用天車", hidden = true } }).ToArray();
                EditorUtility.SetDirty(tr);
            }
            var spare = ScriptableObject.CreateInstance<SpareArrangementDef>();
            spare.entries = LoadCourses().spareArrangement;
            SaveAsset(spare, SpareArrangementPath);   // 備品架 L、R 排列（負責人裁定）
            const string toolRulesPath = "Assets/_Project/Modules/Tools/_Common/Resources/ToolRules.asset";   // 放開握把的規則（gripReleaseDrops、寬限秒數）
            if (AssetDatabase.LoadAssetAtPath<ToolRulesDef>(toolRulesPath) == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toolRulesPath));
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ToolRulesDef>(), toolRulesPath);
            }   // 新增的欄位（例：k3DowngradeModes）寫進資產，組長看得到、改得到
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(MapReportPath, MapReport(resolved), new UTF8Encoding(true));
            sb.AppendLine("每列的對應 → " + MapReportPath);
            return sb.ToString();
        }

        /// <summary>
        /// 存資產（GUID 不變）：內容沒變不寫；有變時先存到暫存檔，再把檔案內容蓋過去重新匯入。
        /// 不用 CopySerialized：[SerializeReference] 的圖形改變（例：完成條件 AllOf→OrderedSequence）時，覆寫既有物件後 SaveAssets 會讓 Unity 當掉（2022.3.62f3 實測兩次）。
        /// </summary>
        static T SaveAsset<T>(T obj, string path) where T : ScriptableObject
        {
            obj.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(obj, path);
                return obj;
            }
            string Norm(UnityEngine.Object o) => Regex.Replace(EditorJsonUtility.ToJson(o), "\"(rid|m_Name)\":\\s*(-?\\d+|\"[^\"]*\")", "");
            if (Norm(obj) == Norm(existing)) { UnityEngine.Object.DestroyImmediate(obj); return existing; }
            string tmp = Path.GetDirectoryName(path).Replace('\\', '/') + "/_import_tmp.asset";
            AssetDatabase.CreateAsset(obj, tmp);
            AssetDatabase.SaveAssets();
            File.Copy(tmp, path, true);
            File.WriteAllText(path, File.ReadAllText(path).Replace("m_Name: " + Path.GetFileNameWithoutExtension(tmp), "m_Name: " + Path.GetFileNameWithoutExtension(path)));   // CreateAsset 把名稱設成暫存檔名
            AssetDatabase.DeleteAsset(tmp);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public const string MapReportPath = "Logs/ConditionMap.csv";

        /// <summary>每個小動作對應到哪一層、哪條規則（給驗收組長逐列看；UTF-8 BOM）。</summary>
        public static string MapReport(List<Resolved> resolved)
        {
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder("course,stepId,subId,line,layer,condition,rule,owner\n");
            foreach (var r in resolved.OrderBy(x => x.e.course).ThenBy(x => x.e.row.line).ThenBy(x => x.e.stepId).ThenBy(x => x.e.order))
                sb.AppendLine(string.Join(",", r.e.course, r.e.stepId, r.e.subId, r.e.row.line, r.layer, Q(r.e.condition), Q(r.rule), r.owner));
            return sb.ToString();
        }
    }
}
