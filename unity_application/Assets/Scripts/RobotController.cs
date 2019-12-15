using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;

public class RobotController : MonoBehaviour
{
    public RawImage rawImage;
    public Text statusText;

    private string robotIP = "192.168.1.1";
    private int controlPort = 2001;
    private Socket clientSocket;
    private enum MoveDirection { Forward, Backwards, Right, Left, LeftTrackForward, LeftTrackBackWards, RightTrackForward, RightTrackBackWards, HeadVertical, HeadHorizontal, Stop };
    private bool stopped;
    private bool connecting;
    private float currentCameraX = 90f;
    private float currentCameraY = 0f;
    private Camera mainCam;
    private Vector3 lastCamPos = Vector3.zero;

    private string cameraStreamURL = "http://192.168.1.1:8080/?action=stream";
    private Texture2D texture;

    private GameObject statusContainer;
    private Texture cameraPlaceholder;
    private bool rawImageFlipped = false;
    private AspectRatioFitter ratioFitter;
    // JPEG delimiters
    private const byte picMarker = 0xFF;
    private const byte picStart = 0xD8;
    private const byte picEnd = 0xD9;
    private bool _threadRunning;
    private Thread _thread;
    private byte[] jpg_buf;

    void Awake()
    {
        UnityThread.initUnityThread();
    }

    void Start()
    {
        Application.targetFrameRate = 300;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        stopped = false;
        connecting = true;
        statusContainer = statusText.transform.parent.gameObject;
        cameraPlaceholder = rawImage.texture;
        ratioFitter = rawImage.GetComponent<AspectRatioFitter>();
        mainCam = Camera.main;
        statusText.text = "Initializing...";
        ServicePointManager.DefaultConnectionLimit = 20;
        Invoke("ConnectToRobot", 1f);
    }

    public void ConnectToRobot()
    {
        CancelInvoke("ConnectToRobot");
        connecting = true;
        statusContainer.SetActive(true);
        statusText.text = "Connecting to robot...";
        try
        {
            IPEndPoint serverAddress = new IPEndPoint(IPAddress.Parse(robotIP), controlPort);
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(serverAddress);

            GetVideo();
            
            //reset servos
            clientSocket.Send(new byte[] { 0xff, 0x33, 0x00, 0x00, 0xff });
            lastCamPos = Vector3.zero;

            connecting = false;
            Debug.Log("Connection open, host active");
            statusContainer.SetActive(false);
        }
        catch (SocketException ex)
        {
            Debug.Log("Connection could not be established due to: \n" + ex.Message);
            statusText.text = ex.Message;
            Invoke("ConnectToRobot", 5f);
        }
    }

    private void Update()
    {
        if (clientSocket != null && clientSocket.Connected)
        { 
            if (Input.GetJoystickNames().Length == 0)
            {
                //joystick disconnected
                RobotMove(MoveDirection.Stop, 0);
                return;
            }

            if (Input.GetAxis("Fire2") > 0.1f)
            {
                RobotMove(MoveDirection.Stop, 0, true);
                return;
            }

            float verticalMovement = Mathf.Clamp(Input.GetAxis("Vertical") + Input.GetAxis("Vertical DPad"),-1f, 1f);
            float horizontalMovement = Mathf.Clamp(Input.GetAxis("Horizontal") + Input.GetAxis("Horizontal DPad"), -1f, 1f);
            float leftThumb = Input.GetAxis("Vertical Left Thumb");
            float rightThumb = Input.GetAxis("Vertical Right Thumb");

            if (Mathf.Abs(verticalMovement) + Mathf.Abs(horizontalMovement) + Mathf.Abs(leftThumb) + Mathf.Abs(rightThumb) < 0.1f)
            {
                RobotMove(MoveDirection.Stop, 0);
                return;
            }
            
            //dpad movement
            /*
            if (verticalMovement > 0.1f)
            {
                RobotMove(MoveDirection.Forward, 1);
            }
            else if (verticalMovement < -0.1f)
            {
                RobotMove(MoveDirection.Backwards, 1);
            }
            if (horizontalMovement > 0.1f)
            {
                RobotMove(MoveDirection.Right, 1);
            }
            else if (horizontalMovement < -0.1f)
            {
                RobotMove(MoveDirection.Left, 1);
            }*/

            //thumbs movement
            if (leftThumb > 0.1f && rightThumb < -0.1f)
            {
                RobotMove(MoveDirection.Left, Mathf.Max(Mathf.Abs(leftThumb), Mathf.Abs(rightThumb)));
                return;
            }
            if (leftThumb < -0.1f && rightThumb > 0.1f)
            {
                RobotMove(MoveDirection.Right, Mathf.Max(Mathf.Abs(leftThumb), Mathf.Abs(rightThumb)));
                return;
            }
            if (leftThumb > 0.1f)
            {
                RobotMove(MoveDirection.LeftTrackForward, leftThumb);
            }
            else if (leftThumb < -0.1f)
            {
                RobotMove(MoveDirection.LeftTrackBackWards, -leftThumb);
            }
            if (rightThumb > 0.1f)
            {
                RobotMove(MoveDirection.RightTrackForward, rightThumb);
            }
            else if (rightThumb < -0.1f)
            {
                RobotMove(MoveDirection.RightTrackBackWards, -rightThumb);
            }

            ProcessHeadMovement(verticalMovement, horizontalMovement);

            //camera movement
            if (XRSettings.enabled)
            {
                Quaternion crtRot = mainCam.transform.rotation;
                Vector3 lookDir = crtRot * Vector3.forward;
                if (Vector3.Distance(lastCamPos, lookDir) > 0.05f)
                {
                    if (lookDir.z > 0)
                    {
                        currentCameraX = Remap(lookDir.x, -1f, 1f, 0, 180);
                        if (lookDir.x <= -1f)
                            currentCameraX = 0;
                        else if (lookDir.x >= 1f)
                            currentCameraX = 180;
                    }
                    //else rotate tobot

                    if (lookDir.y < 0)
                        currentCameraY = 0;
                    else currentCameraY = Remap(lookDir.y, 0, 1, 0, 180);

                    lastCamPos = lookDir;
                    Debug.Log("H: " + currentCameraX.ToString() + " V: " + currentCameraY.ToString());
                    ProcessHeadMovement(1, 1, true);
                }
            }
        }
        else if (!connecting)
        {
            connecting = true;
            Debug.Log("Lost connection");
            statusContainer.SetActive(true);
            statusText.text = "Lost connection.";
            Invoke("ConnectToRobot", 2f);
        }
    }

    private float Remap(float from, float fromMin, float fromMax, float toMin, float toMax)
    {
        var fromAbs = from - fromMin;
        var fromMaxAbs = fromMax - fromMin;

        var normal = fromAbs / fromMaxAbs;

        var toMaxAbs = toMax - toMin;
        var toAbs = toMaxAbs * normal;

        var to = toAbs + toMin;

        return to;
    }

    private void ProcessHeadMovement(float verticalMovement, float horizontalMovement, bool vr = false)
    {
        bool move = vr;
        int angle = 0;

        if (!vr && Mathf.Abs(verticalMovement) > 0.1f)
        {
            currentCameraY = Mathf.Clamp(currentCameraY - verticalMovement, 0, 180);
            move = true;
        }
        if(move)
        {
            angle = Mathf.Clamp(Mathf.RoundToInt(currentCameraY), 0, 180);
            RobotMove(MoveDirection.HeadVertical, angle);
            move = vr;
        }
        if (!vr && Mathf.Abs(horizontalMovement) > 0.1f)
        {
            currentCameraX = Mathf.Clamp(currentCameraX - horizontalMovement, 0, 180);
            move = true;
        }
        if (move)
        {
            angle = Mathf.Clamp(Mathf.RoundToInt(currentCameraX), 0, 180);
            RobotMove(MoveDirection.HeadHorizontal, angle);
        }
    }

    private void RobotMove(MoveDirection direction, float amount, bool forceStop = false)
    {
        if (!forceStop && direction == MoveDirection.Stop && stopped)
            return;

        try
        {
            byte[] bytesCommand = new byte[0];
            switch (direction)
            {
                case MoveDirection.Forward: bytesCommand = new byte[] { 0xff, 0x00, 0x01, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.Backwards: bytesCommand = new byte[] { 0xff, 0x00, 0x02, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.Left: bytesCommand = new byte[] { 0xff, 0x00, 0x03, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.Right: bytesCommand = new byte[] { 0xff, 0x00, 0x04, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.LeftTrackForward: bytesCommand = new byte[] { 0xff, 0x00, 0x06, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.RightTrackForward: bytesCommand = new byte[] { 0xff, 0x00, 0x05, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.LeftTrackBackWards: bytesCommand = new byte[] { 0xff, 0x00, 0x08, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.RightTrackBackWards: bytesCommand = new byte[] { 0xff, 0x00, 0x07, 0x00, 0xff }; stopped = false; break;
                case MoveDirection.HeadVertical: bytesCommand = new byte[] { 0xff, 0x01, 0x08, Convert.ToByte((int)amount & 0xFF), 0xff }; stopped = false; break;
                case MoveDirection.HeadHorizontal: bytesCommand = new byte[] { 0xff, 0x01, 0x07, Convert.ToByte((int)amount & 0xFF), 0xff }; stopped = false; break;
                case MoveDirection.Stop: bytesCommand = new byte[] { 0xff, 0x00, 0x00, 0x00, 0xff }; stopped = true; break;
            }
            clientSocket.Send(bytesCommand);

            //speed
            //FF  02  01  1-10 FF - left
            //FF  02  02  1-10 FF - right
            /*int trackSpeed = Mathf.Clamp(Mathf.RoundToInt(amount * 10), 1, 10);
            Debug.Log(Convert.ToByte(trackSpeed & 0xFF));
            bytesCommand = new byte[] { 0xff, 0x02, 0x01, Convert.ToByte(trackSpeed & 0xFF), 0xff };
            clientSocket.Send(bytesCommand);
            bytesCommand = new byte[] { 0xff, 0x02, 0x02, Convert.ToByte(trackSpeed & 0xFF), 0xff };
            clientSocket.Send(bytesCommand);*/
        }
        catch (Exception e)
        {
            Debug.Log(e.Message);
        }
    }

    private void OnApplicationQuit()
    {
        if (clientSocket != null && clientSocket.Connected)
        {
            RobotMove(MoveDirection.Stop, 0, true);
            clientSocket.Close();
        }

        if (_threadRunning)
        {
            _threadRunning = false;
            _thread.Join();
        }

        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }
    
    public void GetVideo()
    {
        texture = new Texture2D(2, 2);

        StopCoroutine("CameraConnect");
        StartCoroutine("CameraConnect");
    }

    private IEnumerator CameraConnect()
    {
        byte[] bytes = new byte[512 * 1024];

        using (UnityWebRequest webRequest = new UnityWebRequest(cameraStreamURL))
        {
            webRequest.downloadHandler = new CustomWebRequest(bytes, this);
            yield return webRequest.SendWebRequest();

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Debug.Log(webRequest.error);
                Invoke("GetVideo", 2f);
            }
            else
            {
                if(webRequest.downloadHandler.isDone)
                {
                    Debug.Log("camera stopped");
                    Invoke("GetVideo", 2f);
                }
            }
        }
    }

    public void ShowCameraImage(MemoryStream ms)
    {
        texture.LoadImage(ms.GetBuffer());
        rawImage.texture = texture;

        if (!rawImageFlipped)
        {
            rawImageFlipped = true;
            rawImage.rectTransform.localScale = new Vector3(-rawImage.rectTransform.localScale.x, rawImage.rectTransform.localScale.y, rawImage.rectTransform.localScale.z);
            ratioFitter.aspectRatio = (float)texture.width / texture.height;
        }
    }
}
