namespace HoymilesProtobufClient;

internal static class Crc16
{
    // Matches crcmod.mkCrcFun(0x18005, rev=True, initCrc=0xFFFF, xorOut=0x0000).
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;

        foreach (byte value in data)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }
}
