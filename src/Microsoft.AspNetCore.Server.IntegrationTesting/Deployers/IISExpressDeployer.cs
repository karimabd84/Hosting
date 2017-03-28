// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.IntegrationTesting.Common;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.IntegrationTesting
{
    /// <summary>
    /// Deployment helper for IISExpress.
    /// </summary>
    public class IISExpressDeployer : ApplicationDeployer
    {
        private const string IISExpressRunningMessage = "IIS Express is running.";
        private const string FailedToInitializeBindingsMessage = "Failed to initialize site bindings";
        private const string UnableToStartIISExpressMessage = "Unable to start iisexpress.";
        private readonly static int MaximumAttempts = 5;
        private static readonly Regex UrlDetectorRegex = new Regex(@"^\s*Successfully registered URL ""(?<url>[^""]+)"" for site.*$");
        private Process _hostProcess;

        public IISExpressDeployer(DeploymentParameters deploymentParameters, ILogger logger)
            : base(deploymentParameters, logger)
        {
        }

        public bool IsWin8OrLater
        {
            get
            {
                var win8Version = new Version(6, 2);

                return (new Version(Extensions.Internal.RuntimeEnvironment.OperatingSystemVersion) >= win8Version);
            }
        }

        public bool Is64BitHost
        {
            get
            {
                return RuntimeInformation.OSArchitecture == Architecture.X64
                    || RuntimeInformation.OSArchitecture == Architecture.Arm64;
            }
        }

        public override async Task<DeploymentResult> DeployAsync()
        {
            // Start timer
            StartTimer();

            // For now we always auto-publish. Otherwise we'll have to write our own local web.config for the HttpPlatformHandler
            DeploymentParameters.PublishApplicationBeforeDeployment = true;
            if (DeploymentParameters.PublishApplicationBeforeDeployment)
            {
                DotnetPublish();
            }

            var contentRoot = DeploymentParameters.PublishApplicationBeforeDeployment ? DeploymentParameters.PublishedApplicationRootPath : DeploymentParameters.ApplicationPath;

            var hintUri = new Uri(
                string.IsNullOrEmpty(DeploymentParameters.ApplicationBaseUriHint) ?
                "http://localhost:0" :
                DeploymentParameters.ApplicationBaseUriHint);
            // Launch the host process.
            var (actualUri, hostExitToken) = await StartIISExpressAsync(hintUri, contentRoot);

            return new DeploymentResult
            {
                ContentRoot = contentRoot,
                DeploymentParameters = DeploymentParameters,
                // Right now this works only for urls like http://localhost:5001/. Does not work for http://localhost:5001/subpath.
                ApplicationBaseUri = actualUri.ToString(),
                HostShutdownToken = hostExitToken
            };
        }

        private async Task<(string url, CancellationToken hostExitToken)> StartIISExpressAsync(Uri uri, string contentRoot)
        {
            var port = uri.Port;
            if (port == 0)
            {
                port = TestUriHelper.GetRandomPort();
            }

            for (var attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                if (!string.IsNullOrWhiteSpace(DeploymentParameters.ServerConfigTemplateContent))
                {
                    var serverConfig = DeploymentParameters.ServerConfigTemplateContent;

                    // Pass on the applicationhost.config to iis express. With this don't need to pass in the /path /port switches as they are in the applicationHost.config
                    // We take a copy of the original specified applicationHost.Config to prevent modifying the one in the repo.

                    if (serverConfig.Contains("[ANCMPath]"))
                    {
                        string ancmPath;
                        if (!IsWin8OrLater)
                        {
                            // The nupkg build of ANCM does not support Win7. https://github.com/aspnet/AspNetCoreModule/issues/40.
                            ancmPath = @"%ProgramFiles%\IIS Express\aspnetcore.dll";
                        }
                        else
                        {
                            // We need to pick the bitness based the OS / IIS Express, not the application.
                            // We'll eventually add support for choosing which IIS Express bitness to run: https://github.com/aspnet/Hosting/issues/880
                            var ancmFile = Is64BitHost ? "aspnetcore_x64.dll" : "aspnetcore_x86.dll";
                            // Bin deployed by Microsoft.AspNetCore.AspNetCoreModule.nupkg
                            if (DeploymentParameters.RuntimeFlavor == RuntimeFlavor.CoreClr
                                && DeploymentParameters.ApplicationType == ApplicationType.Portable)
                            {
                                ancmPath = Path.Combine(contentRoot, @"runtimes\win7\native\", ancmFile);
                            }
                            else
                            {
                                ancmPath = Path.Combine(contentRoot, ancmFile);
                            }
                        }

                        if (!File.Exists(Environment.ExpandEnvironmentVariables(ancmPath)))
                        {
                            throw new FileNotFoundException("AspNetCoreModule could not be found.", ancmPath);
                        }

                        serverConfig =
                            serverConfig.Replace("[ANCMPath]", ancmPath);
                    }

                    serverConfig =
                        serverConfig
                            .Replace("[ApplicationPhysicalPath]", contentRoot)
                            .Replace("[PORT]", port.ToString());

                    DeploymentParameters.ServerConfigLocation = Path.GetTempFileName();

                    File.WriteAllText(DeploymentParameters.ServerConfigLocation, serverConfig);
                }

                var parameters = string.IsNullOrWhiteSpace(DeploymentParameters.ServerConfigLocation) ?
                                string.Format("/port:{0} /path:\"{1}\" /trace:error", uri.Port, contentRoot) :
                                string.Format("/site:{0} /config:{1} /trace:error", DeploymentParameters.SiteName, DeploymentParameters.ServerConfigLocation);

                var iisExpressPath = GetIISExpressPath();

                Logger.LogInformation("Executing command : {iisExpress} {args}", iisExpressPath, parameters);

                var startInfo = new ProcessStartInfo
                {
                    FileName = iisExpressPath,
                    Arguments = parameters,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                AddEnvironmentVariablesToProcess(startInfo, DeploymentParameters.EnvironmentVariables);

                string url = null;
                var started = new TaskCompletionSource<bool>();

                var process = new Process() { StartInfo = startInfo };
                process.ErrorDataReceived += (sender, dataArgs) => { Logger.LogError("iisexpress:" + dataArgs.Data ?? string.Empty); };
                process.OutputDataReceived += (sender, dataArgs) =>
                {
                    if (string.Equals(dataArgs.Data, UnableToStartIISExpressMessage))
                    {
                        // We completely failed to start and we don't really know why
                        started.TrySetException(new InvalidOperationException("Failed to start IIS Express"));
                    }
                    else if (string.Equals(dataArgs.Data, FailedToInitializeBindingsMessage))
                    {
                        started.TrySetResult(false);
                    }
                    else if (string.Equals(dataArgs.Data, IISExpressRunningMessage))
                    {
                        started.TrySetResult(true);
                    }
                    else if (!string.IsNullOrEmpty(dataArgs.Data))
                    {
                        var m = UrlDetectorRegex.Match(dataArgs.Data);
                        if (m.Success)
                        {
                            url = m.Groups["url"].Value;
                        }
                    }
                    Logger.LogInformation("iisexpress:" + dataArgs.Data ?? string.Empty);
                };
                process.EnableRaisingEvents = true;
                var hostExitTokenSource = new CancellationTokenSource();
                process.Exited += (sender, e) =>
                {
                    TriggerHostShutdown(hostExitTokenSource);
                };
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                if (process.HasExited)
                {
                    Logger.LogError("Host process {processName} exited with code {exitCode} or failed to start.", startInfo.FileName, _hostProcess.ExitCode);
                    throw new Exception("Failed to start host");
                }

                // Wait for the app to start
                if (!await started.Task)
                {
                    // Wait for the process to exit
                    process.WaitForExit(30 * 1000);

                    // Try another port
                    port = TestUriHelper.GetRandomPort();
                }
                else
                {
                    _hostProcess = process;
                    Logger.LogInformation("Started iisexpress. Process Id : {processId}, Port: {port}", _hostProcess.Id, port);
                    return (url: url, hostExitToken: hostExitTokenSource.Token);
                }
            }

            throw new TimeoutException($"Failed to initialize IIS Express after {MaximumAttempts} attempts to select a port");
        }

        private string GetIISExpressPath()
        {
            // Get path to program files
            var iisExpressPath = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") + "\\", "Program Files", "IIS Express", "iisexpress.exe");

            if (!File.Exists(iisExpressPath))
            {
                throw new Exception("Unable to find IISExpress on the machine: " + iisExpressPath);
            }

            return iisExpressPath;
        }

        public override void Dispose()
        {
            ShutDownIfAnyHostProcess(_hostProcess);

            if (!string.IsNullOrWhiteSpace(DeploymentParameters.ServerConfigLocation)
                && File.Exists(DeploymentParameters.ServerConfigLocation))
            {
                // Delete the temp applicationHostConfig that we created.
                try
                {
                    File.Delete(DeploymentParameters.ServerConfigLocation);
                }
                catch (Exception exception)
                {
                    // Ignore delete failures - just write a log.
                    Logger.LogWarning("Failed to delete '{config}'. Exception : {exception}", DeploymentParameters.ServerConfigLocation, exception.Message);
                }
            }

            if (DeploymentParameters.PublishApplicationBeforeDeployment)
            {
                CleanPublishedOutput();
            }

            InvokeUserApplicationCleanup();

            StopTimer();
        }
    }
}