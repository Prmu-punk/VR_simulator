using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace WebcamXR
{
    public class WebcamTrackingReceiver : MonoBehaviour
    {
        [SerializeField]
        int m_Port = 7777;

        [SerializeField]
        float m_MaxPacketAgeSeconds = 0.5f;

        [SerializeField]
        bool m_LogPackets;

        UdpClient m_Client;
        IPEndPoint m_RemoteEndPoint;
        TrackingFrameV1 m_LatestFrame = new TrackingFrameV1();
        float m_LastPacketRealtime = -1f;
        string m_LastError;

        public TrackingFrameV1 LatestFrame => m_LatestFrame;
        public bool HasFreshFrame => m_LastPacketRealtime >= 0f && SecondsSinceLastPacket <= m_MaxPacketAgeSeconds;
        public float SecondsSinceLastPacket => m_LastPacketRealtime < 0f ? float.PositiveInfinity : Time.realtimeSinceStartup - m_LastPacketRealtime;
        public string LastError => m_LastError;

        void OnEnable()
        {
            TryOpenSocket();
        }

        void OnDisable()
        {
            CloseSocket();
        }

        void Update()
        {
            if (m_Client == null)
                return;

            while (m_Client.Available > 0)
            {
                try
                {
                    var bytes = m_Client.Receive(ref m_RemoteEndPoint);
                    var json = Encoding.UTF8.GetString(bytes);
                    var frame = JsonUtility.FromJson<TrackingFrameV1>(json);
                    if (frame == null || frame.version != 1)
                        continue;

                    m_LatestFrame = frame;
                    m_LastPacketRealtime = Time.realtimeSinceStartup;
                    m_LastError = null;

                    if (m_LogPackets)
                        Debug.Log(json, this);
                }
                catch (SocketException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    m_LastError = exception.Message;
                    Debug.LogWarning($"Failed to parse webcam tracking packet: {exception.Message}", this);
                    break;
                }
            }
        }

        public void ResetConnection()
        {
            CloseSocket();
            TryOpenSocket();
        }

        void TryOpenSocket()
        {
            try
            {
                m_RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                m_Client = new UdpClient(m_Port);
                m_Client.Client.ReceiveTimeout = 0;
                m_Client.Client.Blocking = false;
                m_LastError = null;
            }
            catch (Exception exception)
            {
                m_LastError = exception.Message;
                Debug.LogError($"Failed to open UDP receiver on port {m_Port}: {exception.Message}", this);
            }
        }

        void CloseSocket()
        {
            if (m_Client == null)
                return;

            m_Client.Close();
            m_Client = null;
        }
    }
}
