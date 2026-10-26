// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/enc_fields.cc (WriteVisitor, Bundle::Write, WriteCodestreamHeaders).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Headers;

namespace PinataLabs.ImagePastel.Formats.Jxl.Fields;

/// <content>The write side of <see cref="JxlBundle"/>.</content>
internal static partial class JxlBundle
{
    /// <summary>
    /// Writes <paramref name="fields"/> to <paramref name="writer"/>. First checks that every value can
    /// be encoded, as libjxl's <c>CanEncode</c> does, and throws <see cref="ImageFormatException"/>
    /// before anything is written if one cannot.
    /// </summary>
    /// <param name="fields">The bundle.</param>
    /// <param name="writer">The writer.</param>
    public static void Write(JxlFields fields, JxlBitWriter writer)
    {
        CanEncodeVisitor check = new();
        try
        {
            check.Visit(fields);
        }
        catch (InvalidImageContentException ex)
        {
            // VisitFields shares its checks with the reader, which throw InvalidImageContentException.
            // On the write side the caller's value is at fault, so report it as one that cannot be encoded.
            JxlThrowHelper.ThrowImageFormatException($"Cannot encode {fields.GetType().Name}: {ex.Message}", ex);
        }

        if (check.Error != null)
        {
            JxlThrowHelper.ThrowImageFormatException($"Cannot encode {fields.GetType().Name}: {check.Error}");
        }

        long start = writer.BitsWritten;
        new WriteVisitor(check.ExtensionBits, writer).Visit(fields);
        DebugGuard.IsTrue(writer.BitsWritten - start == check.TotalBits, "Bundle wrote a different number of bits than CanEncode counted");
    }

    /// <summary>
    /// Writes the codestream signature, the size header, the image metadata and the transform data,
    /// as the decoder reads them before any ICC profile.
    /// </summary>
    /// <param name="metadata">The codestream headers.</param>
    /// <param name="writer">The writer.</param>
    public static void WriteCodestreamHeaders(JxlCodecMetadata metadata, JxlBitWriter writer)
    {
        // Marker/signature
        writer.Write(8, JxlConstants.CodestreamSignature[0]);
        writer.Write(8, JxlConstants.CodestreamSignature[1]);

        Write(metadata.Size, writer);
        Write(metadata.M, writer);

        metadata.TransformData.NonserializedXybEncoded = metadata.M.XybEncoded;
        Write(metadata.TransformData, writer);
    }

    /// <summary>
    /// Writes each field. libjxl's visitor records whether each write succeeded; here
    /// <see cref="Write"/> has already checked every value, so the writes cannot fail.
    /// </summary>
    private sealed class WriteVisitor : JxlVisitor
    {
        private readonly IReadOnlyList<ulong> extensionBits;
        private readonly JxlBitWriter writer;
        private int nextExtension;

        public WriteVisitor(IReadOnlyList<ulong> extensionBits, JxlBitWriter writer)
        {
            this.extensionBits = extensionBits;
            this.writer = writer;
        }

        public override uint Bits(int bits, uint defaultValue, uint value)
        {
            JxlFieldCoders.WriteBits(bits, value, this.writer);
            return value;
        }

        public override uint U32(JxlU32Enc enc, uint defaultValue, uint value)
        {
            JxlFieldCoders.WriteU32(enc, value, this.writer);
            return value;
        }

        public override ulong U64(ulong defaultValue, ulong value)
        {
            JxlFieldCoders.WriteU64(value, this.writer);
            return value;
        }

        public override float F16(float defaultValue, float value)
        {
            JxlFieldCoders.WriteF16(value, this.writer);
            return value;
        }

        // libjxl reads the all_default field that CanEncode stored; see CanEncodeVisitor.AllDefault.
        public override bool AllDefault(JxlFields fields)
        {
            bool allDefault = JxlBundle.AllDefault(fields);
            this.Bool(true, allDefault);
            return allDefault;
        }

        public override ulong BeginExtensions(ulong extensions)
        {
            extensions = base.BeginExtensions(extensions);
            if (extensions == 0)
            {
                return extensions;
            }

            // All bits go to the first extension, as in libjxl. NOTE: the size can be zero if an
            // extension does not require any additional fields.
            JxlFieldCoders.WriteU64(this.extensionBits[this.nextExtension++], this.writer);

            // For each nonzero bit except the lowest/first (already written):
            for (ulong remaining = extensions & (extensions - 1); remaining != 0; remaining &= remaining - 1)
            {
                JxlFieldCoders.WriteU64(0, this.writer);
            }

            return extensions;
        }
    }
}
