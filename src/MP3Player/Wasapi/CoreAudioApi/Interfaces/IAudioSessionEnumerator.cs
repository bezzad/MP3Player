﻿using System.Runtime.InteropServices;

namespace MP3Player.Wasapi.CoreAudioApi.Interfaces
{
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);

        int GetSession(int sessionCount, out IAudioSessionControl session);
    }
}
