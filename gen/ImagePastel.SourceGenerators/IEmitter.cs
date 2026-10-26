// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace PinataLabs.ImagePastel.SourceGenerators;

/// <summary>
/// Writes the generated files of one template family.
/// </summary>
internal interface IEmitter
{
    /// <summary>
    /// Gets the files this emitter writes.
    /// </summary>
    IReadOnlyList<GeneratedFile> Files { get; }
}
