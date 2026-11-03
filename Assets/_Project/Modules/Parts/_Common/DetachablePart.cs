using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Parts.Common
{
    /// <summary>
    /// R1.5b b2 通用拆裝件（crane-software v0.5 §3.1）：高壓端子、控制線接頭、降弓固定帶。只做三件事：
    /// 狀態 Installed／Removed（寫黑板 &lt;id&gt;.state）、InstallSnap 吸附（裝回時離吸附點 ≤ snapRadius 才成立，成立後對齊到 0）、
    /// 可選的 requires（ChecklistEval 條件，全部成立才拿得下來，例：兩支 hv_bolt 都 Out）。子節點依狀態切換 active（對照寫在資料：installedNodes／removedNodes）。
    /// 碳滑板仍用 RemovableBehaviour（不改它的公開介面）。
    /// shortcut: 碳滑板仍用 RemovableBehaviour, R2 視重複程度合併成一個
    /// </summary>
    public class DetachablePart : PartBehaviour
    {
        public const string Installed = "Installed", Removed = "Removed";

        [Tooltip("拿下來之前要成立的條件（ChecklistEval：鍵、鍵=值、鍵>=數字）")] [SerializeField] string[] requires = Array.Empty<string>();
        [Tooltip("每個條件不成立時的說明（和 requires 同順序；空白就顯示條件本身）")] [SerializeField] string[] requireLabels = Array.Empty<string>();
        [SerializeField] GameObject[] installedNodes = Array.Empty<GameObject>(), removedNodes = Array.Empty<GameObject>();
        [SerializeField] Transform snapTarget;
        [Tooltip("InstallSnap 吸附半徑（m，第 2 部分 2.3：2 cm）")] [SerializeField] float snapRadius = 0.02f;
        [SerializeField] string initialState = Installed;
        [Tooltip("用工具拆裝時的工具 ID（例：降弓固定帶 tool.hold_down_strap）；空白＝只能徒手")] [SerializeField] string toolId = "";

        public void Setup(string[] requiresConditions, string[] labels, GameObject[] whenInstalled, GameObject[] whenRemoved, Transform snap, float radius, string initial, string tool = "")
        {
            toolId = tool ?? "";
            requires = requiresConditions ?? Array.Empty<string>(); requireLabels = labels ?? Array.Empty<string>();
            installedNodes = whenInstalled ?? Array.Empty<GameObject>(); removedNodes = whenRemoved ?? Array.Empty<GameObject>();
            snapTarget = snap; snapRadius = radius; initialState = initial;
        }

        public string StateValue => State != null ? State.Get(Id + ".state", initialState) : initialState;
        public float SnapRadius => snapRadius;

        protected override void OnBound() { if (!State.Has(Id + ".state")) State.Set(Id + ".state", initialState); }

        /// <summary>還缺哪些條件才拿得下來（空字串＝都成立）。</summary>
        public string Explain()
        {
            var s = State ?? Sim.State;
            var missing = new List<string>();
            for (int i = 0; i < requires.Length; i++)
                if (!ChecklistEval.Done(s, requires[i])) missing.Add(i < requireLabels.Length && !string.IsNullOrEmpty(requireLabels[i]) ? requireLabels[i] : requires[i]);
            return missing.Count == 0 ? "" : "還不能拆：" + string.Join("、", missing);
        }

        /// <summary>拆下：requires 全部成立才成功；不成立時寫 &lt;id&gt;.blocked＝Explain() 的說明。</summary>
        public bool Remove()
        {
            if (StateValue != Installed) return false;
            var why = Explain();
            if (why != "") { State.Set(Id + ".blocked", why); return false; }
            State.Batch(() => { State.Set(Id + ".state", Removed); State.Set(Id + ".blocked", ""); });
            return true;
        }

        /// <summary>裝回：放下的位置離吸附點 ≤ snapRadius 才成立（成立後對齊吸附點）；沒有吸附點就直接裝回。</summary>
        public bool Install(Vector3 releasedAt)
        {
            if (StateValue != Removed) return false;
            if (snapTarget != null && (releasedAt - snapTarget.position).magnitude > snapRadius + 1e-5f) return false;
            State.Set(Id + ".state", Installed);
            return true;
        }

        protected override void OnBareHand(ActionRequested e)
        {
            if (e.text == "inspect") return;
            if (StateValue == Installed) Remove();
            else Install(snapTarget != null ? snapTarget.position : transform.position);   // 點選＝放在吸附點
        }

        protected override void OnToolAction(ActionPerformed e)
        {
            if (string.IsNullOrEmpty(toolId) || e.toolId != toolId) return;
            State.Batch(() => OnBareHand(new ActionRequested { targetId = e.targetId, text = e.text }));
        }

        protected override bool WatchesKey(string key) => key == Id + ".state";

        protected override void RefreshVisual()
        {
            bool inst = StateValue == Installed;
            foreach (var g in installedNodes) if (g != null) g.SetActive(inst);
            foreach (var g in removedNodes) if (g != null) g.SetActive(!inst);
        }
    }
}
