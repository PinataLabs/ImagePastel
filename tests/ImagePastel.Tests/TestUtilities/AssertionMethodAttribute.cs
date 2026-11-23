// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace PinataLabs.ImagePastel.Tests.TestUtilities;

/// <summary>
/// Marks a method that fails the test when the code it runs fails. Sonar's "Tests should include
/// assertions" rule (S2699) matches this attribute by name. The rule does not follow a test body
/// that is passed as a method group, so without the attribute it flags every test that delegates
/// to <see cref="FeatureTestRunner"/> or <see cref="RemoteExecutorRunner"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AssertionMethodAttribute : Attribute
{
}
