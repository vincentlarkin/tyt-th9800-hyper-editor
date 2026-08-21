using System.Security.Cryptography;
using System.Text;
using TH9800.HyperEditor.Models;

namespace TH9800.HyperEditor.Services;

public sealed class HyperImage
{
    public const int ExpectedLength = 65_296;
    public const int ActiveOffset = 0xCB10;
    public const int HyperBaseOffset = 0xDA90;
    public const int HyperBlockLength = 0x40;
    public const int BacklightOffset = 0x22;
    public const int BeepFlagsOffset = 0x24;
    public const int P3LongOffset = 0x3A;

    private readonly byte[] _bytes;

    private HyperImage(byte[] bytes) => _bytes = bytes;

    public static HyperImage Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static HyperImage FromBytes(byte[] bytes)
    {
        Validate(bytes);
        return new HyperImage((byte[])bytes.Clone());
    }

    public static void Validate(byte[] bytes)
    {
        if (bytes.Length != ExpectedLength)
            throw new InvalidDataException($"Image is {bytes.Length:N0} bytes; expected {ExpectedLength:N0}.");
        if (Encoding.ASCII.GetString(bytes, 0, 7) != "NT_9800")
            throw new InvalidDataException("The image does not contain the NT_9800 radio identifier.");
    }

    public IReadOnlyList<HyperProfile> ReadProfiles()
    {
        var result = new List<HyperProfile>(6);
        for (var index = 0; index < 6; index++)
        {
            var offset = HyperBaseOffset + index * HyperBlockLength;
            result.Add(ReadProfile(offset, (char)('A' + index)));
        }
        return result;
    }

    public HyperProfile ReadActiveProfile() => ReadProfile(ActiveOffset, '*');

    public byte[] ApplyProfiles(IEnumerable<HyperProfile> profiles)
    {
        var output = (byte[])_bytes.Clone();
        foreach (var profile in profiles)
        {
            if (profile.Slot is < 'A' or > 'F')
                throw new InvalidDataException($"Invalid Hyper slot {profile.Slot}.");
            if (profile.LeftChannel is < 1 or > 800 || profile.RightChannel is < 1 or > 800)
                throw new InvalidDataException($"Hyper {profile.Slot} contains a channel outside 1-800.");
            if (profile.Backlight is < 0 or > 8)
                throw new InvalidDataException($"Hyper {profile.Slot} has an invalid backlight level.");
            if (profile.P3Long is < 0 or > 12)
                throw new InvalidDataException($"Hyper {profile.Slot} has an invalid P3 assignment.");

            var offset = HyperBaseOffset + (profile.Slot - 'A') * HyperBlockLength;
            WriteMemorySelection(output, offset, profile.LeftChannel);
            WriteMemorySelection(output, offset + 4, profile.RightChannel);
            output[offset + BacklightOffset] = (byte)profile.Backlight;
            output[offset + P3LongOffset] = (byte)profile.P3Long;
            output[offset + BeepFlagsOffset] = profile.Beep
                ? (byte)(output[offset + BeepFlagsOffset] | 0x80)
                : (byte)(output[offset + BeepFlagsOffset] & 0x7F);
        }
        return output;
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();
    public string Sha256 => Convert.ToHexString(SHA256.HashData(_bytes));

    public static IReadOnlyList<int> DifferentOffsets(byte[] before, byte[] after)
    {
        if (before.Length != after.Length) throw new ArgumentException("Images must have equal length.");
        var offsets = new List<int>();
        for (var index = 0; index < before.Length; index++)
            if (before[index] != after[index]) offsets.Add(index);
        return offsets;
    }

    private HyperProfile ReadProfile(int offset, char slot) => new()
    {
        Slot = slot,
        ImageOffset = offset,
        LeftChannel = ReadUInt16(offset),
        RightChannel = ReadUInt16(offset + 4),
        Beep = (_bytes[offset + BeepFlagsOffset] & 0x80) != 0,
        Backlight = _bytes[offset + BacklightOffset],
        P3Long = _bytes[offset + P3LongOffset]
    };

    private int ReadUInt16(int offset) => _bytes[offset] | _bytes[offset + 1] << 8;

    private static void WriteMemorySelection(byte[] bytes, int offset, int channel)
    {
        bytes[offset] = (byte)(channel & 0xFF);
        bytes[offset + 1] = (byte)(channel >> 8);
        bytes[offset + 2] = 0x07;
        bytes[offset + 3] = 0xFF;
    }
}
