// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The round trips of <see cref="JxlEncoderRoundTrip"/>, through each reader. Each effort has a class of
/// its own, because xUnit runs the tests of one class one after another and the classes in parallel;
/// in one class the higher efforts set the length of the whole test run.
/// </summary>
/// <remarks>
/// xUnit reads <see cref="Cases"/> and <see cref="CorpusCases"/> from the class it runs, so each
/// subclass hides them with the cases at its effort.
/// </remarks>
[Trait("Format", "Jxl")]
public abstract class JxlEncoderRoundTripTests
{
    /// <summary>Gets the pattern cases. Each subclass hides this with its effort's cases.</summary>
    public static TheoryData<string, string, string, int, int> Cases => new();

    /// <summary>Gets the corpus cases. Each subclass hides this with its effort's cases.</summary>
    public static TheoryData<string, string, string> CorpusCases => new();

    [Theory]
    [MemberData(nameof(Cases))]
    public Task RoundTrip_OurDecoder_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Ours, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task RoundTrip_Magick_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Magick, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task RoundTrip_Djxl_IsExact(string effort, string pixelType, string pattern, int width, int height)
        => JxlEncoderRoundTrip.AssertRoundTripAsync(JxlEncoderRoundTrip.Reader.Djxl, effort, pixelType, pattern, width, height);

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public Task RoundTripCorpus_OurDecoder_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Ours, effort, file, pixelType);

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public Task RoundTripCorpus_Magick_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Magick, effort, file, pixelType);

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public Task RoundTripCorpus_Djxl_IsExact(string effort, string file, string pixelType)
        => JxlEncoderRoundTrip.AssertCorpusRoundTripAsync(JxlEncoderRoundTrip.Reader.Djxl, effort, file, pixelType);
}

/// <summary>The round trips at <see cref="JxlEffort.Fastest"/>.</summary>
public sealed class JxlEncoderRoundTripFastestTests : JxlEncoderRoundTripTests
{
    /// <summary>Gets the pattern cases at Fastest.</summary>
    public static new TheoryData<string, string, string, int, int> Cases { get; } = JxlEncoderRoundTrip.CasesAt(JxlEffort.Fastest);

    /// <summary>Gets the corpus cases at Fastest.</summary>
    public static new TheoryData<string, string, string> CorpusCases { get; } = JxlEncoderRoundTrip.CorpusCasesAt(JxlEffort.Fastest);
}

/// <summary>The round trips at <see cref="JxlEffort.Default"/>.</summary>
public sealed class JxlEncoderRoundTripDefaultTests : JxlEncoderRoundTripTests
{
    /// <summary>Gets the pattern cases at Default.</summary>
    public static new TheoryData<string, string, string, int, int> Cases { get; } = JxlEncoderRoundTrip.CasesAt(JxlEffort.Default);

    /// <summary>Gets the corpus cases at Default.</summary>
    public static new TheoryData<string, string, string> CorpusCases { get; } = JxlEncoderRoundTrip.CorpusCasesAt(JxlEffort.Default);
}

/// <summary>The round trips at <see cref="JxlEffort.Thorough"/>.</summary>
public sealed class JxlEncoderRoundTripThoroughTests : JxlEncoderRoundTripTests
{
    /// <summary>Gets the pattern cases at Thorough.</summary>
    public static new TheoryData<string, string, string, int, int> Cases { get; } = JxlEncoderRoundTrip.CasesAt(JxlEffort.Thorough);

    /// <summary>Gets the corpus cases at Thorough.</summary>
    public static new TheoryData<string, string, string> CorpusCases { get; } = JxlEncoderRoundTrip.CorpusCasesAt(JxlEffort.Thorough);
}
