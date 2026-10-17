// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

// Redundant suppressing of SA1413 for Rider.
[assembly:
    System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.MaintainabilityRules",
        "SA1413:UseTrailingCommasInMultiLineInitializers",
        Justification = "Follows SixLabors.ruleset")]

// BufferedReadStream.Read fills the requested count unless the stream ends, and the decoders
// handle truncated input deliberately (see BufferedReadStream.EofHitCount).
[assembly:
    System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2022:Avoid inexact read with 'Stream.Read'",
        Justification = "Decoders read through BufferedReadStream, which only returns short at end of stream.",
        Scope = "namespaceanddescendants",
        Target = "~N:SixLabors.ImageSharp.Formats")]
