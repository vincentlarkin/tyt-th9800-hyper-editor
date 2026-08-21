using System.IO.Ports;

namespace TH9800.HyperEditor.Services;

public sealed class RadioCloneProtocol
{
    private const int BaudRate = 38_400;
    private const int BlockLength = 0x80;
    private static readonly byte[] Handshake = [0x02, 0x20, 0x50, 0x52, 0x4F, 0x47, 0x52, 0x41];
    private static readonly byte[] Ack = [0x41];

    public byte[] ReadImage(string portName, IProgress<int>? progress = null)
    {
        using var session = Open(portName);
        var image = new List<byte>(HyperImage.ExpectedLength);
        image.AddRange(session.Ident);
        while (image.Count < 16) image.Add(0);

        var blockCount = (0x10000 - 0x0100) / BlockLength;
        var blockNumber = 0;
        for (var address = 0x0100; address < 0x10000; address += BlockLength)
        {
            var request = new byte[] { 0x52, (byte)(address >> 8), (byte)address, BlockLength };
            session.Port.Write(request, 0, request.Length);
            var reply = ReadExact(session.Port, BlockLength + 4);
            if (reply[1] != request[1] || reply[2] != request[2] || reply[3] != BlockLength)
                throw new IOException($"Unexpected reply header at radio address 0x{address:X4}.");
            image.AddRange(reply.AsSpan(4, BlockLength).ToArray());
            session.Port.Write(Ack, 0, 1);
            ExpectAck(session.Port, $"read block 0x{address:X4}");
            progress?.Report(++blockNumber * 100 / blockCount);
        }

        var end = "ENDR"u8.ToArray();
        session.Port.Write(end, 0, end.Length);
        session.Completed = true;
        var result = image.ToArray();
        HyperImage.Validate(result);
        return result;
    }

    public void WriteImage(string portName, byte[] image, IProgress<int>? progress = null)
    {
        HyperImage.Validate(image);
        using var session = Open(portName);
        for (var index = 0; index < session.Ident.Length; index++)
            if (session.Ident[index] != image[index])
                throw new InvalidDataException("The connected radio identifier does not match the loaded image.");

        const int uploadEnd = 0xF400;
        var blockCount = (uploadEnd - 0x0100) / BlockLength;
        var blockNumber = 0;
        for (var address = 0x0100; address < uploadEnd; address += BlockLength)
        {
            var imageOffset = address - 0xF0;
            var packet = new byte[BlockLength + 4];
            packet[0] = 0x57;
            packet[1] = (byte)(address >> 8);
            packet[2] = (byte)address;
            packet[3] = BlockLength;
            Array.Copy(image, imageOffset, packet, 4, BlockLength);
            session.Port.Write(packet, 0, packet.Length);
            ExpectAck(session.Port, $"write block 0x{address:X4}");
            progress?.Report(++blockNumber * 100 / blockCount);
        }

        var end = "ENDW"u8.ToArray();
        session.Port.Write(end, 0, end.Length);
        session.Completed = true;
    }

    public static IReadOnlyList<string> Ports() => SerialPort.GetPortNames().OrderBy(name => name).ToList();

    private static CloneSession Open(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Select a COM port first.");
        var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 2_000,
            WriteTimeout = 2_000,
            DtrEnable = false,
            RtsEnable = false
        };
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        try
        {
            port.Write(Handshake, 0, Handshake.Length);
            ExpectAck(port, "programming handshake");
            port.Write([0x4D, 0x02], 0, 2);
            var ident = ReadIdent(port);
            if (ident.Length is < 15 or > 16)
                throw new IOException($"Unexpected radio identifier length {ident.Length}.");
            port.ReadTimeout = 2_000;
            port.Write(Ack, 0, 1);
            ExpectAck(port, "model confirmation");
            return new CloneSession(port, ident);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    private static byte[] ReadIdent(SerialPort port)
    {
        var bytes = new List<byte>(16);
        port.ReadTimeout = 2_000;
        bytes.Add((byte)port.ReadByte());
        port.ReadTimeout = 150;
        while (bytes.Count < 16)
        {
            try { bytes.Add((byte)port.ReadByte()); }
            catch (TimeoutException) { break; }
        }
        return bytes.ToArray();
    }

    private static byte[] ReadExact(SerialPort port, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = port.Read(buffer, offset, count - offset);
            if (read <= 0) throw new EndOfStreamException($"Short serial read at {offset}/{count} bytes.");
            offset += read;
        }
        return buffer;
    }

    private static void ExpectAck(SerialPort port, string operation)
    {
        var value = port.ReadByte();
        if (value != 0x41) throw new IOException($"Radio rejected {operation}: 0x{value:X2}.");
    }

    private sealed class CloneSession(SerialPort port, byte[] ident) : IDisposable
    {
        public SerialPort Port { get; } = port;
        public byte[] Ident { get; } = ident;
        public bool Completed { get; set; }

        public void Dispose()
        {
            if (Port.IsOpen && !Completed)
            {
                try
                {
                    var end = "ENDR"u8.ToArray();
                    Port.Write(end, 0, end.Length);
                }
                catch { }
            }
            Port.Dispose();
        }
    }
}
