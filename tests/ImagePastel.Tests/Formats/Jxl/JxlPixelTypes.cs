// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using PinataLabs.ImagePastel.PixelFormats;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The one map from a pixel type name in theory data (<c>nameof(L8)</c> and so on) to a generic call.
/// Theory data names the pixel type, so that each test case keeps a readable name.
/// </summary>
internal static class JxlPixelTypes
{
    /// <summary>Runs <paramref name="call"/> for the pixel type named <paramref name="pixelType"/>.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="pixelType">The pixel type name.</param>
    /// <param name="call">The work.</param>
    /// <returns>The result of the work.</returns>
    public static TResult Call<TResult>(string pixelType, IJxlPixelTypeCall<TResult> call) => pixelType switch
    {
        nameof(A8) => call.Invoke<A8>(),
        nameof(Abgr32) => call.Invoke<Abgr32>(),
        nameof(Argb32) => call.Invoke<Argb32>(),
        nameof(Bgr24) => call.Invoke<Bgr24>(),
        nameof(Bgr565) => call.Invoke<Bgr565>(),
        nameof(Bgra32) => call.Invoke<Bgra32>(),
        nameof(Bgra4444) => call.Invoke<Bgra4444>(),
        nameof(Bgra5551) => call.Invoke<Bgra5551>(),
        nameof(Byte4) => call.Invoke<Byte4>(),
        nameof(HalfSingle) => call.Invoke<HalfSingle>(),
        nameof(HalfVector2) => call.Invoke<HalfVector2>(),
        nameof(HalfVector4) => call.Invoke<HalfVector4>(),
        nameof(L16) => call.Invoke<L16>(),
        nameof(L8) => call.Invoke<L8>(),
        nameof(La16) => call.Invoke<La16>(),
        nameof(La32) => call.Invoke<La32>(),
        nameof(NormalizedByte2) => call.Invoke<NormalizedByte2>(),
        nameof(NormalizedByte4) => call.Invoke<NormalizedByte4>(),
        nameof(NormalizedShort2) => call.Invoke<NormalizedShort2>(),
        nameof(NormalizedShort4) => call.Invoke<NormalizedShort4>(),
        nameof(Rg32) => call.Invoke<Rg32>(),
        nameof(Rgb24) => call.Invoke<Rgb24>(),
        nameof(Rgb48) => call.Invoke<Rgb48>(),
        nameof(Rgba1010102) => call.Invoke<Rgba1010102>(),
        nameof(Rgba32) => call.Invoke<Rgba32>(),
        nameof(Rgba64) => call.Invoke<Rgba64>(),
        nameof(RgbaVector) => call.Invoke<RgbaVector>(),
        nameof(Short2) => call.Invoke<Short2>(),
        nameof(Short4) => call.Invoke<Short4>(),
        _ => throw new ArgumentException($"No pixel type named {pixelType}", nameof(pixelType)),
    };

    /// <summary>
    /// Runs <paramref name="check"/>, work that asserts, for the pixel type named <paramref name="pixelType"/>.
    /// It is <see cref="Call{TResult}"/> under a name that says a test using it asserts.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="pixelType">The pixel type name.</param>
    /// <param name="check">The work, which asserts.</param>
    /// <returns>The result of the work.</returns>
    public static TResult Verify<TResult>(string pixelType, IJxlPixelTypeCall<TResult> check) => Call(pixelType, check);
}

/// <summary>
/// A test's work for one pixel type. <see cref="JxlPixelTypes.Call{TResult}"/> runs it for the pixel
/// type that theory data names.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
internal interface IJxlPixelTypeCall<out TResult>
{
    /// <summary>Runs the work for <typeparamref name="TPixel"/>.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <returns>The result.</returns>
    TResult Invoke<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>;
}
