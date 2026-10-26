// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl.Conformance;

/// <summary>
/// Runs the libjxl command-line tools (djxl, cjxl, ssimulacra2) from <c>JXL_TOOLS_DIR</c>.
/// A test that needs a tool is skipped when <c>JXL_TOOLS_DIR</c> is unset, and fails when it is set
/// but the tool is missing, so a job that provides the tools cannot pass without them.
/// </summary>
internal static class JxlTools
{
    public const string DirectoryVariable = "JXL_TOOLS_DIR";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static string GetToolPath(string name)
    {
        string directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrEmpty(directory))
        {
            Assert.Skip($"{DirectoryVariable} is not set, so the libjxl tools are not available.");
        }

        string path = Path.Combine(directory, TestEnvironment.IsWindows ? name + ".exe" : name);
        Assert.True(File.Exists(path), $"{DirectoryVariable} is set to '{directory}', but {Path.GetFileName(path)} is not there.");
        return path;
    }

    public static async Task RunAsync(string name, CancellationToken cancellationToken, params string[] arguments)
    {
        (int exitCode, string output) = await RunForExitCodeAsync(name, cancellationToken, arguments);
        Assert.True(exitCode == 0, $"{name} exited with code {exitCode}.{Environment.NewLine}{output}");
    }

    /// <summary>Runs a tool and returns its exit code and output, for tests that expect it to fail.</summary>
    /// <param name="name">The tool.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The exit code and the standard output and error.</returns>
    public static async Task<(int ExitCode, string Output)> RunForExitCodeAsync(string name, CancellationToken cancellationToken, params string[] arguments)
    {
        ProcessStartInfo info = new(GetToolPath(name))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info);
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, $"{await output}{Environment.NewLine}{await error}");
    }
}
