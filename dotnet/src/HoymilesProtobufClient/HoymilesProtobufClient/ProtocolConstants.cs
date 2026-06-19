using System.Collections.Frozen;

namespace HoymilesProtobufClient;

internal static class ProtocolConstants
{
    public const int DtuPort = 10081;
    public const int DefaultTimeoutSeconds = 10;
    public const int Offset = 28800;

    public const ushort CmdAppInfoDataReqDto = 0xA201;
    public const ushort CmdAppInfoDataResDto = 0xA301;
    public const ushort CmdHbReqDto = 0xA302;
    public const ushort CmdHbResDto = 0xA302;
    public const ushort CmdRealDataResDto = 0xA303;
    public const ushort CmdCommandResDto = 0xA305;
    public const ushort CmdGetConfig = 0xA309;
    public const ushort CmdSetConfig = 0xA310;
    public const ushort CmdRealResDto = 0xA311;
    public const ushort CmdNetworkInfoRes = 0xA314;
    public const ushort CmdAppGetHistPowerRes = 0xA315;

    public const ushort CmdEsRegResDto = 0xC302;
    public const ushort CmdEsDataDto = 0xC303;
    public const ushort CmdEsUserSetResDto = 0xC308;

    public const ushort CmdGwInfoResDto = 0xDB01;
    public const ushort CmdGwNetInfoRes = 0xDB06;

    public const ushort CmdCloudCommandResDto = 0x2305;

    public const int CmdActionDtuReboot = 1;
    public const int CmdActionDtuUpgrade = 2;
    public const int CmdActionMiStart = 6;
    public const int CmdActionMiShutdown = 7;
    public const int CmdActionLimitPower = 8;
    public const int CmdActionPerformanceDataMode = 33;
    public const int CmdActionAlarmList = 50;
    public const int CmdActionInvReboot = 8195;

    public const int DevDtu = 1;

    public const string DtuFirmwareUrl00111 = "http://fwupdate.hoymiles.com/cfs/bin/2311/06/,1488725943932555264.bin";

    public static readonly FrozenSet<ushort> NotEncryptedCommands = new HashSet<ushort>
    {
        CmdAppInfoDataResDto,
        CmdAppInfoDataReqDto,
        CmdHbReqDto,
        CmdHbResDto,
    }.ToFrozenSet();
}
