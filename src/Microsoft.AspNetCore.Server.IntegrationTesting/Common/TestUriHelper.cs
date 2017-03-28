// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.IntegrationTesting.Common
{
    public static class TestUriHelper
    {
        private static readonly Random Random = new Random();
        public static Uri BuildTestUri()
        {
            return new UriBuilder("http", "localhost", FindFreePort()).Uri;
        }

        public static Uri BuildTestUri(string hint)
        {
            if (string.IsNullOrEmpty(hint))
            {
                return BuildTestUri();
            }
            else
            {
                var uriHint = new Uri(hint);
                return new UriBuilder(uriHint) { Port = FindFreePort(uriHint.Port) }.Uri;
            }
        }

        public static int FindFreePort()
        {
            return FindFreePort(0);
        }

        public static int FindFreePort(int initialPort)
        {
            Socket socket;
            using (socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                // Set linger to true, but the linger time to zero, which forces the socket to shut down immediately when closed
                // The default behavior is that linger is off, which actually means that the socket may sit for a small amount
                // of time before being fully closed and reset.
                // More here: https://msdn.microsoft.com/en-us/library/windows/desktop/ms737582(v=vs.85).aspx
                socket.LingerState = new LingerOption(enable: true, seconds: 0);

                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, initialPort));
                }
                catch (SocketException)
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                }

                // Forcibly shut down the socket, this is probably unnecessary but better safe than sorry.
                socket.Shutdown(SocketShutdown.Both);

                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        public static int GetRandomPort()
        {
            return Random.Next(2000, ushort.MaxValue);
        }
    }
}
