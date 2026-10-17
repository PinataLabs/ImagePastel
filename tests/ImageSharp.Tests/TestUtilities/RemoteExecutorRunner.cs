// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.DotNet.RemoteExecutor;

namespace SixLabors.ImageSharp.Tests.TestUtilities
{
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
            private readonly RemoteInvokeHandle handle;
            private readonly StringBuilder standardOutput = new();
            private readonly StringBuilder standardError = new();

            internal RemoteInvocation(RemoteInvokeHandle handle)
            {
                this.handle = handle;

                Process process = handle.Process;
                process.OutputDataReceived += (_, e) => Append(this.standardOutput, e.Data);
                process.ErrorDataReceived += (_, e) => Append(this.standardError, e.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            public void Dispose()
            {
                // The handle disposes the process, so drain the redirected streams first.
                // On a timeout the handle reports the hang itself.
                Process process = this.handle.Process;
                if (process != null && process.WaitForExit(this.handle.Options.TimeOut))
                {
                    process.WaitForExit();
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

            private static void Append(StringBuilder builder, string line)
            {
                if (line is null)
                {
                    return;
                }

                lock (builder)
                {
                    builder.AppendLine(line);
                }
            }

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
}
