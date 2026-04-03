using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class UdpPoseSource : PoseSourceBase
{
    [Header("UDP")]
    [SerializeField] private int listenPort = 5053;
    [SerializeField] private bool autoStartOnEnable = true;

    [Header("Python Auto Launch")]
    [SerializeField] private bool autoLaunchPythonStreamer;
    [SerializeField] private string pythonLauncherBatchRelativePath = "scripts/run_unity_camera.bat";
    [SerializeField] private string pythonExecutableRelativePath = "pose_detection/venv/Scripts/python.exe";
    [SerializeField] private string pythonScriptRelativePath = "pose_detection/stream_to_unity.py";
    [SerializeField] private string pythonArguments = "--camera 0";
    [SerializeField] private string pythonHost = "127.0.0.1";

    [Header("Pose Mapping")]
    [SerializeField] private float horizontalSpan = 2.0f;
    [SerializeField] private float verticalSpan = 3.0f;
    [SerializeField] private float depthScale = 1.0f;
    [SerializeField] private bool mirrorInputX;

    [Header("Connection")]
    [SerializeField] private float staleFrameTimeout = 0.5f;

    private readonly PoseFrame latestPose = new PoseFrame();
    private UdpClient udpClient;
    private Process pythonProcess;
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

    private void OnApplicationQuit()
    {
        StopListening();
    }

    public void StartListening()
    {
        if (started)
        {
            return;
        }

        if (autoLaunchPythonStreamer)
        {
            StartPythonStreamer();
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

        StopPythonStreamer();
    }

    private void StartPythonStreamer()
    {
        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            return;
        }

        string projectRoot = GetProjectRootPath();
        if (TryStartPythonDirect(projectRoot))
        {
            return;
        }

        if (TryStartPythonBatch(projectRoot))
        {
            return;
        }

        Debug.LogError("[UdpPoseSource] Failed to start Python streamer with both direct launch and batch launcher.");
    }

    private bool TryStartPythonDirect(string projectRoot)
    {
        string pythonScriptPath = Path.Combine(projectRoot, pythonScriptRelativePath);
        string workingDirectory = Path.GetDirectoryName(pythonScriptPath);

        if (!File.Exists(pythonScriptPath))
        {
            Debug.LogError($"[UdpPoseSource] Python script not found: {pythonScriptPath}");
            return false;
        }

        if (!TryResolvePythonCommand(projectRoot, out string pythonCommand, out string commandPrefix))
        {
            return false;
        }

        string arguments = BuildPythonArguments(pythonScriptPath, commandPrefix);

        try
        {
            pythonProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = arguments,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? projectRoot : workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
                EnableRaisingEvents = true,
            };

            pythonProcess.Exited += OnPythonProcessExited;
            pythonProcess.Start();
            Debug.Log($"[UdpPoseSource] Started Python streamer: {pythonCommand} {arguments}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UdpPoseSource] Direct Python launch failed, falling back to batch: {exception.Message}");
            DisposePythonProcess();
            return false;
        }
    }

    private bool TryStartPythonBatch(string projectRoot)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor || string.IsNullOrWhiteSpace(pythonLauncherBatchRelativePath))
        {
            return false;
        }

        string batchPath = Path.Combine(projectRoot, pythonLauncherBatchRelativePath);
        if (!File.Exists(batchPath))
        {
            return false;
        }

        string arguments = BuildBatchArguments();

        try
        {
            pythonProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(batchPath),
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                },
                EnableRaisingEvents = true,
            };

            pythonProcess.Exited += OnPythonProcessExited;
            pythonProcess.Start();
            Debug.Log($"[UdpPoseSource] Started Python launcher batch: {batchPath} {arguments}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UdpPoseSource] Failed to start batch launcher, falling back to direct Python: {exception.Message}");
            DisposePythonProcess();
            return false;
        }
    }

    private void StopPythonStreamer()
    {
        if (pythonProcess == null)
        {
            return;
        }

        try
        {
            if (!pythonProcess.HasExited)
            {
                pythonProcess.Kill();
                pythonProcess.WaitForExit(1000);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UdpPoseSource] Failed to stop Python streamer cleanly: {exception.Message}");
        }
        finally
        {
            DisposePythonProcess();
        }
    }

    private void OnPythonProcessExited(object sender, EventArgs eventArgs)
    {
        DisposePythonProcess();
    }

    private void DisposePythonProcess()
    {
        if (pythonProcess == null)
        {
            return;
        }

        pythonProcess.Exited -= OnPythonProcessExited;
        pythonProcess.Dispose();
        pythonProcess = null;
    }

    private bool TryResolvePythonCommand(string projectRoot, out string pythonCommand, out string commandPrefix)
    {
        pythonCommand = null;
        commandPrefix = string.Empty;

        if (!string.IsNullOrWhiteSpace(pythonExecutableRelativePath))
        {
            string pythonExecutablePath = Path.Combine(projectRoot, pythonExecutableRelativePath);
            if (File.Exists(pythonExecutablePath))
            {
                pythonCommand = pythonExecutablePath;
                return true;
            }
        }

        if (TryStartProbe("python", "--version"))
        {
            pythonCommand = "python";
            return true;
        }

        if (TryStartProbe("py", "-3 --version"))
        {
            pythonCommand = "py";
            commandPrefix = "-3";
            return true;
        }

        return false;
    }

    private bool TryStartProbe(string fileName, string arguments)
    {
        try
        {
            using Process probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            probe.Start();
            probe.WaitForExit(2000);
            return probe.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string BuildPythonArguments(string pythonScriptPath, string commandPrefix)
    {
        string portArgument = $"--host {pythonHost} --port {listenPort}";
        string extraArguments = string.IsNullOrWhiteSpace(pythonArguments) ? string.Empty : $" {pythonArguments.Trim()}";
        string launcherPrefix = string.IsNullOrWhiteSpace(commandPrefix) ? string.Empty : $"{commandPrefix.Trim()} ";
        return $"{launcherPrefix}\"{pythonScriptPath}\" {portArgument}{extraArguments}".Trim();
    }

    private string BuildBatchArguments()
    {
        string portArgument = $"--host {pythonHost} --port {listenPort}";
        string extraArguments = string.IsNullOrWhiteSpace(pythonArguments) ? string.Empty : $" {pythonArguments.Trim()}";
        return $"{portArgument}{extraArguments}".Trim();
    }

    private string GetProjectRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
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