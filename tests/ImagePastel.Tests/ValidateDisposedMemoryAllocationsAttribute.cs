// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Reflection;
using Xunit.v3;

namespace PinataLabs.ImagePastel.Tests;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ValidateDisposedMemoryAllocationsAttribute : BeforeAfterTestAttribute
{
    private readonly int expected = 0;

    public ValidateDisposedMemoryAllocationsAttribute()
        : this(0)
    {
    }

    public ValidateDisposedMemoryAllocationsAttribute(int expected)
        => this.expected = expected;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
        => MemoryAllocatorValidator.MonitorAllocations();

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        MemoryAllocatorValidator.ValidateAllocations(this.expected);
        MemoryAllocatorValidator.StopMonitoringAllocations();
    }
}
