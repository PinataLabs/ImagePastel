// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/modular/modular_image.h, lib/jxl/modular/modular_image.cc and lib/jxl/modular/options.h.
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Collections.Generic;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.Memory;

namespace PinataLabs.ImagePastel.Formats.Jxl.Modular;

/// <summary>
/// A Modular image: a list of channels, the first <see cref="NbMetaChannels"/> of which hold
/// metadata such as palettes, plus the transforms still to undo.
/// </summary>
internal sealed class JxlModularImage : IDisposable
{
    private JxlModularImage(MemoryAllocator allocator, int w, int h, int bitDepth)
    {
        this.Allocator = allocator;
        this.W = w;
        this.H = h;
        this.BitDepth = bitDepth;
    }

    /// <summary>Gets the allocator of the channels.</summary>
    public MemoryAllocator Allocator { get; }

    /// <summary>Gets the channels.</summary>
    public List<JxlModularChannel> Channel { get; } = new();

    /// <summary>Gets or sets the transforms still to undo, in the order they were applied.</summary>
    public List<JxlTransform> Transform { get; set; } = new();

    /// <summary>Gets the width.</summary>
    public int W { get; }

    /// <summary>Gets the height.</summary>
    public int H { get; }

    /// <summary>Gets the bit depth of the samples.</summary>
    public int BitDepth { get; }

    /// <summary>Gets or sets the number of meta channels at the start of <see cref="Channel"/>, such as palettes.</summary>
    public int NbMetaChannels { get; set; }

    /// <summary>Gets or sets a value indicating whether a fatal error occurred.</summary>
    public bool Error { get; set; }

    /// <summary>Gets a value indicating whether no channel has any pixels.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (JxlModularChannel ch in this.Channel)
            {
                if (ch.W != 0 && ch.H != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Creates an image with <paramref name="nbChans"/> full-size channels.</summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <param name="bitDepth">The bit depth.</param>
    /// <param name="nbChans">The number of channels.</param>
    /// <returns>The image.</returns>
    public static JxlModularImage Create(MemoryAllocator allocator, int w, int h, int bitDepth, int nbChans)
    {
        JxlModularImage result = new(allocator, w, h, bitDepth);
        for (int i = 0; i < nbChans; i++)
        {
            JxlModularChannel c = JxlModularChannel.Create(allocator, w, h);
            c.Component = i;
            result.Channel.Add(c);
        }

        return result;
    }

    /// <summary>
    /// Undoes the transforms in reverse order. libjxl sets <see cref="Error"/> when one fails;
    /// here the failing transform throws <see cref="InvalidImageContentException"/>.
    /// </summary>
    /// <param name="wpHeader">The weighted predictor parameters, which delta palettes use.</param>
    /// <param name="cancellationToken">The cancellation token, checked per transform and per squeeze step (#293).</param>
    public void UndoTransforms(JxlWeightedHeader wpHeader, CancellationToken cancellationToken)
    {
        while (this.Transform.Count > 0)
        {
            JxlTransform t = this.Transform[^1];
            t.Inverse(this, wpHeader, cancellationToken);
            this.Transform.RemoveAt(this.Transform.Count - 1);
        }
    }

    public void Dispose()
    {
        foreach (JxlModularChannel c in this.Channel)
        {
            c.Dispose();
        }

        this.Channel.Clear();
    }
}
