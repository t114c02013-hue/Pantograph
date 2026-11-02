using PantographSim.Core;
using UnityEngine;

namespace PantographSim.Tools
{
    /// <summary>
    /// 工具清點（WC_01 用）：工具都收回（徒手）時 tools.all_returned=true。
    /// 也提供程式產生的提示音（嗶聲、扭力扳手的喀聲），不需要音效檔。
    /// </summary>
    public class ToolInventory : MonoBehaviour
    {
        static AudioSource source;
        static AudioClip beep, click;
        bool bound;

        void Start()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            beep = Tone("beep", 1800f, 0.18f, false);
            click = Tone("click", 900f, 0.05f, true);
            if (!Sim.Ready) return;
            Sim.Bus.Subscribe<ToolSelected>(OnSelected);
            bound = true;
            Sim.State.SetBool("tools.all_returned", true);
        }

        void OnDestroy()
        {
            if (bound && Sim.Bus != null) Sim.Bus.Unsubscribe<ToolSelected>(OnSelected);
        }

        void OnSelected(ToolSelected e) => Sim.State.SetBool("tools.all_returned", string.IsNullOrEmpty(e.toolId));

        public static void PlayBeep() { if (source != null) source.PlayOneShot(beep); }
        public static void PlayClick() { if (source != null) source.PlayOneShot(click, 1f); }

        static AudioClip Tone(string name, float freq, float seconds, bool percussive)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, env = percussive ? Mathf.Exp(-t * 90f) : Mathf.Min(1f, (seconds - t) * 40f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.5f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
