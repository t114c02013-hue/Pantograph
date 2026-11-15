using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Worksite;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>RA-29：建模師 A 的 7 個平台設備／外接電源 FBX（gen_depot_props v1.0）接進場景：節點對接、元件掛對地方；隔離燈號換模型後 84A 照樣對。</summary>
    public class DepotPropsTests
    {
        static Transform Find(UnityEngine.SceneManagement.Scene scene, string name, string parent = null) =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name && (parent == null || (t.parent != null && t.parent.name == parent)));

        [Test]
        public void Scene_SevenFbx_NodesWired()
        {
            StationTests.WithScene(scene =>
            {
                foreach (var root in new[] { "LiftControlBox", "IsolationStatusLight", "LotoBoard", "PlatformShelf", "ShorePowerBox", "ShorePlug", "ShoreSocket" })
                    Assert.IsNotNull(Find(scene, root), root + " 在場景裡");
                foreach (var b in new[] { "Btn_Up", "Btn_Down" })
                    Assert.IsNotNull(Find(scene, b, "LiftControlBox")?.GetComponent<PressFollow>(), b + " 掛 PressFollow");
                var lamp = Find(scene, "PanIndicator", "LiftControlBox").GetComponent<ControlPanelLamp>();
                Assert.IsNotNull(lamp, "弓位燈 ControlPanelLamp 掛在 FBX 的 PanIndicator");
                Assert.AreEqual("PanIndicator_Lens", ((Renderer)typeof(ControlPanelLamp).GetField("lamp", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lamp)).name);
                Assert.IsNotNull(Find(scene, "PanIndicator_Lens").GetComponent<LampLight>(), "點光源掛在燈罩上（LampLight.SetOn 找得到）");
                Assert.IsNotNull(Find(scene, "Switch_Supply", "ShorePowerBox").GetComponent<CabSwitch>(), "供電開關＝FBX 的 Switch_Supply");
                Assert.IsNotNull(Find(scene, "ShorePlug").GetComponent<ShorePlug>());
                for (int i = 1; i <= 4; i++) Assert.IsNotNull(Find(scene, "Tag_" + i)?.GetComponent<TextMesh>(), $"掛牌 {i} 有字");
                Assert.IsFalse(Find(scene, "BoltTray_Square_L").gameObject.activeSelf, "方盤沿用現有外觀，FBX 的方盤隱藏");
                Assert.AreEqual("PartsTray", Find(scene, "PartsTray_Model").parent.name, "零件盒外觀搬到互動物件下");
            });
        }

        [Test]
        public void IsolationLight_FbxLenses_RedThenGreenThenRedAgain()   // terry 84A：下車頂（roof.open 關）後紅燈不會發綠光
        {
            StationTests.WithScene(scene =>
            {
                var light = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<IsolationStatusLight>(true)).Single();
                var red = Find(scene, "StatusLamp_Red_Lens").GetComponent<Renderer>(); var green = Find(scene, "StatusLamp_Green_Lens").GetComponent<Renderer>();
                var update = typeof(IsolationStatusLight).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
                var shown = typeof(IsolationStatusLight).GetField("shown", BindingFlags.NonPublic | BindingFlags.Instance);
                ServiceLocator.Clear();
                var s = new SimState(new EventBus()); ServiceLocator.Register<ISimState>(s);
                try
                {
                    bool Reddish(Renderer r) => r.sharedMaterial.color.r > r.sharedMaterial.color.g + 0.3f;
                    bool Greenish(Renderer r) => r.sharedMaterial.color.g > r.sharedMaterial.color.r + 0.3f;
                    foreach (var (open, label) in new[] { (false, "隔離未完成"), (true, "隔離完成"), (false, "下車頂後") })
                    {
                        s.SetBool(LiftControl.RoofOpenKey, open); shown.SetValue(light, null); update.Invoke(light, null);
                        Assert.AreEqual(!open, Reddish(red), label + "：紅燈亮＝紅色");
                        Assert.AreEqual(open, Greenish(green), label + "：綠燈亮＝綠色");
                        Assert.IsFalse(Greenish(red), label + "：紅燈不會發綠光");
                    }
                }
                finally { ServiceLocator.Clear(); }
            });
        }
    }
}
