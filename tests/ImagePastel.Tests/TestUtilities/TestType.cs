// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using Xunit.Sdk;

namespace PinataLabs.ImagePastel.Tests.TestUtilities;

public class TestType<T> : IXunitSerializable
{
    public TestType()
    {
    }

    public void Deserialize(IXunitSerializationInfo info)
    {
    }

    // xUnit v3 cannot deserialize an object that wrote no values, and VSTest then drops the
    // test case without reporting it. The type argument carries all the state, so write its name.
    public void Serialize(IXunitSerializationInfo info)
        => info.AddValue("Type", typeof(T).FullName);

    public override string ToString()
    {
        return $"Type<{typeof(T).Name}>";
    }
}
