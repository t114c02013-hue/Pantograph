using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.XR
{
    /// <summary>
    /// R1.5b b1 天車遙控器（crane-software v0.5 §2.1，D18）。只經 CraneControl 寫 ctrl.crane.*，不引用天車機構。
    /// ・拿起／掛回：掛座 PendantDock 周圍 pendantDockRadius 的球是左手 PanelHandle 層的 XRSimpleInteractable，按一下左握把就切換（附著在左手，之後不必一直握著）。
    ///   不在掛座旁按左握把對遙控器沒作用，所以左握把照常可以抓面板把手；遙控器不會掉落。
    /// ・按鍵：右手射線指著按鍵＋右扳機，或右手 Poke_Tip 碰到按鍵；方向鍵按住才動、放開就停、同時按兩個就停（CraneLogic.Resolve）。
    /// ・拿著時，作業單打開或左手正在選取任何其他物件（例：雙手握長桿的前手），遙控器隱藏且所有按鍵放開，放開後再出現。
    /// ・桌面版：點遙控器拿起，按鍵面板在 UI（CranePanelView）。
    /// </summary>
    public class XrCranePendant : MonoBehaviour
    {
        public static readonly string[] Buttons = { "Btn_Up", "Btn_Down", "Btn_East", "Btn_West", "Btn_North", "Btn_South", "Btn_Speed2", "Btn_EStop", "Key_Power" };

        [SerializeField] XrRig rig;
        [SerializeField] CraneProfile profile;
        [SerializeField] Transform pendant, dock, rightPoke;
        [SerializeField] Vector3 heldLocalPosition = new Vector3(0f, 0.02f, 0.08f), heldLocalEuler = new Vector3(-30f, 0f, 0f);
        readonly Dictionary<string, Collider> buttonColliders = new Dictionary<string, Collider>();
        readonly HashSet<string> wasPressed = new HashSet<string>();
        Vector3 dockedPos; Quaternion dockedRot; Transform dockedParent;
        XRSimpleInteractable dockInteractable;
        Renderer speedRenderer; TextMesh speedLabel; AudioSource beep; bool? lastFast;
        MaterialPropertyBlock mpb;
        Renderer[] renderers = new Renderer[0];

        public void Setup(XrRig r, CraneProfile p, Transform pendantT, Transform dockT, Transform rightPokeT)
        {
            rig = r; profile = p; pendant = pendantT; dock = dockT; rightPoke = rightPokeT;
        }

        void Start()
        {
            if (pendant == null || dock == null) { enabled = false; return; }
            dockedParent = pendant.parent; dockedPos = pendant.position; dockedRot = pendant.rotation;
            renderers = pendant.GetComponentsInChildren<Renderer>(true);
            foreach (var n in Buttons)
            {
                var t = pendant.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                if (t == null) continue;
                var c = t.GetComponent<Collider>();   // 建置時（RoofSiteBuilder）補齊；缺了只略過這一顆，後面的設定照常做
                if (c == null) { Debug.LogError($"[XR] 天車遙控器 {n} 沒有 Collider，這顆按鍵按不到"); continue; }
                buttonColliders[n] = c;
            }
            // 中文標籤：Label_<按鍵> 錨點，文字從 CraneProfile.buttonLabels 讀
            foreach (var pair in profile != null ? profile.buttonLabels : new string[0])
            {
                int eq = pair.IndexOf('='); if (eq <= 0) continue;
                var kv = (Key: pair.Substring(0, eq), Value: pair.Substring(eq + 1));
                var a = pendant.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Label_" + kv.Key);
                if (a == null || a.GetComponentInChildren<TextMesh>() != null) continue;
                var tm = new GameObject("Text").AddComponent<TextMesh>();
                tm.transform.SetParent(a, false); tm.text = kv.Value; tm.characterSize = 0.004f; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleLeft; tm.color = Color.white;
            }
            speedRenderer = buttonColliders.TryGetValue("Btn_Speed2", out var sc2) ? sc2.GetComponent<Renderer>() : null;
            speedLabel = pendant.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Label_Btn_Speed2")?.GetComponentInChildren<TextMesh>();
            beep = pendant.gameObject.AddComponent<AudioSource>(); beep.playOnAwake = false; beep.spatialBlend = 0.5f;
            beep.clip = Beep();
            var zone = new GameObject("PendantDockZone");
            zone.transform.SetParent(dock, false);
            var sc = zone.AddComponent<SphereCollider>(); sc.isTrigger = true; sc.radius = profile != null ? profile.pendantDockRadius : 0.3f;
            dockInteractable = zone.AddComponent<XRSimpleInteractable>();
            dockInteractable.interactionLayers = InteractionLayerMask.GetMask(XrPanels.HandleLayerName);
            dockInteractable.selectEntered.AddListener(e =>
            {
                Toggle();
                dockInteractable.interactionManager?.SelectExit(e.interactorObject, dockInteractable);   // 按一下就切換，不維持選取
            });
            if (Sim.Bus != null) Sim.Bus.Subscribe<ActionRequested>(OnRequest);
            if (Sim.State != null && !Sim.State.Has(SimIds.CranePendantState)) CraneControl.SetPendant(Sim.State, false);   // 開場掛在掛座（負責人裁定）
        }

        void OnDestroy() { if (Sim.Bus != null) Sim.Bus.Unsubscribe<ActionRequested>(OnRequest); }

        /// <summary>桌面版：點遙控器＝拿起（掛回用按鍵面板上的按鈕）。</summary>
        void OnRequest(ActionRequested e)
        {
            if (InputMode.XrActive || e.targetId != "env.crane_pendant" || CraneControl.PendantHeld(Sim.State)) return;
            CraneControl.SetPendant(Sim.State, true);
        }

        /// <summary>在掛座範圍內按左握把：拿起或掛回。</summary>
        public void Toggle()
        {
            var s = Sim.State; if (s == null) return;
            CraneControl.SetPendant(s, !CraneControl.PendantHeld(s));
        }

        /// <summary>高速鍵的模式提示（軟體工程裁定）：按鍵發光顏色與標籤跟著變，切換時嗶一聲。顏色、文字讀 CraneProfile。</summary>
        void ShowSpeed(ISimState s)
        {
            bool fast = s.GetBool(SimIds.CraneFast);
            if (lastFast.HasValue && lastFast.Value != fast && beep != null && beep.clip != null) beep.PlayOneShot(beep.clip);
            lastFast = fast;
            if (profile == null) return;
            var c = fast ? profile.speedFastColor : profile.speedFineColor;
            if (speedRenderer != null) { mpb ??= new MaterialPropertyBlock(); speedRenderer.GetPropertyBlock(mpb); mpb.SetColor("_Color", c); mpb.SetColor("_EmissionColor", c); mpb.SetColor("_BaseColor", c); speedRenderer.SetPropertyBlock(mpb); }
            if (speedLabel != null) speedLabel.text = fast ? profile.speedFastLabel : profile.speedFineLabel;
        }

        /// <summary>短提示音（0.08 s、1 kHz 正弦）；不用外部音檔。</summary>
        static AudioClip Beep()
        {
            const int rate = 22050; int n = rate * 8 / 100;
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Sin(2f * Mathf.PI * 1000f * i / rate) * 0.3f * (1f - (float)i / n);
            var clip = AudioClip.Create("CraneSpeedBeep", n, 1, rate, false); clip.SetData(data, 0); return clip;
        }

        void LateUpdate()
        {
            var s = Sim.State; if (s == null || pendant == null) return;
            ShowSpeed(s);
            bool held = CraneControl.PendantHeld(s);
            bool xr = InputMode.XrActive && rig != null && rig.LeftHand != null;
            if (held && xr)
            {
                if (pendant.parent != rig.LeftHand) pendant.SetParent(rig.LeftHand, false);
                pendant.localPosition = heldLocalPosition; pendant.localRotation = Quaternion.Euler(heldLocalEuler);
            }
            else if (!held && pendant.parent != dockedParent)
            {
                pendant.SetParent(dockedParent, true); pendant.SetPositionAndRotation(dockedPos, dockedRot);
            }
            bool hidden = held && xr && (s.GetBool("ui.workorder_open") || LeftHandBusy());
            foreach (var r in renderers) if (r != null) r.enabled = !hidden;
            var pressed = held && xr && !hidden ? Pressed() : new HashSet<string>();
            foreach (var b in pressed.Where(b => !wasPressed.Contains(b)))
            {
                if (b == "Btn_EStop") CraneControl.PressEStop(s);
                else if (b == "Key_Power") CraneControl.TurnKey(s);
                else if (b == "Btn_Speed2") CraneControl.ToggleFast(s);
            }
            wasPressed.Clear(); foreach (var b in pressed) wasPressed.Add(b);
            if (held && xr) CraneControl.Command(s, CraneLogic.Resolve(pressed));
        }

        /// <summary>左手的 interactor 有沒有選取掛座以外的東西。</summary>
        bool LeftHandBusy() => rig.LeftHand.GetComponentsInChildren<XRBaseInteractor>().Any(i => i.hasSelection && !i.IsSelecting(dockInteractable));

        HashSet<string> Pressed()
        {
            var set = new HashSet<string>();
            var origin = rig.RightRayOrigin;
            if (PointerState.XrTriggerHeld && origin != null && Physics.Raycast(origin.position, origin.forward, out var hit, 2f, ~0, QueryTriggerInteraction.Collide))
                foreach (var kv in buttonColliders) if (hit.collider == kv.Value) set.Add(kv.Key);
            if (rightPoke != null)
                foreach (var kv in buttonColliders) if (kv.Value != null && (kv.Value.ClosestPoint(rightPoke.position) - rightPoke.position).sqrMagnitude < 0.008f * 0.008f) set.Add(kv.Key);
            return set;
        }
    }
}
