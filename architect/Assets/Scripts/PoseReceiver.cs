using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Listens for pose JSON messages from pose.py (UDP) and exposes
/// the latest pose for PoseAvatarDriver. Set port to match --udp-port (e.g. 5555).
/// </summary>
public class PoseReceiver : MonoBehaviour
{
    [Tooltip("UDP port to listen on (must match pose.py --udp-port)")]
    public int port = 5555;

    [Tooltip("Latest received pose; null if none yet or invalid.")]
    public PoseMessage latestPose;

    [Tooltip("Latest OCR text from UDP payload (can be updated even without a valid pose packet).")]
    public string latestOcrText = "";
    [Tooltip("Latest clock ROI image payload from UDP (base64-encoded image).")]
    public string latestClockRoiBase64 = "";

    [Tooltip("Minimum confidence (0-1) to consider a keypoint valid.")]
    [Range(0f, 1f)]
    public float minConfidence = 0.3f;

    Socket _socket;
    // 128 KB: plain pose JSON is ~1 KB, but when pose.py streams the Clock ROI
    // (base64-encoded JPEG crop bundled in the pose datagram via --clock-stream-enable),
    // packets routinely reach 5-20 KB. A 4 KB buffer triggered WSAEMSGSIZE on
    // Windows and every oversized datagram was dropped — pose and ROI alike.
    byte[] _buffer = new byte[128 * 1024];
    bool _receivedAny;
    bool _loggedMsgSizeHint;
    bool _loggedFirstReceive;

    void Start()
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket.Blocking = false;
            _socket.ReceiveBufferSize = 1 << 20; // 1 MB OS-level RX buffer, plenty for 30+ FPS ROI stream.
            Debug.Log($"[PoseReceiver] Listening on port {port} (rx buffer {_buffer.Length} B). Start pose.py with --udp-port {port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseReceiver] Failed to bind port {port}: {e.Message}");
        }
    }

    void Update()
    {
        if (_socket == null) return;

        int maxRead = 10;
        while (_socket.Available > 0 && maxRead-- > 0)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int count = _socket.ReceiveFrom(_buffer, ref remote);
                if (count <= 0) continue;

                string json = Encoding.UTF8.GetString(_buffer, 0, count);
                var pose = JsonUtility.FromJson<PoseMessage>(json);
                if (pose != null)
                {
                    if (!_loggedFirstReceive)
                    {
                        _loggedFirstReceive = true;
                        Debug.Log($"[PoseReceiver] First UDP packet received ({count} B, keypoints={(pose.keypoints == null ? 0 : pose.keypoints.Length)}, roiBase64Len={(pose.roiImageBase64 == null ? 0 : pose.roiImageBase64.Length)}).");
                    }
                    if (!string.IsNullOrWhiteSpace(pose.ocrText))
                        latestOcrText = pose.ocrText;
                    if (!string.IsNullOrWhiteSpace(pose.roiImageBase64))
                        latestClockRoiBase64 = pose.roiImageBase64;

                    if (pose.keypoints != null && pose.keypoints.Length >= 17)
                    {
                        latestPose = pose;
                        _receivedAny = true;
                    }
                }
            }
            catch (SocketException ex)
            {
                // WSAEMSGSIZE = 10040: incoming datagram was larger than _buffer and was truncated.
                if (!_loggedMsgSizeHint && ex.SocketErrorCode == SocketError.MessageSize)
                {
                    _loggedMsgSizeHint = true;
                    Debug.LogWarning($"[PoseReceiver] UDP datagram larger than rx buffer ({_buffer.Length} B) — " +
                                     "increase `_buffer` size or lower --clock-stream-jpeg-quality / ROI resolution in pose.py.");
                }
                break;
            }
            catch (Exception)
            {
                // Ignore parse errors
            }
        }
    }

    void OnDestroy()
    {
        try { _socket?.Close(); } catch (Exception) { }
        _socket = null;
    }

    /// <summary>True if at least one pose has been received.</summary>
    public bool HasReceivedPose => _receivedAny;

    /// <summary>Get keypoint by COCO index; returns false if missing or low confidence.</summary>
    public bool TryGetKeypoint(int index, out Vector2 normalized, out float score)
    {
        normalized = Vector2.zero;
        score = 0f;
        if (latestPose == null || latestPose.keypoints == null || index < 0 || index >= latestPose.keypoints.Length)
            return false;
        var k = latestPose.keypoints[index];
        score = k.s;
        if (score < minConfidence) return false;
        normalized.x = k.x;
        normalized.y = k.y;
        return true;
    }

    /// <summary>Latest OCR text, if available from UDP payload.</summary>
    public string LatestOcrText => latestOcrText;
    /// <summary>Latest clock ROI image payload (base64-encoded), if available.</summary>
    public string LatestClockRoiBase64 => latestClockRoiBase64;
}
