// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.IntegrationTesting
{
    /// <summary>
    /// Common operations on an application deployer.
    /// </summary>
    public interface IApplicationDeployer : IDisposable
    {
        /// <summary>
        /// Deploys the application to the target with specified <see cref="DeploymentParameters"/>.
        /// </summary>
        /// <returns></returns>
        Task<DeploymentResult> DeployAsync();
    }

    public static class ApplicationDeployerExtensions
    {
        public static readonly int DefaultTimeoutMillseconds = 10 * 1000;

        [Obsolete("Use DeployAsync instead")]
        public static DeploymentResult Deploy(this IApplicationDeployer deployer) => Deploy(deployer, DefaultTimeoutMillseconds);

        [Obsolete("Use DeployAsync instead")]
        public static DeploymentResult Deploy(this IApplicationDeployer deployer, int millisecondsTimeout)
        {
            var task = deployer.DeployAsync();
            task.Wait(millisecondsTimeout);
            return task.Result;
        }
    }
}