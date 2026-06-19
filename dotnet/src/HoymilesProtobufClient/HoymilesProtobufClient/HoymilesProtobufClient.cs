using System.Buffers.Binary;
using System.Diagnostics;
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
        _encRand = encRand ?? [];
        _timeoutSeconds = timeoutSeconds;
    }

    public NetworkState State { get; private set; } = NetworkState.Unknown;

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
}
