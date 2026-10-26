// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;

namespace PinataLabs.ImagePastel.Tests.TestUtilities;

/// <summary>
/// Runs <see cref="RemoteExecutor"/> child processes with their standard output and error captured.
/// </summary>
/// <remarks>
/// An xUnit v3 test process reports results to the runner as JSON lines on its standard output.
/// <see cref="RemoteExecutor"/> does not redirect the child streams, so a child that writes to the
/// console would corrupt those messages and test results would get lost. The captured output is
/// added to the <see cref="RemoteExecutionException"/> when the child fails.
/// </remarks>
internal static class RemoteExecutorRunner
{
    public static RemoteInvocation Invoke(Action method, RemoteInvokeOptions options = null)
    {
        options = Prepare(options);
        return new RemoteInvocation(RemoteExecutor.Invoke(method, options));
    }

    public static RemoteInvocation Invoke(Action<string> method, string arg, RemoteInvokeOptions options = null)
    {
        options = Prepare(options);
        return new RemoteInvocation(RemoteExecutor.Invoke(method, arg, options));
    }

    public static RemoteInvocation Invoke(Action<string, string> method, string arg1, string arg2, RemoteInvokeOptions options = null)
    {
        options = Prepare(options);
        return new RemoteInvocation(RemoteExecutor.Invoke(method, arg1, arg2, options));
    }

    public static RemoteInvocation Invoke(Action<string, string, string> method, string arg1, string arg2, string arg3, RemoteInvokeOptions options = null)
    {
        options = Prepare(options);
        return new RemoteInvocation(RemoteExecutor.Invoke(method, arg1, arg2, arg3, options));
    }

    private static RemoteInvokeOptions Prepare(RemoteInvokeOptions options)
    {
        options ??= new RemoteInvokeOptions();

        // These overwrite any caller-supplied Start and stream redirection settings.
        // RemoteInvocation starts the process once the output handlers are attached.
        options.Start = false;
        options.StartInfo.RedirectStandardOutput = true;
        options.StartInfo.RedirectStandardError = true;
        return options;
    }

    /// <summary>
    /// A started remote invocation. <see cref="Dispose"/> waits for the child and throws
    /// <see cref="RemoteExecutionException"/> if it failed.
    /// </summary>
    public sealed class RemoteInvocation : IDisposable
    {
        // How long to keep reading the redirected streams once the child has exited.
        // A grandchild that inherited the pipes could otherwise keep them open indefinitely.
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

        private readonly RemoteInvokeHandle handle;
        private readonly Process process;
        private readonly StringBuilder standardOutput = new();
        private readonly StringBuilder standardError = new();
        private readonly TaskCompletionSource standardOutputClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource standardErrorClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal RemoteInvocation(RemoteInvokeHandle handle)
        {
            this.handle = handle;
            this.process = handle.Process;

            try
            {
                this.process.OutputDataReceived += (_, e) => Append(this.standardOutput, this.standardOutputClosed, e.Data);
                this.process.ErrorDataReceived += (_, e) => Append(this.standardError, this.standardErrorClosed, e.Data);
                this.process.Start();
                this.process.BeginOutputReadLine();
                this.process.BeginErrorReadLine();
            }
            catch
            {
                try
                {
                    this.process.Kill();
                }
                catch
                {
                }

                try
                {
                    // Removes the exception file and temporary runtime configuration.
                    handle.Dispose();
                }
                catch
                {
                }

                this.process.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            // The handle disposes the process, which stops the stream readers, so the output has
            // to be drained before the handle runs. Wait for the first half of the timeout here.
            // If the child is still running, the handle waits for the rest of it (in its usual two
            // halves, with its hang diagnostics in between), so the total wait stays at TimeOut.
            RemoteInvokeOptions options = this.handle.Options;
            int firstHalf = options.TimeOut == Timeout.Infinite ? Timeout.Infinite : options.TimeOut / 2;
            if (this.process.WaitForExit(firstHalf))
            {
                this.DrainStreams();
            }
            else
            {
                options.TimeOut /= 2;
            }

            try
            {
                this.handle.Dispose();
            }
            catch (RemoteExecutionException ex)
            {
                throw new RemoteExecutionException(ex.Message + this.FormatOutput(), ex.StackTrace);
            }
        }

        private static void Append(StringBuilder builder, TaskCompletionSource closed, string line)
        {
            if (line is null)
            {
                closed.TrySetResult();
                return;
            }

            lock (builder)
            {
                builder.AppendLine(line);
            }
        }

        private void DrainStreams()
            => Task.WaitAll(new[] { this.standardOutputClosed.Task, this.standardErrorClosed.Task }, DrainTimeout);

        private string FormatOutput()
        {
            var message = new StringBuilder();
            lock (this.standardOutput)
            {
                if (this.standardOutput.Length > 0)
                {
                    message.AppendLine().Append("STDOUT:").AppendLine().Append(this.standardOutput);
                }
            }

            lock (this.standardError)
            {
                if (this.standardError.Length > 0)
                {
                    message.AppendLine().Append("STDERR:").AppendLine().Append(this.standardError);
                }
            }

            return message.ToString();
        }
    }
}
