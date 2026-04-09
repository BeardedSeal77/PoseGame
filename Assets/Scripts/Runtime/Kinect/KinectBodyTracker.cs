using System;
using UnityEngine;
using Windows.Kinect;

[DisallowMultipleComponent]
public class KinectBodyTracker : MonoBehaviour
{
    [SerializeField] private bool autoStartOnEnable = true;
    [SerializeField] private bool readColorFrames = true;
    [SerializeField] private GameObject kinectWarningUi;

    private KinectSensor sensor;
    private BodyFrameReader bodyReader;
    private ColorFrameReader colorReader;
    private Body[] bodies;
    private Texture2D colorTexture;
    private byte[] colorData;

    public bool IsInitialized { get; private set; }
    public bool IsAvailable => IsInitialized && sensor != null && sensor.IsAvailable;
    public CoordinateMapper CoordinateMapper => sensor != null ? sensor.CoordinateMapper : null;
    public Body[] Bodies => bodies;
    public Texture2D ColorTexture => colorTexture;
    public int ColorWidth { get; private set; }
    public int ColorHeight { get; private set; }

    private void OnEnable()
    {
        if (autoStartOnEnable)
        {
            StartTracking();
        }
    }

    private void Update()
    {
        if (!IsInitialized)
        {
            return;
        }

        UpdateBodies();
        UpdateColorFrame();
    }

    private void OnDisable()
    {
        StopTracking();
    }

    private void OnDestroy()
    {
        StopTracking();
    }

    public void StartTracking()
    {
        if (IsInitialized)
        {
            return;
        }

        if (kinectWarningUi != null)
        {
            kinectWarningUi.SetActive(false);
        }

        try
        {
            sensor = KinectSensor.GetDefault();
            if (sensor == null)
            {
                Debug.LogWarning("[KinectBodyTracker] No Kinect v2 sensor detected.");
                ShowWarningUi();
                return;
            }

            bodyReader = sensor.BodyFrameSource.OpenReader();
            bodies = new Body[sensor.BodyFrameSource.BodyCount];

            if (readColorFrames)
            {
                colorReader = sensor.ColorFrameSource.OpenReader();
                FrameDescription frameDescription = sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
                ColorWidth = frameDescription.Width;
                ColorHeight = frameDescription.Height;
                colorData = new byte[frameDescription.BytesPerPixel * frameDescription.LengthInPixels];
                colorTexture = new Texture2D(ColorWidth, ColorHeight, TextureFormat.RGBA32, false);
            }

            sensor.IsAvailableChanged += OnSensorAvailabilityChanged;
            if (!sensor.IsOpen)
            {
                sensor.Open();
            }

            IsInitialized = true;

            if (!sensor.IsAvailable)
            {
                Debug.LogWarning("[KinectBodyTracker] Kinect runtime loaded, but the physical sensor is unavailable.");
            }
        }
        catch (DllNotFoundException)
        {
            Debug.LogWarning("[KinectBodyTracker] Kinect runtime is not installed. Kinect tracking is disabled.");
            ShowWarningUi();
            CleanupReadersAndSensor();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[KinectBodyTracker] Failed to initialize Kinect: {exception.Message}");
            ShowWarningUi();
            CleanupReadersAndSensor();
        }
    }

    public void StopTracking()
    {
        if (!IsInitialized && sensor == null && bodyReader == null && colorReader == null)
        {
            return;
        }

        CleanupReadersAndSensor();
        IsInitialized = false;
    }

    public bool TryGetPrimaryBody(out Body body)
    {
        body = null;
        if (!IsInitialized || bodies == null)
        {
            return false;
        }

        for (int i = 0; i < bodies.Length; i++)
        {
            Body candidate = bodies[i];
            if (candidate != null && candidate.IsTracked)
            {
                body = candidate;
                return true;
            }
        }

        return false;
    }

    private void UpdateBodies()
    {
        if (bodyReader == null)
        {
            return;
        }

        using (BodyFrame frame = bodyReader.AcquireLatestFrame())
        {
            if (frame == null)
            {
                return;
            }

            if (bodies == null || bodies.Length != sensor.BodyFrameSource.BodyCount)
            {
                bodies = new Body[sensor.BodyFrameSource.BodyCount];
            }

            frame.GetAndRefreshBodyData(bodies);
        }
    }

    private void UpdateColorFrame()
    {
        if (!readColorFrames || colorReader == null || colorTexture == null || colorData == null)
        {
            return;
        }

        using (ColorFrame frame = colorReader.AcquireLatestFrame())
        {
            if (frame == null)
            {
                return;
            }

            frame.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Rgba);
            colorTexture.LoadRawTextureData(colorData);
            colorTexture.Apply(false);
        }
    }

    private void OnSensorAvailabilityChanged(object sender, IsAvailableChangedEventArgs eventArgs)
    {
        if (eventArgs.IsAvailable)
        {
            if (kinectWarningUi != null)
            {
                kinectWarningUi.SetActive(false);
            }

            Debug.Log("[KinectBodyTracker] Kinect sensor connected.");
            return;
        }

        Debug.LogWarning("[KinectBodyTracker] Kinect sensor disconnected.");
    }

    private void ShowWarningUi()
    {
        if (kinectWarningUi != null)
        {
            kinectWarningUi.SetActive(true);
        }
    }

    private void CleanupReadersAndSensor()
    {
        if (bodyReader != null)
        {
            bodyReader.Dispose();
            bodyReader = null;
        }

        if (colorReader != null)
        {
            colorReader.Dispose();
            colorReader = null;
        }

        if (sensor != null)
        {
            sensor.IsAvailableChanged -= OnSensorAvailabilityChanged;

            if (sensor.IsOpen)
            {
                sensor.Close();
            }

            sensor = null;
        }

        bodies = null;
    }
}