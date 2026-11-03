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
        /// <summary>
        /// 到扭力的「喀」聲（驗收 F3、K3）：clip 由資料指定（ToolDefinition.clickClip），沒有指定就用內建的短音效，不可以沒聲音。
        /// ClickPlayer 是實際播放的地方（預設 AudioSource.PlayOneShot），測試換成替身記錄呼叫次數。
        /// </summary>
        public static void PlayClick(AudioClip clip = null) => ClickPlayer(clip != null ? clip : BuiltinClick);
        public static System.Action<AudioClip> ClickPlayer = c => { if (source != null) source.PlayOneShot(c, 1f); };
        public static AudioClip BuiltinClick => click != null ? click : (click = Tone("click", 900f, 0.05f, true));

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
