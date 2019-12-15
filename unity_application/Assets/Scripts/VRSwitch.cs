using System.Collections;
using UnityEngine;
using UnityEngine.XR;

public class VRSwitch : MonoBehaviour
{

    public CanvasGroup cg;

    public void ToggleVR()
    {
        StopAllCoroutines();
        StartCoroutine("SwitchVR");
    }
    
    IEnumerator SwitchVR()
    {
        string desiredDevice = "cardboard";
        if (XRSettings.enabled)
        {
            desiredDevice = "";
            XRSettings.LoadDeviceByName("");
            cg.alpha = 1;
            cg.interactable = true;
            yield return null;
            ResetCameras();
        }
        else
        {
            if (string.Compare(XRSettings.loadedDeviceName, desiredDevice, true) != 0)
            {
                XRSettings.LoadDeviceByName(desiredDevice);
                yield return null;
            }

            XRSettings.enabled = true;
            cg.alpha = 0;
            cg.interactable = false;
        }
    }

    void ResetCameras()
    {
        try
        {
            for (int i = 0; i < Camera.allCameras.Length; i++)
            {
                Camera cam = Camera.allCameras[i];
                if (cam.enabled && cam.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    cam.transform.localPosition = Vector3.zero;
                    cam.transform.localRotation = Quaternion.identity;
                }
            }
        }
        catch { }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (XRSettings.enabled)
                ToggleVR();
            else Application.Quit();
        }
    }
}
