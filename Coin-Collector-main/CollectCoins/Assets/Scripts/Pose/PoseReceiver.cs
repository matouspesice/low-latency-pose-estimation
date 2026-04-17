using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class PoseReceiver : MonoBehaviour
{
    public int port = 5555;
    public PoseMessage latestPose;
    [Range(0f, 1f)] public float minConfidence = 0.3f;

    Socket _socket;
    byte[] _buffer = new byte[4096];
    bool _receivedAny;

    void Start()
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            _socket.Blocking = false;
            Debug.Log("[PoseReceiver] Listening on port " + port);
        }
        catch (Exception e) { Debug.LogError("[PoseReceiver] " + e.Message); }
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
                if (pose != null && pose.keypoints != null && pose.keypoints.Length >= 17)
                {
                    latestPose = pose;
                    _receivedAny = true;
                }
            }
            catch (SocketException) { break; }
            catch (Exception) { }
        }
    }

    void OnDestroy()
    {
        try { if (_socket != null) _socket.Close(); } catch (Exception) { }
        _socket = null;
    }

    public bool HasReceivedPose { get { return _receivedAny; } }
}
