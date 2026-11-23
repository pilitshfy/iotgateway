using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Admin.NET.Web.Core;

/// <summary>
/// Modbus TCP 模拟从站，使用与 NModbus DataStore 一致的 1 基数据索引
/// </summary>
public class IotModbusSlaveService : BackgroundService
{
    private const byte SlaveId = 1;
    private const int Port = 503;
    private const int StoreSize = ushort.MaxValue + 2;
    private const int MaxPduSize = 253;

    private const byte IllegalFunction = 0x01;
    private const byte IllegalDataAddress = 0x02;
    private const byte IllegalDataValue = 0x03;
    private const byte SlaveDeviceFailure = 0x04;

    private readonly ILogger<IotModbusSlaveService> _logger;
    private readonly object _dataLock = new();

    // 数组索引 0 不使用，协议地址 0 对应索引 1
    private readonly bool[] _coils = new bool[StoreSize];
    private readonly bool[] _discreteInputs = new bool[StoreSize];
    private readonly ushort[] _holdingRegisters = new ushort[StoreSize];
    private readonly ushort[] _inputRegisters = new ushort[StoreSize];

    public IotModbusSlaveService(ILogger<IotModbusSlaveService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, Port);
        var clients = new ConcurrentDictionary<int, TcpClient>();
        var clientTasks = new List<Task>();
        var nextClientId = 0;
        var started = false;

        using var runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? simulationTask = null;

        try
        {
            listener.Start();
            started = true;
            _logger.LogInformation("Modbus TCP 模拟从站已启动，监听 {Address}:{Port}，Slave ID {SlaveId}", IPAddress.Any, Port, SlaveId);

            simulationTask = SimulateAsync(runtimeCts.Token);

            while (!runtimeCts.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(runtimeCts.Token);
                var clientId = ++nextClientId;
                clients[clientId] = client;

                clientTasks.RemoveAll(task => task.IsCompleted);
                clientTasks.Add(HandleClientAsync(client, clientId, clients, runtimeCts.Token));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modbus TCP 模拟从站启动或监听失败，地址 {Address}:{Port}", IPAddress.Any, Port);
        }
        finally
        {
            runtimeCts.Cancel();
            listener.Stop();

            foreach (var client in clients.Values)
            {
                client.Close();
            }

            var pendingTasks = new List<Task>(clientTasks);
            if (simulationTask != null)
                pendingTasks.Add(simulationTask);

            try
            {
                await Task.WhenAll(pendingTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 Modbus TCP 模拟从站时发生异常");
            }

            if (started)
                _logger.LogInformation("Modbus TCP 模拟从站已停止，端口 {Port} 已释放", Port);
        }
    }

    private async Task SimulateAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    lock (_dataLock)
                    {
                        for (var i = 1; i <= 20; i++)
                        {
                            _holdingRegisters[i] = (ushort)Random.Shared.Next(0, 32768);
                            _inputRegisters[i] = (ushort)Random.Shared.Next(0, 32768);
                            _coils[i] = Random.Shared.Next(2) == 0;
                            _discreteInputs[i] = Random.Shared.Next(2) == 0;
                        }

                        _holdingRegisters[1] = (ushort)Random.Shared.Next(2000, 3000);
                        _holdingRegisters[2] = (ushort)Random.Shared.Next(4000, 7000);
                        _holdingRegisters[7] = (ushort)Random.Shared.Next(0, 10000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新 Modbus 模拟数据失败");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        int clientId,
        ConcurrentDictionary<int, TcpClient> clients,
        CancellationToken cancellationToken)
    {
        var remoteEndPoint = "未知";

        try
        {
            remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? remoteEndPoint;
            _logger.LogDebug("Modbus TCP 客户端已连接：{RemoteEndPoint}", remoteEndPoint);

            using var stream = client.GetStream();
            var header = new byte[7];
            var pdu = new byte[MaxPduSize];

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, header, header.Length, cancellationToken))
                    break;

                var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                if (protocolId != 0 || length < 2 || length > MaxPduSize + 1)
                {
                    _logger.LogWarning(
                        "Modbus TCP 客户端 {RemoteEndPoint} 发送了无效 MBAP 帧（Protocol ID {ProtocolId}，Length {Length}），连接已关闭",
                        remoteEndPoint, protocolId, length);
                    break;
                }

                var pduLength = length - 1;
                if (!await ReadExactAsync(stream, pdu, pduLength, cancellationToken))
                    break;

                if (header[6] != SlaveId)
                    continue;

                var responsePdu = ProcessRequest(pdu.AsSpan(0, pduLength));
                var response = BuildResponse(header, responsePdu);
                await stream.WriteAsync(response, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            _logger.LogDebug(ex, "Modbus TCP 客户端 {RemoteEndPoint} 连接断开", remoteEndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Modbus TCP 客户端 {RemoteEndPoint} 时发生异常", remoteEndPoint);
        }
        finally
        {
            clients.TryRemove(clientId, out _);
            client.Dispose();
            _logger.LogDebug("Modbus TCP 客户端已断开：{RemoteEndPoint}", remoteEndPoint);
        }
    }

    private byte[] ProcessRequest(ReadOnlySpan<byte> request)
    {
        var function = request[0];

        try
        {
            return function switch
            {
                1 => ReadBits(request, _coils),
                2 => ReadBits(request, _discreteInputs),
                3 => ReadRegisters(request, _holdingRegisters),
                4 => ReadRegisters(request, _inputRegisters),
                5 => WriteSingleCoil(request),
                6 => WriteSingleRegister(request),
                15 => WriteMultipleCoils(request),
                16 => WriteMultipleRegisters(request),
                _ => ExceptionResponse(function, IllegalFunction)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Modbus 请求失败，功能码 {FunctionCode}", function);
            return ExceptionResponse(function, SlaveDeviceFailure);
        }
    }

    private byte[] ReadBits(ReadOnlySpan<byte> request, bool[] store)
    {
        var function = request[0];
        if (request.Length != 5)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));
        if (quantity < 1 || quantity > 2000)
            return ExceptionResponse(function, IllegalDataValue);
        if (!IsValidRange(address, quantity))
            return ExceptionResponse(function, IllegalDataAddress);

        var byteCount = (quantity + 7) / 8;
        var response = new byte[byteCount + 2];
        response[0] = function;
        response[1] = (byte)byteCount;

        lock (_dataLock)
        {
            for (var i = 0; i < quantity; i++)
            {
                if (store[address + i + 1])
                    response[i / 8 + 2] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] ReadRegisters(ReadOnlySpan<byte> request, ushort[] store)
    {
        var function = request[0];
        if (request.Length != 5)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));
        if (quantity < 1 || quantity > 125)
            return ExceptionResponse(function, IllegalDataValue);
        if (!IsValidRange(address, quantity))
            return ExceptionResponse(function, IllegalDataAddress);

        var response = new byte[quantity * 2 + 2];
        response[0] = function;
        response[1] = (byte)(quantity * 2);

        lock (_dataLock)
        {
            for (var i = 0; i < quantity; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(i * 2 + 2, 2), store[address + i + 1]);
            }
        }

        return response;
    }

    private byte[] WriteSingleCoil(ReadOnlySpan<byte> request)
    {
        const byte function = 5;
        if (request.Length != 5)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));
        if (value != 0x0000 && value != 0xFF00)
            return ExceptionResponse(function, IllegalDataValue);

        lock (_dataLock)
        {
            _coils[address + 1] = value == 0xFF00;
        }

        return request.ToArray();
    }

    private byte[] WriteSingleRegister(ReadOnlySpan<byte> request)
    {
        const byte function = 6;
        if (request.Length != 5)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));

        lock (_dataLock)
        {
            _holdingRegisters[address + 1] = value;
        }

        return request.ToArray();
    }

    private byte[] WriteMultipleCoils(ReadOnlySpan<byte> request)
    {
        const byte function = 15;
        if (request.Length < 6)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));
        var byteCount = request[5];
        if (quantity < 1 || quantity > 1968 || byteCount != (quantity + 7) / 8 || request.Length != byteCount + 6)
            return ExceptionResponse(function, IllegalDataValue);
        if (!IsValidRange(address, quantity))
            return ExceptionResponse(function, IllegalDataAddress);

        lock (_dataLock)
        {
            for (var i = 0; i < quantity; i++)
            {
                _coils[address + i + 1] = (request[i / 8 + 6] & (1 << (i % 8))) != 0;
            }
        }

        return WriteMultipleResponse(function, address, quantity);
    }

    private byte[] WriteMultipleRegisters(ReadOnlySpan<byte> request)
    {
        const byte function = 16;
        if (request.Length < 6)
            return ExceptionResponse(function, IllegalDataValue);

        var address = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(1, 2));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(3, 2));
        var byteCount = request[5];
        if (quantity < 1 || quantity > 123 || byteCount != quantity * 2 || request.Length != byteCount + 6)
            return ExceptionResponse(function, IllegalDataValue);
        if (!IsValidRange(address, quantity))
            return ExceptionResponse(function, IllegalDataAddress);

        lock (_dataLock)
        {
            for (var i = 0; i < quantity; i++)
            {
                _holdingRegisters[address + i + 1] = BinaryPrimitives.ReadUInt16BigEndian(request.Slice(i * 2 + 6, 2));
            }
        }

        return WriteMultipleResponse(function, address, quantity);
    }

    private static bool IsValidRange(ushort address, ushort quantity)
    {
        return address + quantity <= ushort.MaxValue + 1;
    }

    private static byte[] WriteMultipleResponse(byte function, ushort address, ushort quantity)
    {
        var response = new byte[5];
        response[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(1, 2), address);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(3, 2), quantity);
        return response;
    }

    private static byte[] ExceptionResponse(byte function, byte exceptionCode)
    {
        return [(byte)(function | 0x80), exceptionCode];
    }

    private static byte[] BuildResponse(byte[] requestHeader, byte[] responsePdu)
    {
        var response = new byte[responsePdu.Length + 7];
        response[0] = requestHeader[0];
        response[1] = requestHeader[1];
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(responsePdu.Length + 1));
        response[6] = SlaveId;
        responsePdu.CopyTo(response, 7);
        return response;
    }

    private static async Task<bool> ReadExactAsync(
        NetworkStream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }
}
