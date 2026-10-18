// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.SourceGenerators.Library;

/// <summary>
/// Writes <c>Guard.Numeric.cs</c>: the range checks of <c>Guard</c> for each primitive numeric type.
/// Ported from the T4 template it replaced, <c>Common/SharedInfrastructure/Guard.Numeric.tt</c>.
/// </summary>
internal sealed class GuardNumericEmitter : IEmitter
{
    private static readonly string[] Types = ["byte", "sbyte", "short", "ushort", "char", "int", "uint", "float", "long", "ulong", "double", "decimal"];

    /// <inheritdoc/>
    public IReadOnlyList<string> HintNames { get; } = ["Guard.Numeric.cs"];

    /// <inheritdoc/>
    public void Emit(string hintName, SourceWriter writer)
    {
        writer.WriteLine("""
            // Copyright (c) Six Labors.
            // Licensed under the Apache License, Version 2.0.

            using System;
            using System.Runtime.CompilerServices;

            namespace SixLabors;

            /// <summary>
            /// Provides methods to protect against invalid parameters.
            /// </summary>
            internal static partial class Guard
            {
            """);

        using (writer.Indent())
        {
            for (int i = 0; i < Types.Length; i++)
            {
                if (i > 0)
                {
                    writer.WriteLine();
                }

                string t = Types[i];
                writer.WriteLine($$"""
                    /// <summary>
                    /// Ensures that the specified value is less than a maximum value.
                    /// </summary>
                    /// <param name="value">The target value, which should be validated.</param>
                    /// <param name="max">The maximum value.</param>
                    /// <param name="parameterName">The name of the parameter that is to be checked.</param>
                    /// <exception cref="ArgumentException">
                    /// <paramref name="value"/> is greater than the maximum value.
                    /// </exception>
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void MustBeLessThan({{t}} value, {{t}} max, string parameterName)
                    {
                        if (value < max)
                        {
                            return;
                        }

                        ThrowHelper.ThrowArgumentOutOfRangeExceptionForMustBeLessThan(value, max, parameterName);
                    }

                    /// <summary>
                    /// Verifies that the specified value is less than or equal to a maximum value
                    /// and throws an exception if it is not.
                    /// </summary>
                    /// <param name="value">The target value, which should be validated.</param>
                    /// <param name="max">The maximum value.</param>
                    /// <param name="parameterName">The name of the parameter that is to be checked.</param>
                    /// <exception cref="ArgumentException">
                    /// <paramref name="value"/> is greater than the maximum value.
                    /// </exception>
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void MustBeLessThanOrEqualTo({{t}} value, {{t}} max, string parameterName)
                    {
                        if (value <= max)
                        {
                            return;
                        }

                        ThrowHelper.ThrowArgumentOutOfRangeExceptionForMustBeLessThanOrEqualTo(value, max, parameterName);
                    }

                    /// <summary>
                    /// Verifies that the specified value is greater than a minimum value
                    /// and throws an exception if it is not.
                    /// </summary>
                    /// <param name="value">The target value, which should be validated.</param>
                    /// <param name="min">The minimum value.</param>
                    /// <param name="parameterName">The name of the parameter that is to be checked.</param>
                    /// <exception cref="ArgumentException">
                    /// <paramref name="value"/> is less than the minimum value.
                    /// </exception>
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void MustBeGreaterThan({{t}} value, {{t}} min, string parameterName)
                    {
                        if (value > min)
                        {
                            return;
                        }

                        ThrowHelper.ThrowArgumentOutOfRangeExceptionForMustBeGreaterThan(value, min, parameterName);
                    }

                    /// <summary>
                    /// Verifies that the specified value is greater than or equal to a minimum value
                    /// and throws an exception if it is not.
                    /// </summary>
                    /// <param name="value">The target value, which should be validated.</param>
                    /// <param name="min">The minimum value.</param>
                    /// <param name="parameterName">The name of the parameter that is to be checked.</param>
                    /// <exception cref="ArgumentException">
                    /// <paramref name="value"/> is less than the minimum value.
                    /// </exception>
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void MustBeGreaterThanOrEqualTo({{t}} value, {{t}} min, string parameterName)
                    {
                        if (value >= min)
                        {
                            return;
                        }

                        ThrowHelper.ThrowArgumentOutOfRangeExceptionForMustBeGreaterThanOrEqualTo(value, min, parameterName);
                    }

                    /// <summary>
                    /// Verifies that the specified value is greater than or equal to a minimum value and less than
                    /// or equal to a maximum value and throws an exception if it is not.
                    /// </summary>
                    /// <param name="value">The target value, which should be validated.</param>
                    /// <param name="min">The minimum value.</param>
                    /// <param name="max">The maximum value.</param>
                    /// <param name="parameterName">The name of the parameter that is to be checked.</param>
                    /// <exception cref="ArgumentException">
                    /// <paramref name="value"/> is less than the minimum value of greater than the maximum value.
                    /// </exception>
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void MustBeBetweenOrEqualTo({{t}} value, {{t}} min, {{t}} max, string parameterName)
                    {
                        if (value >= min && value <= max)
                        {
                            return;
                        }

                        ThrowHelper.ThrowArgumentOutOfRangeExceptionForMustBeBetweenOrEqualTo(value, min, max, parameterName);
                    }
                    """);
            }
        }

        writer.WriteLine("}");
    }
}
