using UnityEngine;

public class CanvasScaler : MonoBehaviour {

	void Awake () {
        /*float camHeight;
        if (Camera.main.orthographic)
            camHeight = Camera.main.orthographicSize * 2;
        else
        {
            float distanceToMain = Vector3.Distance(Camera.main.transform.position, transform.position);
            camHeight = 2.0f * distanceToMain * Mathf.Tan(Mathf.Deg2Rad * (Camera.main.fieldOfView * 0.5f));
        }

        float scale = (camHeight / screenSize.y) * m_ScaleFactor;*/
        GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
    }
}
