﻿using System;
using System.Runtime.InteropServices;

namespace MP3Player.Wasapi.CoreAudioApi.Interfaces
{
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    interface IAudioCaptureClient
    {
        /*HRESULT GetBuffer(
            BYTE** ppData,
            UINT32* pNumFramesToRead,
            DWORD* pdwFlags,
            UINT64* pu64DevicePosition,
            UINT64* pu64QPCPosition
            );*/

        int GetBuffer(
            out IntPtr dataBuffer, 
            out int numFramesToRead, 
            out AudioClientBufferFlags bufferFlags,
            out long devicePosition,
            out long qpcPosition);

        int ReleaseBuffer(int numFramesRead);

        int GetNextPacketSize(out int numFramesInNextPacket);

    }
}
