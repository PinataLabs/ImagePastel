// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace PinataLabs.ImagePastel.Common.Helpers;

/// <summary>
/// Internal utilities intended to be only used in tests.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// This constant is useful to verify the target framework ImageSharp has been built against.
    /// Only intended to be used in tests!
    /// </summary>
    internal const string ImageSharpBuiltAgainst =
#if NETSTANDARD2_1
        "netstandard2.1";
#elif NET11_0_OR_GREATER
        "net11.0";
#else
        "net10.0";
#endif
}
