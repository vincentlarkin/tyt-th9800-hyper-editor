using System.Text;
using TH9800.HyperEditor.Models;
using TH9800.HyperEditor.Services;
using Xunit;

namespace TH9800.HyperEditor.Tests;

public sealed class HyperImageTests
{
    [Fact]
    public void ReadsAllSixHyperBlocksAtExpectedOffsets()
    {
        var bytes = SyntheticImage();
        for (var index = 0; index < 6; index++)
        {
            var offset = HyperImage.HyperBaseOffset + index * HyperImage.HyperBlockLength;
            SetWord(bytes, offset, 101 + index);
            SetWord(bytes, offset + 4, 201 + index);
            bytes[offset + HyperImage.BeepFlagsOffset] = (byte)(0x12 | (index % 2 == 0 ? 0x80 : 0));
            bytes[offset + HyperImage.BacklightOffset] = (byte)(index + 1);
            bytes[offset + HyperImage.P1Offset] = 11;
            bytes[offset + HyperImage.P2Offset] = 12;
            bytes[offset + HyperImage.P3Offset] = (byte)(12 - index);
            bytes[offset + HyperImage.P4Offset] = 6;
        }

        var profiles = HyperImage.FromBytes(bytes).ReadProfiles();

        Assert.Equal(6, profiles.Count);
        Assert.Collection(profiles,
            profile => AssertProfile(profile, 'A', 101, 201, true, 1, 11, 12, 12, 6),
            profile => AssertProfile(profile, 'B', 102, 202, false, 2, 11, 12, 11, 6),
            profile => AssertProfile(profile, 'C', 103, 203, true, 3, 11, 12, 10, 6),
            profile => AssertProfile(profile, 'D', 104, 204, false, 4, 11, 12, 9, 6),
            profile => AssertProfile(profile, 'E', 105, 205, true, 5, 11, 12, 8, 6),
            profile => AssertProfile(profile, 'F', 106, 206, false, 6, 11, 12, 7, 6));
    }

    [Fact]
    public void ApplyingProfilePreservesUnrelatedBytesAndLowerBeepFlags()
    {
        var original = SyntheticImage(0x5A);
        var offset = HyperImage.HyperBaseOffset + 2 * HyperImage.HyperBlockLength;
        original[offset + HyperImage.BeepFlagsOffset] = 0xD5;
        var image = HyperImage.FromBytes(original);

        var changed = image.ApplyProfiles([new HyperProfile
        {
            Slot = 'C', ImageOffset = offset, LeftChannel = 265, RightChannel = 187,
            Beep = false, Backlight = 3, P1 = 1, P2 = 2, P3 = 3, P4 = 4
        }]);

        Assert.Equal(265, ReadWord(changed, offset));
        Assert.Equal(187, ReadWord(changed, offset + 4));
        Assert.Equal(0x07, changed[offset + 2]);
        Assert.Equal(0xFF, changed[offset + 3]);
        Assert.Equal(0x55, changed[offset + HyperImage.BeepFlagsOffset]);
        Assert.Equal(3, changed[offset + HyperImage.BacklightOffset]);
        Assert.Equal(1, changed[offset + HyperImage.P1Offset]);
        Assert.Equal(2, changed[offset + HyperImage.P2Offset]);
        Assert.Equal(3, changed[offset + HyperImage.P3Offset]);
        Assert.Equal(4, changed[offset + HyperImage.P4Offset]);

        var allowed = new HashSet<int>
        {
            offset, offset + 1, offset + 2, offset + 3,
            offset + 4, offset + 5, offset + 6, offset + 7,
            offset + HyperImage.BacklightOffset,
            offset + HyperImage.BeepFlagsOffset,
            offset + HyperImage.P1Offset,
            offset + HyperImage.P2Offset,
            offset + HyperImage.P3Offset,
            offset + HyperImage.P4Offset
        };
        Assert.All(HyperImage.DifferentOffsets(original, changed), item => Assert.Contains(item, allowed));
    }

    [Fact]
    public void EveryDocumentedMicrophoneFunctionValueIsAccepted()
    {
        for (var value = 0; value <= 12; value++)
        {
            var image = HyperImage.FromBytes(SyntheticImage());
            var changed = image.ApplyProfiles([new HyperProfile
            {
                Slot = 'A', ImageOffset = HyperImage.HyperBaseOffset,
                LeftChannel = 1, RightChannel = 1, Beep = false, Backlight = 4,
                P1 = value, P2 = value, P3 = value, P4 = value
            }]);
            Assert.Equal(value, changed[HyperImage.HyperBaseOffset + HyperImage.P1Offset]);
            Assert.Equal(value, changed[HyperImage.HyperBaseOffset + HyperImage.P4Offset]);
        }
    }

    [Fact]
    public void InvalidImagesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => HyperImage.FromBytes(new byte[10]));

        var wrongModel = new byte[HyperImage.ExpectedLength];
        Encoding.ASCII.GetBytes("NOT_980").CopyTo(wrongModel, 0);
        Assert.Throws<InvalidDataException>(() => HyperImage.FromBytes(wrongModel));
    }

    private static byte[] SyntheticImage(byte fill = 0)
    {
        var bytes = Enumerable.Repeat(fill, HyperImage.ExpectedLength).ToArray();
        Encoding.ASCII.GetBytes("NT_9800").CopyTo(bytes, 0);
        return bytes;
    }

    private static void AssertProfile(HyperProfile profile, char slot, int left, int right,
        bool beep, int backlight, int p1, int p2, int p3, int p4)
    {
        Assert.Equal(slot, profile.Slot);
        Assert.Equal(left, profile.LeftChannel);
        Assert.Equal(right, profile.RightChannel);
        Assert.Equal(beep, profile.Beep);
        Assert.Equal(backlight, profile.Backlight);
        Assert.Equal(p1, profile.P1);
        Assert.Equal(p2, profile.P2);
        Assert.Equal(p3, profile.P3);
        Assert.Equal(p4, profile.P4);
    }

    private static int ReadWord(byte[] bytes, int offset) => bytes[offset] | bytes[offset + 1] << 8;

    private static void SetWord(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}
