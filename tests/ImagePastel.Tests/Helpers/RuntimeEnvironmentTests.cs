// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;
using Xunit;

#pragma warning disable IDE0022 // Use expression body for methods
namespace PinataLabs.ImagePastel.Tests.Helpers;

public class RuntimeEnvironmentTests
{
    [Fact]
    public void CanDetectNetCore()
    {
        Assert.False(RuntimeEnvironment.IsNetCore);
    }

    [Fact]
    public void CanDetectOSPlatform()
    {
        if (TestEnvironment.IsLinux)
        {
            Assert.True(RuntimeEnvironment.IsOSPlatform(OSPlatform.Linux));
        }
        else if (TestEnvironment.IsMacOS)
        {
            Assert.True(RuntimeEnvironment.IsOSPlatform(OSPlatform.OSX));
        }
        else if (TestEnvironment.IsWindows)
        {
            Assert.True(RuntimeEnvironment.IsOSPlatform(OSPlatform.Windows));
        }
    }
}
