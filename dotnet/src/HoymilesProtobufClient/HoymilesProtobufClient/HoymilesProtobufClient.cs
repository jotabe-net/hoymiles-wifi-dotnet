using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

namespace HoymilesProtobufClient;

public sealed class HoymilesProtobufClient
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private readonly string _host;
    private readonly string? _localAddress;
    private readonly bool _isEncrypted;
    private readonly byte[] _encRand;
    private readonly int _timeoutSeconds;

    private int _sequence;
    private DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;

    public HoymilesProtobufClient(
        string host,
        string? localAddress = null,
        bool isEncrypted = false,
        byte[]? encRand = null,
        int timeoutSeconds = ProtocolConstants.DefaultTimeoutSeconds)
    {
        _host = host;
        _localAddress = localAddress;
        _isEncrypted = isEncrypted;
        _encRand = encRand ?? Array.Empty<byte>();
        _timeoutSeconds = timeoutSeconds;
    }

    public NetworkState State { get; private set; } = NetworkState.Unknown;

    public async Task<RealDataReqDTO?> GetRealDataAsync(CancellationToken cancellationToken = default)
    {
            var request = new RealDataResDTO
        {
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Time = NowInt(),
            Offset = ProtocolConstants.Offset,
            ErrorCode = 0,
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdRealDataResDto,
            request,
            RealDataReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<RealDataNewReqDTO?> GetRealDataNewAsync(CancellationToken cancellationToken = default)
    {
        var combined = new RealDataNewReqDTO();
        var request = new RealDataNewResDTO
        {
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Offset = ProtocolConstants.Offset,
            Time = NowInt(),
            Cp = 0,
        };

        RealDataNewReqDTO? initial = await SendRequestAsync(
            ProtocolConstants.CmdRealResDto,
            request,
            RealDataNewReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (initial is null)
        {
            return null;
        }

        combined.MergeFrom(initial);

        for (int cp = 1; cp < initial.Ap; cp++)
        {
            request.Cp = cp;
            RealDataNewReqDTO? extra = await SendRequestAsync(
                ProtocolConstants.CmdRealResDto,
                request,
                RealDataNewReqDTO.Parser,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (extra is not null)
            {
                combined.MergeFrom(extra);
            }
        }

        return combined.CalculateSize() > 0 ? combined : null;
    }

    public async Task<GetConfigReqDTO?> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var request = new GetConfigResDTO
        {
            Offset = ProtocolConstants.Offset,
            Time = (uint)(NowInt() - 60),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdGetConfig,
            request,
            GetConfigReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<NetworkInfoReqDTO?> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new NetworkInfoResDTO
        {
            Offset = ProtocolConstants.Offset,
            Time = NowUint(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdNetworkInfoRes,
            request,
            NetworkInfoReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<APPInfoDataReqDTO?> GetAppInformationDataAsync(CancellationToken cancellationToken = default)
    {
        var request = new APPInfoDataResDTO
        {
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Offset = ProtocolConstants.Offset,
            Time = NowUint(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdAppInfoDataResDto,
            request,
            APPInfoDataReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppGetHistPowerReqDTO?> GetHistoricalPowerAsync(CancellationToken cancellationToken = default)
    {
        var combined = new AppGetHistPowerReqDTO();
        var request = new AppGetHistPowerResDTO
        {
            Cp = 0,
            Offset = ProtocolConstants.Offset,
            RequestedTime = NowUint(),
            RequestedDay = 0,
        };

        AppGetHistPowerReqDTO? initial = await SendRequestAsync(
            ProtocolConstants.CmdAppGetHistPowerRes,
            request,
            AppGetHistPowerReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (initial is null)
        {
            return null;
        }

        uint initialAbsoluteStart = initial.AbsoluteStart;
        combined.MergeFrom(initial);

        for (int cp = 1; cp < initial.Ap; cp++)
        {
            request.Cp = cp;
            AppGetHistPowerReqDTO? extra = await SendRequestAsync(
                ProtocolConstants.CmdAppGetHistPowerRes,
                request,
                AppGetHistPowerReqDTO.Parser,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (extra is not null)
            {
                combined.MergeFrom(extra);
            }
        }

        combined.AbsoluteStart = initialAbsoluteStart;
        return combined.CalculateSize() > 0 ? combined : null;
    }

    public async Task<CommandReqDTO?> SetPowerLimitAsync(int powerLimit, CancellationToken cancellationToken = default)
    {
        if (powerLimit < 0 || powerLimit > 100)
        {
            return null;
        }

        int scaledPowerLimit = powerLimit * 10;
        var request = new CommandResDTO
        {
            Time = NowInt(),
            Action = ProtocolConstants.CmdActionLimitPower,
            PackageNub = 1,
            Tid = NowLong(),
            Data = $"A:{scaledPowerLimit},B:0,C:0\r",
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<SetConfigReqDTO?> SetWifiAsync(string ssid, string password, CancellationToken cancellationToken = default)
    {
        GetConfigReqDTO? config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            return null;
        }

        var request = ProtoMapping.InitializeSetConfig<SetConfigResDTO>(config);
        request.Time = NowUint();
        request.Offset = ProtocolConstants.Offset;
        request.AppPage = 1;
        request.NetmodeSelect = (int)NetmodeSelect.Wifi;
        request.WifiSsid = ssid ?? string.Empty;
        request.WifiPassword = password ?? string.Empty;

        return await SendRequestAsync(
            ProtocolConstants.CmdSetConfig,
            request,
            SetConfigReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> UpdateDtuFirmwareAsync(
        string firmwareUrl = ProtocolConstants.DtuFirmwareUrl00111,
        CancellationToken cancellationToken = default)
    {
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionDtuUpgrade,
            PackageNub = 1,
            Tid = NowLong(),
            Data = firmwareUrl + "\r",
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdCloudCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> RestartDtuAsync(CancellationToken cancellationToken = default)
    {
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionDtuReboot,
            PackageNub = 1,
            Tid = NowLong(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdCloudCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> TurnOnInverterAsync(string inverterSerial, CancellationToken cancellationToken = default)
    {
        long inverterSerialInt = ProtoMapping.ConvertInverterSerialNumber(inverterSerial);
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionMiStart,
            PackageNub = 1,
            DevKind = ProtocolConstants.DevDtu,
            Tid = NowLong(),
        };
        request.MiToSn.Add(inverterSerialInt);

        return await SendRequestAsync(
            ProtocolConstants.CmdCloudCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> TurnOffInverterAsync(string inverterSerial, CancellationToken cancellationToken = default)
    {
        long inverterSerialInt = ProtoMapping.ConvertInverterSerialNumber(inverterSerial);
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionMiShutdown,
            PackageNub = 1,
            DevKind = ProtocolConstants.DevDtu,
            Tid = NowLong(),
        };
        request.MiToSn.Add(inverterSerialInt);

        return await SendRequestAsync(
            ProtocolConstants.CmdCloudCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> RebootInverterAsync(string inverterSerial, CancellationToken cancellationToken = default)
    {
        long inverterSerialInt = ProtoMapping.ConvertInverterSerialNumber(inverterSerial);
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionInvReboot,
            PackageNub = 1,
            DevKind = ProtocolConstants.DevDtu,
            Tid = NowLong(),
        };
        request.MiToSn.Add(inverterSerialInt);

        return await SendRequestAsync(
            ProtocolConstants.CmdCloudCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<InfoDataReqDTO?> GetInformationDataAsync(CancellationToken cancellationToken = default)
    {
        var request = new InfoDataResDTO
        {
            TimeYmdHms = GetNowString(),
            Offset = ProtocolConstants.Offset,
            Time = NowInt(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdAppInfoDataResDto,
            request,
            InfoDataReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<HBReqDTO?> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var request = new HBResDTO
        {
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Offset = ProtocolConstants.Offset,
            Time = NowInt(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdHbResDto,
            request,
            HBReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> GetAlarmListAsync(CancellationToken cancellationToken = default)
    {
        var request = new CommandResDTO
        {
            Action = ProtocolConstants.CmdActionAlarmList,
            PackageNub = 1,
            DevKind = 0,
            Tid = NowLong(),
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandReqDTO?> EnablePerformanceDataModeAsync(CancellationToken cancellationToken = default)
    {
        var request = new CommandResDTO
        {
            Time = NowInt(),
            Action = ProtocolConstants.CmdActionPerformanceDataMode,
            PackageNub = 1,
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdCommandResDto,
            request,
            CommandReqDTO.Parser,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<GWInfoReqDTO?> GetGatewayInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new GWInfoResDTO
        {
            Time = NowInt(),
            Offset = ProtocolConstants.Offset,
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdGwInfoResDto,
            request,
            GWInfoReqDTO.Parser,
            isExtendedFormat: true,
            number: 255,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<GWNetInfoReq?> GetGatewayNetworkInfoAsync(ulong dtuSerialNumber, CancellationToken cancellationToken = default)
    {
        var request = new GWNetInfoRes
        {
            Time = NowInt(),
            Offset = ProtocolConstants.Offset,
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdGwNetInfoRes,
            request,
            GWNetInfoReq.Parser,
            isExtendedFormat: true,
            dtuSerialNumber: dtuSerialNumber,
            number: 255,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ESRegReqDTO?> GetEnergyStorageRegistryAsync(ulong dtuSerialNumber, CancellationToken cancellationToken = default)
    {
        var request = new ESRegResDTO
        {
            Time = NowInt(),
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Offset = ProtocolConstants.Offset,
            Cp = 0,
        };

        return await SendRequestAsync(
            ProtocolConstants.CmdEsRegResDto,
            request,
            ESRegReqDTO.Parser,
            isExtendedFormat: true,
            dtuSerialNumber: dtuSerialNumber,
            number: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ESDataReqDTO?> GetEnergyStorageDataAsync(
        ulong dtuSerialNumber,
        long inverterSerialNumber,
        CancellationToken cancellationToken = default)
    {
        var request = new ESDataResDTO
        {
            Time = NowInt(),
            TimeYmdHms = ByteString.CopyFromUtf8(GetNowString()),
            Offset = ProtocolConstants.Offset,
            Cp = 0,
            SerialNumber = inverterSerialNumber,
        };
        return await SendRequestAsync(
            ProtocolConstants.CmdEsDataDto,
            request,
            ESDataReqDTO.Parser,
            isExtendedFormat: true,
            dtuSerialNumber: dtuSerialNumber,
            number: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ESUserSetPutReqDTO?> SetEnergyStorageWorkingModeAsync(
        ulong dtuSerialNumber,
        long inverterSerialNumber,
        BmsWorkingMode bmsWorkingMode,
        int? revSoc = null,
        IReadOnlyList<DateBean>? timeSettings = null,
        int? maxPower = null,
        int? peakSoc = null,
        int? peakMeterPower = null,
        IReadOnlyList<TimePeriodBean>? timePeriods = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ESUserSetPutResDTO
        {
            Time = NowInt(),
            Tid = NowLong(),
            Mode = (int)bmsWorkingMode,
        };
        request.SerialNumber.Add(inverterSerialNumber);

        if (revSoc is not null)
        {
            request.RevSoc = revSoc.Value;
        }

        if (maxPower is not null)
        {
            if (maxPower.Value < 0 || maxPower.Value > 100)
            {
                return null;
            }

            request.MaxPower = maxPower.Value * 10;
        }

        if (bmsWorkingMode == BmsWorkingMode.Economic)
        {
            if (timeSettings is null)
            {
                return null;
            }

            foreach (DateBean timeSetting in timeSettings)
            {
                var setDate = new EconomicsSetDateMO
                {
                    Dr = ProtoMapping.EncodeDateTimeRange(timeSetting.StartDate, timeSetting.EndDate, "."),
                };

                if (timeSetting.Time is null || timeSetting.Time.Count != 2)
                {
                    return null;
                }

                for (int idx = 0; idx < timeSetting.Time.Count; idx++)
                {
                    TimeBean timeRange = timeSetting.Time[idx];
                    var setWeek = new EconomicsSetWeekMO
                    {
                        Wr = ProtoMapping.EncodeWeekRange(timeRange.Week),
                    };

                    foreach (DurationBean duration in timeRange.Durations)
                    {
                        if (duration.Type == TariffType.Peak)
                        {
                            setWeek.PeakIn = ProtoMapping.FloatToScaledInt(duration.InPrice);
                            setWeek.PeakOut = ProtoMapping.FloatToScaledInt(duration.OutPrice);
                            setWeek.PeakTime = ProtoMapping.EncodeDateTimeRange(duration.StartTime, duration.EndTime, ":");
                        }
                        else if (duration.Type == TariffType.OffPeak)
                        {
                            setWeek.ValleyIn = ProtoMapping.FloatToScaledInt(duration.InPrice);
                            setWeek.ValleyOut = ProtoMapping.FloatToScaledInt(duration.OutPrice);
                            setWeek.ValleyTime = ProtoMapping.EncodeDateTimeRange(duration.StartTime, duration.EndTime, ":");
                        }
                        else if (duration.Type == TariffType.PartialPeak)
                        {
                            setWeek.PartialPeakIn = ProtoMapping.FloatToScaledInt(duration.InPrice);
                            setWeek.PartialPeakOut = ProtoMapping.FloatToScaledInt(duration.OutPrice);
                        }
                    }

                    if (idx == 0)
                    {
                        setDate.W1 = setWeek;
                    }
                    else if (idx == 1)
                    {
                        setDate.W2 = setWeek;
                    }
                }

                request.Date.Add(setDate);
            }
        }
        else if (bmsWorkingMode == BmsWorkingMode.PeakShaving)
        {
            if (peakSoc is null || peakMeterPower is null)
            {
                return null;
            }

            request.PeakSoc = peakSoc.Value;
            request.PeakMeterpwr = peakMeterPower.Value;
        }
        else if (bmsWorkingMode == BmsWorkingMode.TimeOfUse)
        {
            if (timePeriods is null)
            {
                return null;
            }

            foreach (TimePeriodBean period in timePeriods)
            {
                bool missingRequired =
                    string.IsNullOrWhiteSpace(period.ChargeTimeFrom) ||
                    string.IsNullOrWhiteSpace(period.ChargeTimeTo) ||
                    string.IsNullOrWhiteSpace(period.DischargeTimeFrom) ||
                    string.IsNullOrWhiteSpace(period.DischargeTimeTo) ||
                    period.ChargePower is null ||
                    period.DischargePower is null ||
                    period.MaxSoc is null ||
                    period.MinSoc is null;

                if (missingRequired)
                {
                    return null;
                }

                bool invalidRange =
                    period.ChargePower < 0 || period.ChargePower > 100 ||
                    period.DischargePower < 0 || period.DischargePower > 100 ||
                    period.MaxSoc < 0 || period.MaxSoc > 100 ||
                    period.MinSoc < 0 || period.MinSoc > 100;

                if (invalidRange)
                {
                    return null;
                }

                var timeOfUse = new TimeOfUseSetMO
                {
                    ChrgTr = ProtoMapping.EncodeDateTimeRange(period.ChargeTimeFrom, period.ChargeTimeTo, ":"),
                    DischrgTr = ProtoMapping.EncodeDateTimeRange(period.DischargeTimeFrom, period.DischargeTimeTo, ":"),
                    ChrgPwr = period.ChargePower.Value,
                    DischrgPwr = period.DischargePower.Value,
                    MaxSoc = period.MaxSoc.Value,
                    MinSoc = period.MinSoc.Value,
                };

                request.Tou.Add(timeOfUse);
            }
        }

        return await SendRequestAsync(
            ProtocolConstants.CmdEsUserSetResDto,
            request,
            ESUserSetPutReqDTO.Parser,
            isExtendedFormat: true,
            dtuSerialNumber: dtuSerialNumber,
            number: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        ushort command,
        TRequest request,
        MessageParser<TResponse> parser,
        int dtuPort = ProtocolConstants.DtuPort,
        bool isExtendedFormat = false,
        ulong dtuSerialNumber = 0,
        ushort number = 0,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage<TResponse>
    {
        byte[] message = GenerateMessage(command, request, isExtendedFormat, dtuSerialNumber, number);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRequestTime;
            if (elapsed < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(TimeSpan.FromSeconds(2) - elapsed, cancellationToken).ConfigureAwait(false);
            }

            using var client = new TcpClient();
            if (!string.IsNullOrWhiteSpace(_localAddress))
            {
                client.Client.Bind(new IPEndPoint(IPAddress.Parse(_localAddress), 0));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            await client.ConnectAsync(_host, dtuPort, timeoutCts.Token).ConfigureAwait(false);

            using NetworkStream stream = client.GetStream();
            await stream.WriteAsync(message, timeoutCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            byte[] buffer = new byte[2048];
            int bytesRead = await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                SetState(NetworkState.Offline);
                return null;
            }

            byte[] payload = buffer[..bytesRead];
            _lastRequestTime = DateTimeOffset.UtcNow;
            return ParseResponse(payload, parser, isExtendedFormat);
        }
        catch
        {
            SetState(NetworkState.Offline);
            return null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private byte[] GenerateMessage<TRequest>(ushort command, TRequest request, bool isExtendedFormat, ulong serialNumber, ushort number)
        where TRequest : class, IMessage
    {
        _sequence = (_sequence + 1) & 0xFFFF;
        ushort sequence = (ushort)_sequence;

        byte[] requestBytes;
        ushort crc16;

        if (_isEncrypted && !isExtendedFormat && !ProtocolConstants.NotEncryptedCommands.Contains(command))
        {
            requestBytes = CryptoUtil.CryptData(true, _encRand, command, sequence, request.ToByteArray());
            crc16 = Crc16.Compute(requestBytes.AsSpan(0, requestBytes.Length - 16));
        }
        else
        {
            requestBytes = request.ToByteArray();
            crc16 = Crc16.Compute(requestBytes);
        }

        int metadataLength = isExtendedFormat ? 2 + 2 + 2 + 2 + 8 + 2 + 2 : 2 + 2 + 2;
        byte[] metadata = new byte[metadataLength];

        BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(0, 2), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(2, 2), crc16);

        if (isExtendedFormat)
        {
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(4, 2), (ushort)(24 + requestBytes.Length));
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(6, 2), 14);
            BinaryPrimitives.WriteUInt64BigEndian(metadata.AsSpan(8, 8), serialNumber);
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(16, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(18, 2), number);
        }
        else
        {
            ushort readLength = (ushort)(_isEncrypted && !ProtocolConstants.NotEncryptedCommands.Contains(command)
                ? requestBytes.Length - 16 + 10
                : requestBytes.Length + 10);
            BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(4, 2), readLength);
        }

        byte[] result = new byte[4 + metadata.Length + requestBytes.Length];
        result[0] = (byte)'H';
        result[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), command);
        metadata.CopyTo(result.AsSpan(4));
        requestBytes.CopyTo(result.AsSpan(4 + metadata.Length));

        return result;
    }

    private TResponse? ParseResponse<TResponse>(byte[] buffer, MessageParser<TResponse> parser, bool isExtendedFormat)
        where TResponse : class, IMessage<TResponse>
    {
        try
        {
            if (buffer.Length < 10)
            {
                return null;
            }

            ushort tag = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
            ushort seq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4, 2));
            ushort crc16Target = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6, 2));
            ushort readLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(8, 2));

            int expectedLength = _isEncrypted && !ProtocolConstants.NotEncryptedCommands.Contains(tag) && !isExtendedFormat
                ? readLength + 16
                : readLength;

            if (buffer.Length < expectedLength)
            {
                return null;
            }

            ushort crc16Response = isExtendedFormat
                ? Crc16.Compute(buffer.AsSpan(24, readLength - 24))
                : Crc16.Compute(buffer.AsSpan(10, readLength - 10));

            if (crc16Response != crc16Target)
            {
                return null;
            }

            byte[] responseBytes;
            if (isExtendedFormat)
            {
                responseBytes = buffer[24..readLength];
            }
            else if (_isEncrypted && !ProtocolConstants.NotEncryptedCommands.Contains(tag))
            {
                responseBytes = CryptoUtil.CryptData(false, _encRand, tag, seq, buffer[10..expectedLength]);
            }
            else
            {
                responseBytes = buffer[10..readLength];
            }

            TResponse parsed = parser.ParseFrom(responseBytes);
            SetState(NetworkState.Online);
            return parsed;
        }
        catch
        {
            SetState(NetworkState.Unknown);
            return null;
        }
    }

    private void SetState(NetworkState state)
    {
        State = state;
    }

    private static string GetNowString()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static int NowInt()
    {
        return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static uint NowUint()
    {
        return (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static long NowLong()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
