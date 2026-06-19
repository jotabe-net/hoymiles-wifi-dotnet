namespace HoymilesProtobufClient;

public enum NetworkState
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
}

public enum NetmodeSelect
{
    Wifi = 1,
    Sim = 2,
    Lan = 3,
}

public enum BmsWorkingMode
{
    SelfUse = 1,
    Economic = 2,
    BackupPower = 3,
    PureOffGrid = 4,
    ForcedCharging = 5,
    ForcedDischarge = 6,
    PeakShaving = 7,
    TimeOfUse = 8,
    Unknown = -1,
}

public enum TariffType
{
    OffPeak = 1,
    PartialPeak = 2,
    Peak = 3,
}

public sealed class DurationBean
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public double? InPrice { get; set; }
    public double? OutPrice { get; set; }
    public TariffType? Type { get; set; }
}

public sealed class TimeBean
{
    public List<DurationBean> Durations { get; set; } = [];
    public List<int> Week { get; set; } = [];
}

public sealed class DateBean
{
    public string? EndDate { get; set; }
    public string? StartDate { get; set; }
    public List<TimeBean> Time { get; set; } = [];
}

public sealed class TimePeriodBean
{
    public string? ChargeTimeFrom { get; set; }
    public string? ChargeTimeTo { get; set; }
    public string? DischargeTimeFrom { get; set; }
    public string? DischargeTimeTo { get; set; }
    public int? ChargePower { get; set; }
    public int? DischargePower { get; set; }
    public int? MaxSoc { get; set; }
    public int? MinSoc { get; set; }
}
