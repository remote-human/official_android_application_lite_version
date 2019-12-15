using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System;
using System.IO;

public class CustomWebRequest : DownloadHandlerScript
{
    private const byte picMarker = 0xFF;
    private const byte picStart = 0xD8;
    private const byte picEnd = 0xD9;
    private int frameIdx = 0;       // Last written byte location in the frame buffer
    private bool inPicture = false;  // Are we currently parsing a picture ?
    private byte current = 0x00;    // The last byte read
    private byte previous = 0x00;   // The byte before
    private byte[] jpg_buf;
    private RobotController rc;

    // Standard scripted download handler - will allocate memory on each ReceiveData callback
    public CustomWebRequest()
        : base()
    {
    }

    // Pre-allocated scripted download handler
    // Will reuse the supplied byte array to deliver data.
    // Eliminates memory allocation.
    public CustomWebRequest(byte[] buffer, RobotController rc)
        : base(buffer)
    {
        frameIdx = 0;       // Last written byte location in the frame buffer
        inPicture = false;  // Are we currently parsing a picture ?
        current = 0x00;    // The last byte read
        previous = 0x00;   // The byte before
        jpg_buf = new byte[buffer.Length];
        this.rc = rc;
    }

    // Required by DownloadHandler base class. Called when you address the 'bytes' property.
    protected override byte[] GetData() { return null; }

    // Called once per frame when data has been received from the network.
    protected override bool ReceiveData(byte[] byteFromCamera, int dataLength)
    {
        if (byteFromCamera == null || byteFromCamera.Length < 1)
        {
            Debug.Log("CustomWebRequest :: ReceiveData - received a null/empty buffer");
            return false;
        }

        //Search of JPEG Image
        parseStreamBuffer(jpg_buf, ref frameIdx, dataLength, byteFromCamera, ref inPicture, ref previous, ref current);

        return true;
    }


    // Called when all data has been received from the server and delivered via ReceiveData
    protected override void CompleteContent()
    {
        Debug.Log("CustomWebRequest :: CompleteContent - DOWNLOAD COMPLETE!");
    }

    void parseStreamBuffer(byte[] frameBuffer, ref int frameIdx, int streamLength, byte[] streamBuffer, ref bool inPicture, ref byte previous, ref byte current)
    {
        var idx = 0;
        while (idx < streamLength)
        {
            if (inPicture)
            {
                parsePicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current);
            }
            else
            {
                searchPicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture, ref previous, ref current);
            }
        }
    }

    void searchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];

            // JPEG picture start ?
            if (previous == picMarker && current == picStart)
            {
                frameIdx = 2;
                frameBuffer[0] = picMarker;
                frameBuffer[1] = picStart;
                inPicture = true;
                return;
            }
        } while (idx < streamLength);
    }

    // While we are parsing a picture, fill the frame buffer until a FFD9 is reach.

    void parsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer, ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];
            frameBuffer[frameIdx++] = current;

            // JPEG picture end ?
            if (previous == picMarker && current == picEnd)
            {
                using (var ms = new MemoryStream(frameBuffer, 0, frameIdx, false, true))
                {
                    rc.ShowCameraImage(ms);
                }

                inPicture = false;
                return;
            }
        } while (idx < streamLength);
    }
}
