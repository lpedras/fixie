﻿namespace Fixie.Internal
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipes;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Cli;
    using Listeners;
    using static System.Console;

    public class AssemblyRunner
    {
        enum ExitCode
        {
            Success = 0,
            Failure = 1,
            FatalError = -1
        }

        public static int Main(string[] arguments)
        {
            try
            {
                CommandLine.Partition(arguments, out var runnerArguments, out var customArguments);

                var options = CommandLine.Parse<Options>(runnerArguments);

                options.Validate();

                var assembly = Assembly.GetEntryAssembly();

                var pipeName = Environment.GetEnvironmentVariable("FIXIE_NAMED_PIPE");

                var runner = new AssemblyRunner();

                if (pipeName == null)
                    return (int)runner.RunAssembly(assembly, options, customArguments);

                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    runner.Subscribe(new PipeListener(pipe));

                    pipe.Connect();
                    pipe.ReadMode = PipeTransmissionMode.Message;

                    var exitCode = ExitCode.Success;

                    try
                    {
                        var messageType = pipe.ReceiveMessage();

                        if (messageType == typeof(PipeMessage.DiscoverTests).FullName)
                        {
                            var discoverTests = pipe.Receive<PipeMessage.DiscoverTests>();
                            runner.DiscoverMethods(assembly, customArguments);
                        }
                        else if (messageType == typeof(PipeMessage.ExecuteTests).FullName)
                        {
                            var executeTests = pipe.Receive<PipeMessage.ExecuteTests>();

                            exitCode = executeTests.Filter.Length > 0
                                ? runner.RunTests(assembly, options, customArguments, executeTests.Filter)
                                : runner.RunAssembly(assembly, options, customArguments);
                        }
                        else
                        {
                            var body = pipe.ReceiveMessage();
                            throw new Exception($"Test assembly received unexpected message of type {messageType}: {body}");
                        }
                    }
                    catch (Exception exception)
                    {
                        pipe.Send(exception);
                    }
                    finally
                    {
                        pipe.Send<PipeMessage.Completed>();
                    }

                    return (int)exitCode;
                }
            }
            catch (Exception exception)
            {
                using (Foreground.Red)
                    WriteLine($"Fatal Error: {exception}");

                return (int)ExitCode.FatalError;
            }
        }

        readonly List<Listener> customListeners = new List<Listener>();

        void Subscribe<TListener>(TListener listener) where TListener : Listener
        {
            customListeners.Add(listener);
        }

        void DiscoverMethods(Assembly assembly, string[] customArguments)
        {
            var listeners = customListeners;
            var bus = new Bus(listeners);
            var discoverer = new Discoverer(bus, customArguments);

            discoverer.DiscoverMethods(assembly);
        }

        ExitCode RunAssembly(Assembly assembly, Options options, string[] customArguments)
        {
            return Run(assembly, options, customArguments, runner => runner.Run());
        }

        ExitCode RunTests(Assembly assembly, Options options, string[] customArguments, PipeMessage.Test[] tests)
        {
            return Run(assembly, options, customArguments,
                r => r.Run(tests.Select(x => new Test(x.Class, x.Method)).ToList()));
        }

        ExitCode Run(Assembly assembly, Options options, string[] customArguments, Func<Runner, ExecutionSummary> run)
        {
            var listeners = GetExecutionListeners(options);
            var bus = new Bus(listeners);
            var runner = new Runner(assembly, bus, customArguments);

            var summary = run(runner);

            if (summary.Total == 0)
                return ExitCode.FatalError;

            if (summary.Failed > 0)
                return ExitCode.Failure;

            return ExitCode.Success;
        }

        List<Listener> GetExecutionListeners(Options options)
        {
            return customListeners.Any() ? customListeners : DefaultExecutionListeners(options).ToList();
        }

        static IEnumerable<Listener> DefaultExecutionListeners(Options options)
        {
            if (ShouldUseAzureListener())
                yield return new AzureListener();

            if (AppVeyorListener.TryCreate(out var listener))
                yield return listener;

            if (options.Report != null)
                yield return new ReportListener(SaveReport(options));

            if (ShouldUseTeamCityListener())
                yield return new TeamCityListener();
            else
                yield return new ConsoleListener();
        }

        static Action<XDocument> SaveReport(Options options)
        {
            return report => ReportListener.Save(report, FullPath(options.Report));
        }

        static string FullPath(string absoluteOrRelativePath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), absoluteOrRelativePath);
        }

        static bool ShouldUseAzureListener()
        {
            var runningUnderAzure = Environment.GetEnvironmentVariable("TF_BUILD") == "True";

            if (runningUnderAzure)
            {
                var accessTokenIsAvailable =
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN"));

                if (accessTokenIsAvailable)
                    return true;

                using (Foreground.Yellow)
                {
                    WriteLine("The Azure DevOps access token has not been made available to this process, so");
                    WriteLine("test results will not be collected. To resolve this issue, review your pipeline");
                    WriteLine("definition to ensure that the access token is made available as the environment");
                    WriteLine("variable SYSTEM_ACCESSTOKEN.");
                    WriteLine();
                    WriteLine("From https://docs.microsoft.com/en-us/azure/devops/pipelines/build/variables#systemaccesstoken");
                    WriteLine();
                    WriteLine("  env:");
                    WriteLine("    SYSTEM_ACCESSTOKEN: $(System.AccessToken)");
                    WriteLine();
                }
            }

            return false;
        }

        static bool ShouldUseTeamCityListener()
        {
            return Environment.GetEnvironmentVariable("TEAMCITY_PROJECT_NAME") != null;
        }
    }
}