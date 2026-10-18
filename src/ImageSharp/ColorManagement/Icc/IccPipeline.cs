// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.ColorManagement.Icc;

/// <summary>
/// A list of <see cref="IccStage"/>s applied in order: the source profile's device-to-PCS stages,
/// a PCS conversion when the two profiles use different PCS encodings, then the target profile's
/// PCS-to-device stages.
/// </summary>
internal sealed class IccPipeline
{
    private readonly IccStage[] stages;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccPipeline"/> class. Adjacent matrix stages
    /// are combined into one, in double precision.
    /// </summary>
    /// <param name="stages">The stages, in the order they apply.</param>
    public IccPipeline(IEnumerable<IccStage> stages)
    {
        List<IccStage> combined = [];
        foreach (IccStage stage in stages)
        {
            if (stage is IccMatrixStage next && combined.Count > 0 && combined[^1] is IccMatrixStage previous)
            {
                combined[^1] = new IccMatrixStage(IccMatrix.Multiply(next.Matrix, previous.Matrix));
            }
            else
            {
                combined.Add(stage);
            }
        }

        this.stages = [.. combined];
    }

    /// <summary>
    /// Gets the stages, in the order they apply.
    /// </summary>
    public IReadOnlyList<IccStage> Stages => this.stages;

    /// <summary>
    /// Transforms the values in place.
    /// </summary>
    /// <param name="values">The values.</param>
    public void Invoke(Span<Vector4> values)
    {
        foreach (IccStage stage in this.stages)
        {
            stage.Invoke(values);
        }
    }
}
