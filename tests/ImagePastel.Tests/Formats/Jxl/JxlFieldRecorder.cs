// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Globalization;
using SixLabors.ImageSharp.Formats.Jxl.Fields;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Lists every serialized field of a bundle, in visit order, so two bundles can be compared field by
/// field. It visits all fields, even of a bundle that is all default, and changes none.
/// </summary>
internal sealed class JxlFieldRecorder : JxlVisitor
{
    private readonly List<string> fields = new();

    /// <summary>Returns the fields of <paramref name="bundle"/>.</summary>
    public static List<string> Record(JxlFields bundle)
    {
        JxlFieldRecorder recorder = new();
        recorder.Visit(bundle);
        return recorder.fields;
    }

    public override uint Bits(int bits, uint defaultValue, uint value)
    {
        this.fields.Add($"Bits{bits}={value}");
        return value;
    }

    public override uint U32(JxlU32Enc enc, uint defaultValue, uint value)
    {
        this.fields.Add($"U32={value}");
        return value;
    }

    public override ulong U64(ulong defaultValue, ulong value)
    {
        this.fields.Add($"U64={value}");
        return value;
    }

    public override float F16(float defaultValue, float value)
    {
        this.fields.Add("F16=" + value.ToString("R", CultureInfo.InvariantCulture));
        return value;
    }

    public override bool AllDefault(JxlFields fields) => false;
}
