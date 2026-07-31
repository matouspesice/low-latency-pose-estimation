using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Append-only text log for pipeline latency (pairs with pose.py --pipeline-trace).
/// Use the same file path on both sides to correlate Python and Unity events by seq + timestamp.
/// </summary>
public static class PipelineTrace
{
    static readonly object Lock = new object();
    static string _path;
    static StreamWriter _writer;

    public static bool Enabled => _writer != null;

    public static void Init(string path)
    {
        Shutdown();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _path = path.Trim();
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = true };
            Log("session_start", -1, "source=unity");
            Debug.Log($"[PipelineTrace] Logging to {_path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PipelineTrace] Could not open log file '{_path}': {e.Message}");
            _writer = null;
            _path = null;
        }
    }

    public static void Log(string stage, int seq = -1, string extra = null)
    {
        if (_writer == null)
            return;

        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var sb = new StringBuilder(128);
        sb.Append(ts).Append(" | unity | stage=").Append(stage);
        if (seq >= 0)
            sb.Append(" | seq=").Append(seq);
        if (!string.IsNullOrEmpty(extra))
            sb.Append(" | ").Append(extra);
        sb.AppendLine();

        lock (Lock)
        {
            try
            {
                _writer.Write(sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PipelineTrace] Write failed: {e.Message}");
            }
        }
    }

    public static void Shutdown()
    {
        lock (Lock)
        {
            if (_writer == null)
                return;
            try
            {
                Log("session_end", -1, "source=unity");
                _writer.Dispose();
            }
            catch (Exception)
            {
                // ignore on shutdown
            }
            _writer = null;
            _path = null;
        }
    }
}
