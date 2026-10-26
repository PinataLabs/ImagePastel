// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.DotNet.RemoteExecutor;
using Xunit.Sdk;

namespace PinataLabs.ImagePastel.Tests.TestUtilities;

/// <summary>
/// Allows the testing against specific feature sets.
/// </summary>
public static class FeatureTestRunner
{
    private static readonly char[] SplitChars = new[] { ',', ' ' };

    /// <summary>
    /// Allows the deserialization of parameters passed to the feature test.
    /// <remark>
    /// <para>
    /// This is required because <see cref="RemoteExecutor"/> does not allow
    /// marshalling of fields so we cannot pass a wrapped <see cref="Action{T}"/>
    /// allowing automatic deserialization.
    /// </para>
    /// </remark>
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="value">The string value to deserialize.</param>
    /// <returns>The <see cref="T"/> value.</returns>
    public static T DeserializeForXunit<T>(string value)
        where T : IXunitSerializable
        => BasicSerializer.Deserialize<T>(value);

    /// <summary>
    /// Allows the deserialization of types implementing <see cref="IConvertible"/>
    /// passed to the feature test.
    /// </summary>
    /// <param name="value">The string value to deserialize.</param>
    /// <returns>The <typeparamref name="T"/> value.</returns>
    public static T Deserialize<T>(string value)
        where T : IConvertible
        => (T)Convert.ChangeType(value, typeof(T));

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    public static void RunWithHwIntrinsicsFeature(
        Action action,
        HwIntrinsics intrinsics)
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action();
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">
    /// The test action to run.
    /// The parameter passed will be a string representing the currently testing <see cref="HwIntrinsics"/>.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    public static void RunWithHwIntrinsicsFeature(
        Action<string> action,
        HwIntrinsics intrinsics)
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    intrinsic.Key.ToString(),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(intrinsic.Key.ToString());
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    /// <param name="serializable">The value to pass as a parameter to the test action.</param>
    public static void RunWithHwIntrinsicsFeature<T>(
        Action<string> action,
        HwIntrinsics intrinsics,
        T serializable)
        where T : IXunitSerializable
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    BasicSerializer.Serialize(serializable),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(BasicSerializer.Serialize(serializable));
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    /// <param name="serializable">The value to pass as a parameter to the test action.</param>
    public static void RunWithHwIntrinsicsFeature<T>(
        Action<string, string> action,
        HwIntrinsics intrinsics,
        T serializable)
        where T : IXunitSerializable
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    BasicSerializer.Serialize(serializable),
                    intrinsic.Key.ToString(),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(BasicSerializer.Serialize(serializable), intrinsic.Key.ToString());
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    /// <param name="arg1">The value to pass as a parameter to the test action.</param>
    /// <param name="arg2">The second value to pass as a parameter to the test action.</param>
    public static void RunWithHwIntrinsicsFeature<T, T2>(
        Action<string, string> action,
        HwIntrinsics intrinsics,
        T arg1,
        T2 arg2)
        where T : IXunitSerializable
        where T2 : IXunitSerializable
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    BasicSerializer.Serialize(arg1),
                    BasicSerializer.Serialize(arg2),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(BasicSerializer.Serialize(arg1), BasicSerializer.Serialize(arg2));
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="serializable">The value to pass as a parameter to the test action.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    public static void RunWithHwIntrinsicsFeature<T>(
        Action<string> action,
        T serializable,
        HwIntrinsics intrinsics)
        where T : IConvertible
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    serializable.ToString(),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(serializable.ToString());
            }
        }
    }

    /// <summary>
    /// Runs the given test <paramref name="action"/> within an environment
    /// where the given <paramref name="intrinsics"/> features.
    /// </summary>
    /// <param name="action">The test action to run.</param>
    /// <param name="arg0">The value to pass as a parameter #0 to the test action.</param>
    /// <param name="arg1">The value to pass as a parameter #1 to the test action.</param>
    /// <param name="intrinsics">The intrinsics features.</param>
    public static void RunWithHwIntrinsicsFeature<T>(
        Action<string, string> action,
        T arg0,
        T arg1,
        HwIntrinsics intrinsics)
        where T : IConvertible
    {
        if (!RemoteExecutor.IsSupported)
        {
            return;
        }

        foreach (KeyValuePair<HwIntrinsics, string> intrinsic in intrinsics.ToFeatureKeyValueCollection())
        {
            var processStartInfo = new ProcessStartInfo();
            if (intrinsic.Key != HwIntrinsics.AllowAll)
            {
                processStartInfo.Environment[$"COMPlus_{intrinsic.Value}"] = "0";

                RemoteExecutorRunner.Invoke(
                    action,
                    arg0.ToString(),
                    arg1.ToString(),
                    new RemoteInvokeOptions
                    {
                        StartInfo = processStartInfo
                    })
                    .Dispose();
            }
            else
            {
                // Since we are running using the default architecture there is no
                // point creating the overhead of running the action in a separate process.
                action(arg0.ToString(), arg1.ToString());
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the runtime honors EnableSSE42.
    /// The .NET 11 runtime ignores it, so SSE3 to SSE4.2 and POPCNT stay enabled there.
    /// </summary>
    internal static bool CanDisableSse42 { get; } =
#if NET11_0_OR_GREATER
        false;
#else
        true;
#endif

    internal static Dictionary<HwIntrinsics, string> ToFeatureKeyValueCollection(this HwIntrinsics intrinsics)
    {
        // Loop through and translate the given values into COMPlus equivaluents.
        // Since .NET 8 the runtime only honors grouped ISA knobs, so each value maps to the
        // narrowest knob that still guarantees the requested feature is disabled.
        var features = new Dictionary<HwIntrinsics, string>();
        foreach (string intrinsic in intrinsics.ToString("G").Split(SplitChars, StringSplitOptions.RemoveEmptyEntries))
        {
            var key = (HwIntrinsics)Enum.Parse(typeof(HwIntrinsics), intrinsic);
            switch (intrinsic)
            {
                case nameof(HwIntrinsics.DisableSIMD):
                case nameof(HwIntrinsics.DisableHWIntrinsic):
                case nameof(HwIntrinsics.DisableSSE):
                case nameof(HwIntrinsics.DisableSSE2):
                    features.Add(key, "EnableHWIntrinsic");
                    break;

                case nameof(HwIntrinsics.DisableAES):
                case nameof(HwIntrinsics.DisablePCLMULQDQ):
                    features.Add(key, "EnableAES");
                    break;

                case nameof(HwIntrinsics.DisableSSE3):
                case nameof(HwIntrinsics.DisableSSSE3):
                case nameof(HwIntrinsics.DisableSSE41):
                case nameof(HwIntrinsics.DisableSSE42):
                case nameof(HwIntrinsics.DisablePOPCNT):
                    features.Add(key, "EnableSSE42");
                    break;

                case nameof(HwIntrinsics.DisableAVX):
                    features.Add(key, "EnableAVX");
                    break;

                case nameof(HwIntrinsics.DisableFMA):
                case nameof(HwIntrinsics.DisableAVX2):
                case nameof(HwIntrinsics.DisableBMI1):
                case nameof(HwIntrinsics.DisableBMI2):
                case nameof(HwIntrinsics.DisableLZCNT):
                    features.Add(key, "EnableAVX2");
                    break;

                case nameof(HwIntrinsics.AllowAll):

                    // Not a COMPlus value. We filter in calling method.
                    features.Add(key, nameof(HwIntrinsics.AllowAll));
                    break;
            }
        }

        return features;
    }
}

/// <summary>
/// See <see href="https://github.com/dotnet/runtime/blob/main/src/coreclr/jit/jitconfigvalues.h"/>
/// <remarks>
/// The runtime disables ISAs in groups, so each value may disable more than the named feature.
/// <see cref="DisableSIMD"/>, <see cref="DisableSSE"/> and <see cref="DisableSSE2"/> disable all hardware intrinsics.
/// </remarks>
/// </summary>
[Flags]
#pragma warning disable RCS1135 // Declare enum member with zero value (when enum has FlagsAttribute).
public enum HwIntrinsics
#pragma warning restore RCS1135 // Declare enum member with zero value (when enum has FlagsAttribute).
{
    // Use flags so we can pass multiple values without using params.
    // Don't base on 0 or use inverse for All as that doesn't translate to string values.
    DisableSIMD = 1 << 0,
    DisableHWIntrinsic = 1 << 1,
    DisableSSE = 1 << 2,
    DisableSSE2 = 1 << 3,
    DisableAES = 1 << 4,
    DisablePCLMULQDQ = 1 << 5,
    DisableSSE3 = 1 << 6,
    DisableSSSE3 = 1 << 7,
    DisableSSE41 = 1 << 8,
    DisableSSE42 = 1 << 9,
    DisablePOPCNT = 1 << 10,
    DisableAVX = 1 << 11,
    DisableFMA = 1 << 12,
    DisableAVX2 = 1 << 13,
    DisableBMI1 = 1 << 14,
    DisableBMI2 = 1 << 15,
    DisableLZCNT = 1 << 16,
    AllowAll = 1 << 17
}
