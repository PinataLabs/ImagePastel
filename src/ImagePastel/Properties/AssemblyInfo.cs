// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

// BufferedReadStream.Read fills the requested count unless the stream ends, and the decoders
// handle truncated input deliberately (see BufferedReadStream.EofHitCount).
[assembly:
    System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2022:Avoid inexact read with 'Stream.Read'",
        Justification = "Decoders read through BufferedReadStream, which only returns short at end of stream.",
        Scope = "namespaceanddescendants",
        Target = "~N:SixLabors.ImageSharp.Formats")]
