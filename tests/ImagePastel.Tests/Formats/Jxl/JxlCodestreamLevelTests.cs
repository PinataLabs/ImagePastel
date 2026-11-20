// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// <see cref="JxlCodestreamLevel.Verify"/> at each limit and one past it (#338). It reads only the metadata
/// and the profile's length, so the limits that <c>SaveAsJxl</c> cannot reach (it would encode 2^28 pixels,
/// or the image allocator refuses 2^40) are tested here without pixels.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlCodestreamLevelTests
{
    private const ulong Side18 = 1UL << 18;
    private const ulong Side30 = 1UL << 30;

    // Stands for "throws ImageFormatException", where Level5 and Level10 are the levels themselves.
    private const int Throws = 0;

    // 2^28 + 1 and 2^40 + 1 have no factor pair within the side limits. 1026 x 261633 = 2^28 + 2 and
    // 2731 x 402604038 = 2^40 + 2 are the closest reachable areas past each pixel limit.
    [Theory]
    [InlineData(1UL, 1UL, JxlCodestreamLevel.Level5)]
    [InlineData(Side18, 1UL, JxlCodestreamLevel.Level5)]
    [InlineData(Side18 + 1, 1UL, JxlCodestreamLevel.Level10)]
    [InlineData(1UL, Side18, JxlCodestreamLevel.Level5)]
    [InlineData(1UL, Side18 + 1, JxlCodestreamLevel.Level10)]
    [InlineData(Side18, 1UL << 10, JxlCodestreamLevel.Level5)]
    [InlineData(1UL << 10, Side18, JxlCodestreamLevel.Level5)]
    [InlineData(1UL << 14, 1UL << 14, JxlCodestreamLevel.Level5)]
    [InlineData((1UL << 14) + 1, 1UL << 14, JxlCodestreamLevel.Level10)]
    [InlineData(1UL << 14, (1UL << 14) + 1, JxlCodestreamLevel.Level10)]
    [InlineData(Side18, (1UL << 10) + 1, JxlCodestreamLevel.Level10)]
    [InlineData(Side18, Side18, JxlCodestreamLevel.Level10)]
    [InlineData(1026UL, 261633UL, JxlCodestreamLevel.Level10)]
    [InlineData(Side30, 1UL, JxlCodestreamLevel.Level10)]
    [InlineData(1UL, Side30, JxlCodestreamLevel.Level10)]
    [InlineData(Side30 + 1, 1UL, Throws)]
    [InlineData(1UL, Side30 + 1, Throws)]
    [InlineData(1UL << 20, 1UL << 20, JxlCodestreamLevel.Level10)]
    [InlineData(Side30, 1UL << 10, JxlCodestreamLevel.Level10)]
    [InlineData((1UL << 20) + 1, 1UL << 20, Throws)]
    [InlineData(1UL << 20, (1UL << 20) + 1, Throws)]
    [InlineData(Side30, (1UL << 10) + 1, Throws)]
    [InlineData(2731UL, 402604038UL, Throws)]
    public void Verify_Dimensions(ulong width, ulong height, int expected)
        => AssertVerify(CreateMetadata(width, height), null, expected);

    // The profile counts only when the colour encoding wants it. JxlIccWriter refuses a profile over 2^22
    // bytes, so through SaveAsJxl these sizes are unreachable.
    [Theory]
    [InlineData(0, true, JxlCodestreamLevel.Level5)]
    [InlineData(1 << 22, true, JxlCodestreamLevel.Level5)]
    [InlineData((1 << 22) + 1, true, JxlCodestreamLevel.Level10)]
    [InlineData(1 << 28, true, JxlCodestreamLevel.Level10)]
    [InlineData((1 << 28) + 1, true, Throws)]
    [InlineData((1 << 22) + 1, false, JxlCodestreamLevel.Level5)]
    public void Verify_IccSize(int length, bool wantIcc, int expected)
    {
        JxlCodecMetadata metadata = CreateMetadata(16, 16);
        metadata.M.ColorEncoding.WantIcc = wantIcc;

        // Only the length is read, so skip zeroing a profile of up to 256 MiB.
        byte[] icc = GC.AllocateUninitializedArray<byte>(length);

        AssertVerify(metadata, icc, expected);
    }

    [Fact]
    public void Verify_WantedIccThatIsNull_CountsAsEmpty()
    {
        JxlCodecMetadata metadata = CreateMetadata(16, 16);
        metadata.M.ColorEncoding.WantIcc = true;

        AssertVerify(metadata, null, JxlCodestreamLevel.Level5);
    }

    [Theory]
    [InlineData(0, JxlCodestreamLevel.Level5)]
    [InlineData(4, JxlCodestreamLevel.Level5)]
    [InlineData(5, JxlCodestreamLevel.Level10)]
    [InlineData(256, JxlCodestreamLevel.Level10)]
    [InlineData(257, Throws)]
    public void Verify_ExtraChannels(int count, int expected)
    {
        JxlCodecMetadata metadata = CreateMetadata(16, 16);
        for (int i = 0; i < count; i++)
        {
            metadata.M.ExtraChannelInfo.Add(new JxlExtraChannelInfo { Type = JxlExtraChannelType.Alpha });
        }

        AssertVerify(metadata, null, expected);
    }

    [Theory]
    [InlineData(JxlExtraChannelType.Black, JxlCodestreamLevel.Level10)]
    [InlineData(JxlExtraChannelType.Alpha, JxlCodestreamLevel.Level5)]
    [InlineData(JxlExtraChannelType.Depth, JxlCodestreamLevel.Level5)]
    public void Verify_ExtraChannelType(JxlExtraChannelType type, int expected)
    {
        JxlCodecMetadata metadata = CreateMetadata(16, 16);
        metadata.M.ExtraChannelInfo.Add(new JxlExtraChannelInfo { Type = JxlExtraChannelType.Alpha });
        metadata.M.ExtraChannelInfo.Add(new JxlExtraChannelInfo { Type = type });

        AssertVerify(metadata, null, expected);
    }

    [Theory]
    [InlineData(true, JxlCodestreamLevel.Level5)]
    [InlineData(false, JxlCodestreamLevel.Level10)]
    public void Verify_Modular16BitBuffer(bool sufficient, int expected)
    {
        JxlCodecMetadata metadata = CreateMetadata(16, 16);
        metadata.M.Modular16BitBufferSufficient = sufficient;

        AssertVerify(metadata, null, expected);
    }

    private static JxlCodecMetadata CreateMetadata(ulong width, ulong height)
    {
        JxlCodecMetadata metadata = new();
        metadata.Size.Set(width, height);

        // The size header picks a compact form, so check it kept the size the row asked for.
        Assert.Equal(width, metadata.XSize);
        Assert.Equal(height, metadata.YSize);
        metadata.M.Modular16BitBufferSufficient = true;
        return metadata;
    }

    private static void AssertVerify(JxlCodecMetadata metadata, byte[] icc, int expected)
    {
        if (expected == Throws)
        {
            Assert.Throws<ImageFormatException>(() => JxlCodestreamLevel.Verify(metadata, icc));
        }
        else
        {
            Assert.Equal(expected, JxlCodestreamLevel.Verify(metadata, icc));
        }
    }
}
