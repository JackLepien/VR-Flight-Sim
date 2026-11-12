using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>Receives browser controls from the local Python server on this computer.</summary>
[DisallowMultipleComponent]
public class HelicopterControlReceiver : MonoBehaviour
{
    [SerializeField, Range(1024, 65535)] int m_Port = 15050;
    [SerializeField, Min(0.1f)] float m_TimeoutSeconds = 0.5f;

    [Serializable]
    public struct Controls
    {
        public float cyclicX;
        public float cyclicY;
        public float collective;
        public float yaw;
    }

    // Missing fields stay NaN, so incomplete packets cannot become valid controls.
    [Serializable]
    class Packet
    {
        public float cyclicX = float.NaN;
        public float cyclicY = float.NaN;
        public float collective = float.NaN;
        public float yaw = float.NaN;
    }

    UdpClient m_Client;
    Controls m_Controls;
    float m_LastReceived;
    bool m_HasPacket;

    public bool IsConnected => isActiveAndEnabled && m_Client != null && m_HasPacket
        && Time.realtimeSinceStartup - m_LastReceived <= m_TimeoutSeconds;

    void OnEnable()
    {
        CloseSocket();
        try
        {
            // Assign before binding so a failed bind can also dispose the socket.
            m_Client = new UdpClient(AddressFamily.InterNetwork);
            m_Client.Client.Bind(new IPEndPoint(IPAddress.Loopback, m_Port));
            m_Client.Client.Blocking = false;
        }
        catch (SocketException exception)
        {
            CloseSocket();
            Debug.LogError($"Cannot listen for helicopter controls on 127.0.0.1:{m_Port} " +
                $"({exception.SocketErrorCode}): {exception.Message}. " +
                "Check that the UDP port is free and matches UNITY_UDP_PORT on the control server.", this);
        }
    }

    void OnDisable() => CloseSocket();

    void CloseSocket()
    {
        m_Client?.Close();
        m_Client = null;
        m_HasPacket = false;
        m_Controls = default;
    }

    /// <summary>Called on Unity's main thread. Returns neutral controls after a stream timeout.</summary>
    public bool TryRead(out Controls controls)
    {
        if (isActiveAndEnabled && m_Client != null)
        {
            try
            {
                // Bound work per frame and use the newest valid packet in the queue.
                for (int i = 0; i < 128 && m_Client.Available > 0; i++)
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = m_Client.Receive(ref sender);
                    if (bytes.Length > 16384)
                        continue;

                    var packet = new Packet();
                    try
                    {
                        JsonUtility.FromJsonOverwrite(Encoding.UTF8.GetString(bytes), packet);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (!Finite(packet.cyclicX) || !Finite(packet.cyclicY)
                        || !Finite(packet.collective) || !Finite(packet.yaw))
                        continue;

                    m_Controls = new Controls
                    {
                        cyclicX = Mathf.Clamp(packet.cyclicX, -1f, 1f),
                        cyclicY = Mathf.Clamp(packet.cyclicY, -1f, 1f),
                        collective = Mathf.Clamp(packet.collective, -1f, 1f),
                        yaw = Mathf.Clamp(packet.yaw, -1f, 1f)
                    };
                    m_LastReceived = Time.realtimeSinceStartup;
                    m_HasPacket = true;
                }
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.WouldBlock)
                {
                    CloseSocket();
                    Debug.LogError($"Helicopter control receiver stopped: {exception.Message}", this);
                }
            }
        }

        controls = IsConnected ? m_Controls : default;
        return IsConnected;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
