﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Statiq.Common;

namespace Statiq.Core
{
    /// <summary>
    /// Starts a system process.
    /// </summary>
    /// <remarks>
    /// This module can start both foreground and background processes. If the process
    /// is a foreground process (the default), the module will wait for it to return
    /// and output a document with the process standard output as it's content.
    /// If the process is a background process, the module will fork it and let it run,
    /// but no output document will be generated and it will log with a debug level.
    /// </remarks>
    /// <category>Extensibility</category>
    public class StartProcess : ParallelMultiConfigModule, IDisposable
    {
        /// <summary>
        /// A metadata key that contains the process exit code.
        /// </summary>
        public const string ExitCode = nameof(ExitCode);

        /// <summary>
        /// A metadata key that contains any error data from the process.
        /// </summary>
        public const string ErrorData = nameof(ErrorData);

        // Config keys
        private const string FileName = nameof(FileName);
        private const string Arguments = nameof(Arguments);
        private const string WorkingDirectory = nameof(WorkingDirectory);

        private readonly ConcurrentDictionary<int, (Process, ILogger)> _processes = new ConcurrentDictionary<int, (Process, ILogger)>();
        private readonly Dictionary<string, string> _environmentVariables = new Dictionary<string, string>();

        private int _timeout;
        private bool _background;
        private bool _onlyOnce;
        private bool _executed;
        private bool _logOutput;
        private bool _continueOnError;
        private bool _keepContent;
        private Func<int, bool> _errorExitCode = x => x != 0;

        /// <summary>
        /// Starts a process for the specified file name and arguments.
        /// </summary>
        /// <param name="fileName">The file name of the process to start.</param>
        public StartProcess(Config<string> fileName)
            : this(fileName, Config.FromValue((string)null))
        {
        }

        /// <summary>
        /// Starts a process for the specified file name and arguments.
        /// </summary>
        /// <param name="fileName">The file name of the process to start.</param>
        /// <param name="arguments">The arguments to pass to the process.</param>
        public StartProcess(Config<string> fileName, Config<string> arguments)
            : base(
                new Dictionary<string, IConfig>
                {
                    { nameof(FileName), fileName ?? throw new ArgumentNullException(nameof(fileName)) },
                    { nameof(Arguments), arguments ?? throw new ArgumentNullException(nameof(arguments)) }
                },
                false)
        {
        }

        /// <summary>
        /// Sets the working directory to use for the process.
        /// </summary>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess WithWorkingDirectory(Config<string> workingDirectory) =>
            (StartProcess)SetConfig(nameof(WorkingDirectory), workingDirectory);

        /// <summary>
        /// Sets process-specific environment variables.
        /// </summary>
        /// <param name="environmentVariables">The environment variables to set.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess WithEnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
        {
            _ = environmentVariables ?? throw new ArgumentNullException(nameof(environmentVariables));
            foreach (KeyValuePair<string, string> environmentVariable in environmentVariables)
            {
                _environmentVariables[environmentVariable.Key] = environmentVariable.Value;
            }
            return this;
        }

        /// <summary>
        /// Sets a process-specific environment variable.
        /// </summary>
        /// <param name="environmentVariable">The name and value of the environment variable to set.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess WithEnvironmentVariable(KeyValuePair<string, string> environmentVariable)
        {
            _environmentVariables[environmentVariable.Key] = environmentVariable.Value;
            return this;
        }

        /// <summary>
        /// Sets a timeout in milliseconds before the process will be terminated.
        /// </summary>
        /// <remarks>
        /// This has no effect for background processes.
        /// </remarks>
        /// <param name="timeout">The timeout in milliseconds.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess WithTimeout(int timeout)
        {
            _timeout = timeout;
            return this;
        }

        /// <summary>
        /// Starts the process and leaves it running in the background.
        /// </summary>
        /// <remarks>
        /// If the process is a background process, the module will fork it and let it run,
        /// but no output document will be generated and it will log with a debug level.
        /// </remarks>
        /// <param name="background"><c>true</c> to start this process in the background, <c>false</c> otherwise.</param>
        /// <param name="onlyOnce">
        /// <c>true</c> to start the process the first time the module is executed and not on re-execution,
        /// <c>false</c> to start a new process on every execution.
        /// </param>
        /// <returns>The current module instance.</returns>
        public StartProcess AsBackground(bool background = true, bool onlyOnce = true)
        {
            _background = background;
            _onlyOnce = onlyOnce;
            return this;
        }

        /// <summary>
        /// Only starts the process on the first module execution.
        /// </summary>
        /// <param name="onlyOnce">
        /// <c>true</c> to start the process the first time the module is executed and not on re-execution,
        /// <c>false</c> to start a new process on every execution.
        /// </param>
        /// <returns>The current module instance.</returns>
        public StartProcess OnlyOnce(bool onlyOnce = true)
        {
            _onlyOnce = onlyOnce;
            return this;
        }

        /// <summary>
        /// Toggles whether to log process output.
        /// </summary>
        /// <remarks>
        /// By default, process output is only logged as debug messages. Output to standard error will always be logged.
        /// </remarks>
        /// <param name="logOutput"><c>true</c> to log process output, <c>false</c> otherwise.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess LogOutput(bool logOutput = true)
        {
            _logOutput = logOutput;
            return this;
        }

        /// <summary>
        /// Toggles throwing an exception if the process exits with a non-zero exit code.
        /// </summary>
        /// <remarks>
        /// By default the module will throw an exception if the process exits with a non-zero exit code.
        /// </remarks>
        /// <param name="continueOnError"><c>true</c> to continue when the process exits with a non-zero exit code, <c>false</c> to throw an exception.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess ContinueOnError(bool continueOnError = true)
        {
            _continueOnError = continueOnError;
            return this;
        }

        /// <summary>
        /// Provides a function that determines if the exit code from the process was an error.
        /// </summary>
        /// <remarks>
        /// By default any non-zero exit code is considered an error. Some processes return non-zero
        /// exit codes to indicate success and this lets you treat those as successful.
        /// </remarks>
        /// <param name="errorExitCode">A function that determines if the exit code is an error by returning <c>true</c>.</param>
        /// <returns>The current module instance.</returns>
        public StartProcess WithErrorExitCode(Func<int, bool> errorExitCode)
        {
            _errorExitCode = errorExitCode ?? throw new ArgumentNullException(nameof(errorExitCode));
            return this;
        }

        /// <summary>
        /// Keeps the existing document content instead of replacing it with the process output.
        /// </summary>
        /// <remarks>
        /// This has no effect if the process is a background process.
        /// </remarks>
        /// <param name="keepContent">
        /// <c>true</c> to keep the existing document content,
        /// <c>false</c> to replace it with the process output.
        /// </param>
        /// <returns>The current module instance.</returns>
        public StartProcess KeepContent(bool keepContent = true)
        {
            _keepContent = keepContent;
            return this;
        }

        protected override async Task<IEnumerable<IDocument>> ExecuteConfigAsync(IDocument input, IExecutionContext context, IMetadata values)
        {
            // Only execute once if requested
            if (_onlyOnce && _executed)
            {
                context.LogDebug("Process was configured to execute once, returning input document");
                return input.Yield();
            }
            _executed = true;

            // Get the filename
            string fileName = values.GetString(nameof(FileName));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                context.LogDebug("Provided file name was null or empty, skipping and returning input document");
                return input.Yield();
            }

            // Create the process start info
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Set arguments
            string arguments = values.GetString(nameof(Arguments));
            if (!string.IsNullOrEmpty(arguments))
            {
                startInfo.Arguments = arguments;
            }

            // Set working directory
            string workingDirectory = values.GetString(nameof(WorkingDirectory));
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = context.FileSystem.RootPath.FullPath;
            }
            else
            {
                startInfo.WorkingDirectory = context.FileSystem.RootPath.Combine(workingDirectory).FullPath;
            }

            // Set environment variables for the process
            foreach (KeyValuePair<string, string> environmentVariable in _environmentVariables)
            {
                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
                startInfo.EnvironmentVariables[environmentVariable.Key] = environmentVariable.Value;
            }

            // Create the process
            Process process = new Process
            {
                StartInfo = startInfo
            };

            // Raises Process.Exited immediately instead of when checked via .WaitForExit() or .HasExited
            process.EnableRaisingEvents = true;
            process.Exited += ProcessExited;

            // Prepare the streams
            using (Stream contentStream = _background || _keepContent ? null : await context.GetContentStreamAsync())
            {
                using (StreamWriter contentWriter = contentStream == null ? null : new StreamWriter(contentStream))
                {
                    using (StringWriter errorWriter = !_background && _continueOnError ? new StringWriter() : null)
                    {
                        // Write to the stream on data received
                        // If we happen to write before we've created and added the process to the collection, go ahead and do that too
                        ILoggerFactory loggerFactory = context.GetService<ILoggerFactory>();
                        process.OutputDataReceived += (_, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                (Process, ILogger) item = _processes.GetOrAdd(
                                    process.Id,
                                    _ => (process, loggerFactory?.CreateLogger($"{process.Id}: {Path.GetFileName(startInfo.FileName)}")));
                                item.Item2?.Log(_logOutput ? LogLevel.Information : LogLevel.Debug, e.Data);
                                contentWriter?.WriteLine(e.Data);
                            }
                        };
                        process.ErrorDataReceived += (_, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                (Process, ILogger) item = _processes.GetOrAdd(
                                    process.Id,
                                    _ => (process, loggerFactory?.CreateLogger($"{process.Id}: {Path.GetFileName(startInfo.FileName)}")));
                                item.Item2?.LogError(e.Data);
                                errorWriter?.WriteLine(e.Data);
                            }
                        };

                        // Start the process
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        // Use a separate logger, but only create and add it if it wasn't already from one of the received events
                        _processes.GetOrAdd(
                            process.Id,
                            _ => (process, loggerFactory?.CreateLogger($"{process.Id}: {Path.GetFileName(startInfo.FileName)}")));

                        context.Log(
                            _logOutput ? LogLevel.Information : LogLevel.Debug,
                            $"Started {(_background ? "background " : string.Empty)}process {process.Id}: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

                        // If this is a background process, let it run and just return the original document
                        if (_background)
                        {
                            return input.Yield();
                        }

                        // Otherwise wait for exit
                        int exitCode = 0;
                        try
                        {
                            if (_timeout > 0)
                            {
                                process.WaitForExit(_timeout);
                            }
                            else
                            {
                                process.WaitForExit();
                            }

                            // Log exit code and throw if non-zero
                            exitCode = process.ExitCode;
                            if (_errorExitCode(process.ExitCode))
                            {
                                string errorMessage = $"Process {process.Id} exited with error code {process.ExitCode}";
                                if (!_continueOnError)
                                {
                                    throw new ExecutionException(errorMessage);
                                }
                                context.LogError(errorMessage);
                            }
                        }
                        finally
                        {
                            process.Close();
                        }

                        // Finish the stream and return a document with output as content
                        contentWriter?.Flush();
                        errorWriter?.Flush();
                        string errorData = errorWriter?.ToString();
                        MetadataItems metadata = new MetadataItems
                        {
                            { ExitCode, exitCode }
                        };
                        if (!string.IsNullOrEmpty(errorData))
                        {
                            metadata.Add(ErrorData, errorData);
                        }
                        return context.CloneOrCreateDocument(
                            input,
                            metadata,
                            contentStream == null ? null : context.GetContentProvider(contentStream))
                            .Yield();
                    }
                }
            }
        }

        private void ProcessExited(object sender, EventArgs e)
        {
            Process process = (Process)sender;
            if (_processes.TryRemove(process.Id, out (Process, ILogger) item))
            {
                item.Item2?.Log(
                    _logOutput ? LogLevel.Information : LogLevel.Debug,
                    $"Process {process.Id} exited with code {process.ExitCode}");
            }
        }

        public void Dispose()
        {
            foreach ((Process, ILogger) item in _processes.Values)
            {
                item.Item1.Close();
                item.Item1.Exited -= ProcessExited;
            }
            _processes.Clear();
        }
    }
}