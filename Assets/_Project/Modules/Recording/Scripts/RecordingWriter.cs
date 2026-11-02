using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace PantographSim.Recording
{
    /// <summary>
    /// 紀錄資料夾的寫檔器：主執行緒只把工作放進佇列，背景執行緒負責寫檔（避免 VR 掉幀）。
    /// session.jsonl 每行寫完就 flush；poses.bin 由取樣器每 1 s 交一塊、寫完 flush；snapshots.bin 每筆寫完 flush。
    /// </summary>
    public class RecordingWriter : IDisposable
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly ManualResetEvent idle = new ManualResetEvent(true);
        readonly Thread thread;
        volatile bool stopping;
        int pending;

        StreamWriter session;
        FileStream posesStream, snapStream;
        BinaryWriter snapWriter;

        public string Folder { get; }
        /// <summary>最後一次寫檔錯誤（給介面顯示；沒有錯誤是空字串）。</summary>
        public string LastError { get; private set; } = "";

        public RecordingWriter(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
            session = new StreamWriter(new FileStream(Path.Combine(folder, RecordFiles.Session), FileMode.Append, FileAccess.Write, FileShare.Read), Utf8);
            posesStream = new FileStream(Path.Combine(folder, RecordFiles.Poses), FileMode.Create, FileAccess.Write, FileShare.Read);
            using (var w = new BinaryWriter(posesStream, Encoding.UTF8, true)) PoseFile.WriteHeader(w);
            posesStream.Flush();
            snapStream = new FileStream(Path.Combine(folder, RecordFiles.Snapshots), FileMode.Create, FileAccess.Write, FileShare.Read);
            snapWriter = new BinaryWriter(snapStream, Encoding.UTF8, true);
            SnapshotFile.WriteHeader(snapWriter);
            snapWriter.Flush();
            thread = new Thread(Loop) { IsBackground = true, Name = "PantographSim.RecordingWriter" };
            thread.Start();
        }

        void Enqueue(Action a)
        {
            if (stopping) return;
            Interlocked.Increment(ref pending);
            idle.Reset();
            queue.Enqueue(a);
            signal.Set();
        }

        void Loop()
        {
            while (true)
            {
                while (queue.TryDequeue(out var a))
                {
                    try { a(); }
                    catch (Exception e) { LastError = e.Message; }
                    if (Interlocked.Decrement(ref pending) == 0) idle.Set();
                }
                if (stopping && queue.IsEmpty) break;
                signal.WaitOne(200);
            }
        }

        /// <summary>寫一行 session.jsonl（自動加換行並 flush）。</summary>
        public void WriteLine(string json) => Enqueue(() => { session.Write(json); session.Write('\n'); session.Flush(); });

        /// <summary>寫一塊位姿資料（取樣器每 1 s 交一次）。</summary>
        public void WritePoseChunk(byte[] chunk)
        {
            if (chunk == null || chunk.Length == 0) return;
            Enqueue(() => { posesStream.Write(chunk, 0, chunk.Length); posesStream.Flush(); });
        }

        public void WriteSnapshot(Snapshot s) => Enqueue(() => { SnapshotFile.WriteRecord(snapWriter, s); snapWriter.Flush(); });

        /// <summary>整檔覆寫（meta.json、summary.json）：先寫暫存檔再換名，避免寫到一半斷電留下壞檔。</summary>
        public void WriteFile(string name, string content) => Enqueue(() => WriteAtomic(Path.Combine(Folder, name), content));

        public static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>等佇列清空（測試、結算產生報表前用）。</summary>
        public bool Drain(int timeoutMs = 5000) => idle.WaitOne(timeoutMs);

        public void Dispose()
        {
            if (stopping) return;
            Drain(3000);
            stopping = true;
            signal.Set();
            thread.Join(3000);
            try { session?.Flush(); session?.Dispose(); } catch (Exception) { }
            try { posesStream?.Flush(); posesStream?.Dispose(); } catch (Exception) { }
            try { snapWriter?.Flush(); snapWriter?.Dispose(); snapStream?.Dispose(); } catch (Exception) { }
            session = null; posesStream = null; snapStream = null; snapWriter = null;
            signal.Dispose();
            idle.Dispose();
        }
    }
}
