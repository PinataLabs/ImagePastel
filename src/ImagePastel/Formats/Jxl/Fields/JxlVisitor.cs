// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/field_encodings.h (Fields, EnumValid) and lib/jxl/fields.h (Visitor, VisitorBase).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System;
using System.Runtime.CompilerServices;
using static PinataLabs.ImagePastel.Formats.Jxl.Fields.JxlU32Distr;

namespace PinataLabs.ImagePastel.Formats.Jxl.Fields;

/// <summary>
/// Visits the fields of a bundle. libjxl passes field pointers; this port passes each field's
/// current value in and assigns the result, so fields can be properties.
/// <para>
/// libjxl's visit methods return a <c>Status</c>. Here errors throw, and only <see cref="Conditional"/>
/// and <see cref="AllDefault"/> return a value that steers the visit.
/// </para>
/// </summary>
internal abstract class JxlVisitor
{
    private const int MaxExtensions = 64;

    // libjxl also tracks each level's extension state, but only to assert on it in debug builds.
    private int depth;

    /// <summary>Gets a value indicating whether the visitor reads from a bitstream, which allows dynamically sized fields.</summary>
    public virtual bool IsReading => false;

    /// <summary>Visits <paramref name="fields"/>. This is the only caller of <see cref="JxlFields.VisitFields"/>.</summary>
    /// <param name="fields">The bundle.</param>
    public void Visit(JxlFields fields)
    {
        if (this.depth >= MaxExtensions)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("Fields nested too deeply");
        }

        this.depth++;
        fields.VisitFields(this);
        this.depth--;
    }

    /// <summary>Visits a field of <paramref name="bits"/> bits.</summary>
    /// <param name="bits">The number of bits, 1 to 32.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public abstract uint Bits(int bits, uint defaultValue, uint value);

    /// <summary>Visits a 32-bit field coded with <paramref name="enc"/>.</summary>
    /// <param name="enc">The encoding.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public abstract uint U32(JxlU32Enc enc, uint defaultValue, uint value);

    /// <summary>Visits a 32-bit field coded with four distributions.</summary>
    /// <param name="d0">Distribution for selector 0.</param>
    /// <param name="d1">Distribution for selector 1.</param>
    /// <param name="d2">Distribution for selector 2.</param>
    /// <param name="d3">Distribution for selector 3.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public uint U32(JxlU32Distr d0, JxlU32Distr d1, JxlU32Distr d2, JxlU32Distr d3, uint defaultValue, uint value)
        => this.U32(new JxlU32Enc(d0, d1, d2, d3), defaultValue, value);

    /// <summary>Visits a 64-bit field.</summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public abstract ulong U64(ulong defaultValue, ulong value);

    /// <summary>Visits a half-precision float field.</summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public abstract float F16(float defaultValue, float value);

    /// <summary>Visits a 1-bit field.</summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public virtual bool Bool(bool defaultValue, bool value)
        => this.Bits(1, defaultValue ? 1u : 0u, value ? 1u : 0u) == 1;

    /// <summary>
    /// Visits an enum field: 00 is 0, 01 is 1, 10xxxx is 2 to 17, 11yyyyyy is 18 to 81.
    /// Only values the enum defines are valid; the JXL enums define exactly libjxl's <c>EnumBits</c>.
    /// </summary>
    /// <typeparam name="TEnum">An enum with a 32-bit unsigned underlying type.</typeparam>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="value">The current value.</param>
    /// <returns>The new value.</returns>
    public TEnum Enum<TEnum>(TEnum defaultValue, TEnum value)
        where TEnum : unmanaged, Enum
    {
        uint u32 = this.U32(Val(0), Val(1), BitsOffset(4, 2), BitsOffset(6, 18), Unsafe.As<TEnum, uint>(ref defaultValue), Unsafe.As<TEnum, uint>(ref value));
        TEnum result = Unsafe.As<uint, TEnum>(ref u32);
        if (u32 >= 64 || !System.Enum.IsDefined(typeof(TEnum), result))
        {
            JxlThrowHelper.ThrowInvalidImageContentException($"Invalid value {u32} for {typeof(TEnum).Name}");
        }

        return result;
    }

    /// <summary>
    /// Returns whether to visit fields that depend on <paramref name="condition"/>. The default
    /// visitors visit them all, so every field gets its default.
    /// </summary>
    /// <param name="condition">The condition, typically from earlier fields.</param>
    /// <returns>Whether to visit the conditional fields.</returns>
    public virtual bool Conditional(bool condition) => condition;

    /// <summary>
    /// Visits a bundle's <c>all_default</c> flag. When it returns <see langword="true"/>, the bundle
    /// calls <see cref="SetDefault"/> and skips its other fields.
    /// </summary>
    /// <param name="fields">The bundle.</param>
    /// <returns>Whether to skip the remaining fields.</returns>
    public virtual bool AllDefault(JxlFields fields) => this.Bool(true, true);

    /// <summary>Sets every field of <paramref name="fields"/> to its default. Only the read visitor does this.</summary>
    /// <param name="fields">The bundle.</param>
    public virtual void SetDefault(JxlFields fields)
    {
    }

    /// <summary>Visits a bundle nested in the current one.</summary>
    /// <param name="fields">The nested bundle.</param>
    public virtual void VisitNested(JxlFields fields) => this.Visit(fields);

    /// <summary>
    /// Visits the extension bit field. Call it before any field that depends on extensions.
    /// </summary>
    /// <param name="extensions">The current extension bits.</param>
    /// <returns>The new extension bits.</returns>
    public virtual ulong BeginExtensions(ulong extensions) => this.U64(0, extensions);

    /// <summary>Ends the extension fields, which are always the last fields of a bundle.</summary>
    public virtual void EndExtensions()
    {
    }
}
