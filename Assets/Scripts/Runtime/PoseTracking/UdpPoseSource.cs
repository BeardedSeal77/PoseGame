using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpPoseSource : PoseSourceBase
{
    [Header("UDP")]
    [SerializeField] private int listenPort = 5053;
    [SerializeField] private bool autoStartOnEnable = true;

    [Header("Pose Mapping")]
    [SerializeField] private float horizontalSpan = 2.0f;
    [SerializeField] private float verticalSpan = 3.0f;
    [SerializeField] private float depthScale = 1.0f;
    [SerializeField] private bool mirrorInputX;

    [Header("Connection")]
    [SerializeField] private float staleFrameTimeout = 0.5f;

    private readonly PoseFrame latestPose = new PoseFrame();
    private UdpClient udpClient;
    private bool started;
    private float lastPacketTime;
    private IPEndPoint remoteEndPoint;

    public override bool TryGetPose(PoseFrame outputFrame)
    {
        if (outputFrame == null)
        {
            return false;
        }

        PollPackets();

        if (Time.realtimeSinceStartup - lastPacketTime > staleFrameTimeout)
        {
            return false;
        }

        outputFrame.CopyFrom(latestPose);
        return true;
    }

    private void OnEnable()
    {
        if (autoStartOnEnable)
        {
            StartListening();
        }
    }

    private void Update()
    {
        PollPackets();
    }

    private void OnDisable()
    {
        StopListening();
    }

    public void StartListening()
    {
        if (started)
        {
            return;
        }

        try
        {
            udpClient = new UdpClient(listenPort);
            udpClient.Client.Blocking = false;
            remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            started = true;
            Debug.Log($"[UdpPoseSource] Listening on UDP {listenPort}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[UdpPoseSource] Failed to open UDP {listenPort}: {exception.Message}");
            StopListening();
        }
    }

    public void StopListening()
    {
        started = false;

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

    private void PollPackets()
    {
        if (!started || udpClient == null)
        {
            return;
        }

        while (udpClient.Available > 0)
        {
            byte[] buffer = udpClient.Receive(ref remoteEndPoint);
            string json = Encoding.UTF8.GetString(buffer);
            ApplyMessage(json);
        }
    }

    private void ApplyMessage(string json)
    {
        PosePacket packet = JsonUtility.FromJson<PosePacket>(json);
        if (packet == null || packet.joints == null || packet.joints.Length == 0)
        {
            return;
        }

        latestPose.Clear();
        latestPose.timestamp = packet.timestamp;

        for (int index = 0; index < packet.joints.Length; index++)
        {
            PosePacketJoint joint = packet.joints[index];
            if (!PoseJointIdUtility.IsValidIndex(joint.id))
            {
                continue;
            }

            float mappedX = (joint.x - 0.5f) * horizontalSpan;
            if (mirrorInputX)
            {
                mappedX *= -1f;
            }

            float mappedY = (0.5f - joint.y) * verticalSpan;
            float mappedZ = joint.z * depthScale;

            latestPose.SetJoint((PoseJointId)joint.id, new Vector3(mappedX, mappedY, mappedZ), joint.confidence);
        }

        lastPacketTime = Time.realtimeSinceStartup;
    }

    [Serializable]
    private class PosePacket
    {
        public float timestamp;
        public int frameWidth;
        public int frameHeight;
        public PosePacketJoint[] joints;
    }

    [Serializable]
    private class PosePacketJoint
    {
        public int id;
        public float x;
        public float y;
        public float z;
        public float confidence;
    }
}