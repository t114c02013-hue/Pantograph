using System.IO;
using UnityEditor;

namespace PantographSim.Editor
{
    /// <summary>
    /// 每 2 秒把 Editor 狀態（是否在 Play、是否編譯中）寫到 Logs/editor-state.txt，
    /// 讓外部工具判斷現在能不能重新整理資產、跑測試或安裝套件（Play 中一律不動）。
    /// </summary>
    [InitializeOnLoad]
    public static class EditorStateProbe
    {
        const string Path = "Logs/editor-state.txt";
        static double next;

        static EditorStateProbe() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2.0;
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Path, $"time={System.DateTime.Now:O}\nplaying={EditorApplication.isPlaying}\nwillPlay={EditorApplication.isPlayingOrWillChangePlaymode}\n" +
                                        $"compiling={EditorApplication.isCompiling}\nupdating={EditorApplication.isUpdating}\nscene={UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path}\n");
            }
            catch (IOException) { }
        }
    }
}
