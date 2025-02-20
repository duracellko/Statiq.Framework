﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;
using Statiq.Common;
using Statiq.Core;
using Statiq.Testing;

namespace Statiq.App.Tests.Bootstrapper
{
    [TestFixture]
    [NonParallelizable]
    public class BootstrapperFixture : BaseFixture
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ConsoleLoggerProvider.PreventDisposeAll = true;
        }

        public class RunTests : BootstrapperFixture
        {
            [Test]
            public async Task LogsVersion()
            {
                // Given
                string[] args = new[] { "build" };
                IBootstrapper bootstrapper = App.Bootstrapper.Create(args);
                bootstrapper.AddCommand<BuildCommand<EngineCommandSettings>>("build");
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                bootstrapper.AddPipeline("Foo");

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                provider.Messages.ShouldContain(x => x.FormattedMessage.StartsWith("Statiq version"));
            }

            [Test]
            public async Task NoPipelinesWarning()
            {
                // Given
                string[] args = new[] { "build" };
                IBootstrapper bootstrapper = App.Bootstrapper.Create(args);
                bootstrapper.AddCommand<BuildCommand<EngineCommandSettings>>("build");
                TestLoggerProvider provider = new TestLoggerProvider
                {
                    ThrowLogLevel = LogLevel.None
                };
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                provider.Messages.ShouldContain(x =>
                    x.LogLevel == LogLevel.Warning
                    && x.FormattedMessage == "No pipelines are configured or specified for execution.");
            }

            [TestCase("Trace", 19)] // Includes module start/finish
            [TestCase("Debug", 18)] // Include modules start/finish
            [TestCase("Information", 5)] // Includes pipeline finish
            [TestCase("Warning", 3)]
            [TestCase("Error", 2)]
            [TestCase("Critical", 1)]
            public async Task SetsLogLevel(string logLevel, int expected)
            {
                // Given
                string[] args = new[] { "build", "-l", logLevel };
                IBootstrapper bootstrapper = App.Bootstrapper.Create(args);
                bootstrapper.AddCommand<BuildCommand<EngineCommandSettings>>("build");
                TestLoggerProvider provider = new TestLoggerProvider
                {
                    ThrowLogLevel = LogLevel.None
                };
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                bootstrapper.AddPipeline(
                    "Foo",
                    new Core.LogMessage(LogLevel.Trace, "A"),
                    new Core.LogMessage(LogLevel.Debug, "B"),
                    new Core.LogMessage(LogLevel.Information, "C"),
                    new Core.LogMessage(LogLevel.Warning, "D"),
                    new Core.LogMessage(LogLevel.Error, "E"),
                    new Core.LogMessage(LogLevel.Critical, "F"));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                provider.Messages.Count(x => x.FormattedMessage.StartsWith("Foo/Process")).ShouldBe(expected);
            }

            [Test]
            public async Task CatalogsType()
            {
                // Given
                string[] args = new[] { "build", "-l", "Debug" };
                IBootstrapper bootstrapper = App.Bootstrapper.Create(args);
                bootstrapper.AddCommand<BuildCommand<EngineCommandSettings>>("build");
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                bootstrapper.AddPipeline("Foo");

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                bootstrapper.ClassCatalog.GetTypesAssignableTo<BootstrapperFixture>().Count().ShouldBe(1);
                provider.Messages.ShouldContain(x => x.FormattedMessage.StartsWith("Cataloging types in assembly"));
            }
        }

        public class CreateDefaultTests : BootstrapperFixture
        {
            [Test]
            public async Task EnvironmentVariableConfiguration()
            {
                // Given
                string[] args = new string[] { };
                Environment.SetEnvironmentVariable(nameof(EnvironmentVariableConfiguration), "Foo");
                IBootstrapper bootstrapper = App.Bootstrapper.CreateDefault(args);
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                object variable = null;
                bootstrapper.AddPipeline("Foo", new ExecuteConfig(Config.FromContext(x => variable = x.Settings[nameof(EnvironmentVariableConfiguration)])));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                variable.ShouldBe("Foo");
            }

            [Test]
            public async Task CommandLineSettingTakesPrecedenceOverEnvironmentVariables()
            {
                // Given
                string[] args = new string[] { "-s", $"{nameof(CommandLineSettingTakesPrecedenceOverEnvironmentVariables)}=Bar" };
                Environment.SetEnvironmentVariable(nameof(CommandLineSettingTakesPrecedenceOverEnvironmentVariables), "Foo");
                IBootstrapper bootstrapper = App.Bootstrapper.CreateDefault(args);
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                object variable = null;
                bootstrapper.AddPipeline("Foo", new ExecuteConfig(Config.FromContext(x => variable = x.Settings[nameof(CommandLineSettingTakesPrecedenceOverEnvironmentVariables)])));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                variable.ShouldBe("Bar");
            }

            [Test]
            public async Task SetsDefaultSettings()
            {
                // Given
                string[] args = new string[] { };
                IBootstrapper bootstrapper = App.Bootstrapper.CreateDefault(args);
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                object variable = null;
                bootstrapper.AddPipeline("Foo", new ExecuteConfig(Config.FromContext(x => variable = x.Settings[Keys.LinkHideIndexPages])));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                variable.ShouldBe("true");
            }

            [Test]
            public async Task CommandLineSettingTakesPrecedenceOverDefaultSettings()
            {
                // Given
                string[] args = new string[] { "-s", $"{Keys.LinkHideIndexPages}=false" };
                IBootstrapper bootstrapper = App.Bootstrapper.CreateDefault(args);
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                object variable = null;
                bootstrapper.AddPipeline("Foo", new ExecuteConfig(Config.FromContext(x => variable = x.Settings[Keys.LinkHideIndexPages])));

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                variable.ShouldBe("false");
            }
        }

        public class ConfigureSettingsTests : BootstrapperFixture
        {
            [Test]
            public async Task CanReadConfigurationValues()
            {
                // Given
                string[] args = new string[] { };
                Environment.SetEnvironmentVariable(nameof(CanReadConfigurationValues), "Foo");
                IBootstrapper bootstrapper = App.Bootstrapper.CreateDefault(args);
                TestLoggerProvider provider = new TestLoggerProvider();
                bootstrapper.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(provider));
                string variable = null;
                bootstrapper.ConfigureSettings(x => variable = x[nameof(CanReadConfigurationValues)]);
                bootstrapper.AddPipeline("Foo");

                // When
                int exitCode = await bootstrapper.RunAsync();

                // Then
                exitCode.ShouldBe((int)ExitCode.Normal);
                variable.ShouldBe("Foo");
            }
        }
    }
}
