using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Hosting.Fakes
{
    public class TestLoggerFactory : ILoggerFactory
    {
        public void CustomConfigureMethod()
        {

        }

        public void AddProvider(ILoggerProvider provider)
        {
            throw new NotImplementedException();
        }

        public ILogger CreateLogger(string categoryName)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }

    public class TestLogProvider : ILoggerProvider
    {
        public TestLogProvider(string value)
        {

        }

        public ILogger CreateLogger(string categoryName)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }

    public static class TestLoggerFactoryExtensions
    {
        public static IWebHostBuilder ConfigureTestLogger(this IWebHostBuilder builder, Action<TestLoggerFactory> configureLogger)
        {
            builder.UseLoggerFactory((env, config) =>
            {
                return new TestLoggerFactory();
            });
            builder.ConfigureLogging(configureLogger);
            return builder;
        }
    }
}
