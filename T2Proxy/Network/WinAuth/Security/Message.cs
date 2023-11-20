
using System;
using System.Text;

namespace T2Proxy.Network.WinAuth.Security;

internal class Message
{
    private static readonly byte[] header = { 0x4e, 0x54, 0x4c, 0x4d, 0x53, 0x53, 0x50, 0x00 };

    private readonly int type;

    internal Message(byte[] message)
    {
        type = 3;

        if (message == null) throw new ArgumentNullException(nameof(message));

        if (message.Length < 12)
        {
            var msg = "Minimum Type3 message length is 12 bytes.";
            throw new ArgumentOutOfRangeException(nameof(message), message.Length, msg);
        }

        if (!CheckHeader(message))
        {
            var msg = "Invalid Type3 message header.";
            throw new ArgumentException(msg, nameof(message));
        }

        if (LittleEndian.ToUInt16(message, 56) != message.Length)
        {
            var msg = "Invalid Type3 message length.";
            throw new ArgumentException(msg, nameof(message));
        }

        if (message.Length >= 64)
            Flags = (Common.NtlmFlags)LittleEndian.ToUInt32(message, 60);
        else
            Flags = (Common.NtlmFlags)0x8201;

        int domLen = LittleEndian.ToUInt16(message, 28);
        int domOff = LittleEndian.ToUInt16(message, 32);

        Domain = DecodeString(message, domOff, domLen);

        int userLen = LittleEndian.ToUInt16(message, 36);
        int userOff = LittleEndian.ToUInt16(message, 40);

        Username = DecodeString(message, userOff, userLen);
    }

    internal string Domain { get; }

    internal string Username { get; }

    internal Common.NtlmFlags Flags { get; set; }

    private string DecodeString(byte[] buffer, int offset, int len)
    {
        if ((Flags & Common.NtlmFlags.NegotiateUnicode) != 0) return Encoding.Unicode.GetString(buffer, offset, len);

        return Encoding.ASCII.GetString(buffer, offset, len);
    }

    protected bool CheckHeader(byte[] message)
    {
        for (var i = 0; i < header.Length; i++)
            if (message[i] != header[i])
                return false;

        return LittleEndian.ToUInt32(message, 8) == type;
    }
}