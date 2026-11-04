using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// steps-v6 v3.10（D44、吊帶割傷故障）：吊梁上的四條吊帶與吊具架、停用箱。點擊（徒手）轉給 SlingLogic：
    /// ip.sling_n.top_eye＝取下割傷吊帶、env.tag_out_of_service＝拿停用牌、ip.sling_n.tag＝掛停用牌、env.sling_reject_bin＝放進停用箱、
    /// env.rigging_rack＝放回／拿回吊具架（割傷吊帶）、env.sling_spare＝拿備用吊帶、ip.lifting_beam.eye_n＝掛上備用吊帶。
    /// 顯示：割傷那一條的 Defect_Cut、拆下後吊梁上那一條隱藏、拆下的那條（env.sling_rejected 的外觀）在停用箱或吊具架、備用吊帶在架上或已掛上。
    /// 狀態都在黑板（SlingLogic 的鍵），這裡只轉點擊與更新外觀。
    /// </summary>
    public class SlingRig : SiteObject
    {
        [Tooltip("吊梁上的吊帶 1–4（各自的不縮放根節點）")] [SerializeField] Transform[] slings = new Transform[0];
        [Tooltip("吊帶 1–4 的割傷小網格（故障時才開）")] [SerializeField] GameObject[] defects = new GameObject[0];
        [Tooltip("拆下的割傷吊帶的外觀（停用箱、吊具架兩個位置）")] [SerializeField] GameObject rejectedInBin, rejectedOnRack;
        [Tooltip("吊具架上的備用吊帶、停用牌")] [SerializeField] GameObject spareOnRack, tagOnRack;

        public void Setup(Transform[] slingRoots, GameObject[] cutMeshes, GameObject inBin, GameObject onRack, GameObject spare, GameObject tag)
        {
            slings = slingRoots; defects = cutMeshes; rejectedInBin = inBin; rejectedOnRack = onRack; spareOnRack = spare; tagOnRack = tag;
        }

        protected override bool Owns(string t) =>
            t == SlingLogic.Tag || t == SlingLogic.Bin || t == SlingLogic.Rack || t == SlingLogic.Spare
            || t.StartsWith("ip.sling_") || t.StartsWith("ip.lifting_beam.eye_");

        protected override bool Watches(string key) => key.StartsWith(SlingLogic.Prefix) || key.StartsWith(SlingLogic.Rejected) || key.StartsWith(SlingLogic.Tag);

        static int N(string t) => int.TryParse(t.Substring(t.LastIndexOf('_') + 1).Split('.')[0], out int n) ? n : 0;

        protected override void OnBareHand(ActionRequested e)
        {
            string t = e.targetId;
            if (e.text == "inspect") return;   // 查看由 InspectLogic（指著停一下）處理
            if (t == SlingLogic.Tag) SlingLogic.TakeTag(State);
            else if (t == SlingLogic.Bin) SlingLogic.PutInBin(State);
            else if (t == SlingLogic.Rack) SlingLogic.RackClick(State);
            else if (t == SlingLogic.Spare) SlingLogic.TakeSpare(State);
            else if (t.StartsWith("ip.lifting_beam.eye_")) SlingLogic.HangSpare(State, N(t));
            else if (t.EndsWith(".top_eye")) SlingLogic.Detach(State, N(t.Substring(0, t.Length - ".top_eye".Length)));
            else if (t.EndsWith(".tag")) SlingLogic.HangTag(State, N(t.Substring(0, t.Length - ".tag".Length)));
        }

        protected override void Refresh()
        {
            for (int n = 1; n <= SlingLogic.Count; n++)
            {
                bool detached = State.GetBool(SlingLogic.DetachedKey(n));
                if (n - 1 < slings.Length && slings[n - 1] != null) slings[n - 1].gameObject.SetActive(!detached);
                if (n - 1 < defects.Length && defects[n - 1] != null) defects[n - 1].SetActive(State.GetBool(SlingLogic.CutKey(n)) && !detached);
            }
            string st = State.Get(SlingLogic.Rejected + ".state");
            if (rejectedInBin != null) rejectedInBin.SetActive(st == SlingLogic.InBin);
            if (rejectedOnRack != null) rejectedOnRack.SetActive(st == SlingLogic.OnRack);
            if (spareOnRack != null) spareOnRack.SetActive(State.Get(SlingLogic.Spare + ".state", SlingLogic.OnRack) == SlingLogic.OnRack);
            if (tagOnRack != null) tagOnRack.SetActive(!State.GetBool(SlingLogic.Tag + ".in_hand") && !State.GetBool(SlingLogic.Rejected + ".tagged"));
        }
    }
}
