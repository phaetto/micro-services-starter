/* **********************************************************************************
 *
 * Copyright (c) Microsoft Corporation. All rights reserved.
 *
 * This source code is subject to terms and conditions of the Microsoft Public
 * License (Ms-PL). A copy of the license can be found in the license.htm file
 * included in this distribution.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * **********************************************************************************/

namespace Services.Cassini.Core
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.Remoting;
    using System.Threading;
    using System.Web.Hosting;
    using Services.Management.Administration.Worker;

    public class Server : MarshalByRefObject, IWorkerEvents
    {
        int _port;
        string _hostip;
        string _virtualPath;
        string _physicalPath;
        bool _shutdownInProgress;
        Socket _socket;
        Host _host;

        public Server(int port, string hostip, string virtualPath, string physicalPath, WorkUnitContext workUnitContext)
        {
            _port = port;
            _hostip = hostip;
            _virtualPath = virtualPath;
            if (!Path.IsPathRooted(physicalPath))
                physicalPath = Path.GetFullPath(physicalPath);
            _physicalPath = physicalPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? physicalPath : physicalPath + Path.DirectorySeparatorChar;
            WorkUnitContext = workUnitContext;
        }

        public override object InitializeLifetimeService()
        {
            // never expire the license
            return null;
        }

        public string VirtualPath
        {
            get
            {
                return _virtualPath;
            }
        }

        public string PhysicalPath
        {
            get
            {
                return _physicalPath;
            }
        }

        public int Port
        {
            get
            {
                return _port;
            }
        }

        public string RootUrl
        {
            get
            {
                if (_port != 80)
                {
                    return "http://" + myAddress.ToString() + ":" + _port + _virtualPath;
                }
                else
                {
                    return "http://" + myAddress.ToString() + _virtualPath;
                }
            }
        }

        //
        // Socket listening
        // 

        IPAddress myAddress = null;
        IPAddress GetMyPublicIp()
        {
            if (!string.IsNullOrWhiteSpace(_hostip))
            {
                try
                {
                    myAddress = IPAddress.Parse(_hostip);
                    return myAddress;
                }
                catch
                {
                }
            }

            IPHostEntry host;
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    myAddress = ip;
                    return myAddress;
                }
            }
            myAddress = IPAddress.Loopback;
            return myAddress;
        }

        static Socket CreateSocketBindAndListen(AddressFamily family, IPAddress address, int port)
        {
            Socket socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen((int)SocketOptionName.MaxConnections);
            return socket;
        }

        public void Start()
        {
            try
            {
                _socket = CreateSocketBindAndListen(AddressFamily.InterNetwork, GetMyPublicIp(), _port);
            }
            catch
            {
                _socket = CreateSocketBindAndListen(AddressFamily.InterNetworkV6, IPAddress.IPv6Loopback, _port);
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                while (!_shutdownInProgress)
                {
                    try
                    {
                        Socket acceptedSocket = _socket.Accept();

                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            if (!_shutdownInProgress)
                            {
                                Connection conn = new Connection(this, acceptedSocket);

                                // wait for at least some input
                                if (conn.WaitForRequestBytes() == 0)
                                {
                                    conn.WriteErrorAndClose(400);
                                    return;
                                }

                                // find or create host
                                Host host = GetHost();
                                if (host == null)
                                {
                                    conn.WriteErrorAndClose(500);
                                    return;
                                }

                                // process request in worker app domain
                                host.ProcessRequest(conn);
                            }
                        });
                    }
                    catch
                    {
                        Thread.Sleep(100);
                    }
                }
            });
        }

        public void Stop()
        {
            _shutdownInProgress = true;

            try
            {
                if (_socket != null)
                {
                    _socket.Close();
                }
            }
            catch
            {
            }
            finally
            {
                _socket = null;
            }

            try
            {
                if (_host != null)
                {
                    _host.Shutdown();
                }

                while (_host != null)
                {
                    Thread.Sleep(100);
                }
            }
            catch
            {
            }
            finally
            {
                _host = null;
            }
        }

        // called at the end of request processing
        // to disconnect the remoting proxy for Connection object
        // and allow GC to pick it up
        internal void OnRequestEnd(Connection conn)
        {
            RemotingServices.Disconnect(conn);
        }

        public void HostStopped()
        {
            _host = null;
        }

        Host GetHost()
        {
            if (_shutdownInProgress)
                return null;

            Host host = _host;

            if (host == null)
            {
                lock (this)
                {
                    host = _host;
                    if (host == null)
                    {
                        host = (Host)CreateWorkerAppDomainWithHost(_virtualPath, _physicalPath, typeof(Host));
                        host.Configure(this, _port, _virtualPath, _physicalPath);
                        _host = host;
                    }
                }
            }

            return host;
        }

        static object CreateWorkerAppDomainWithHost(string virtualPath, string physicalPath, Type hostType)
        {
            // this creates worker app domain in a way that host need to be in GAC or bin
            string uniqueAppString = string.Concat(virtualPath, physicalPath).ToLowerInvariant();
            string appId = (uniqueAppString.GetHashCode()).ToString("x", CultureInfo.InvariantCulture);

            // create BuildManagerHost in the worker app domain
            ApplicationManager appManager = ApplicationManager.GetApplicationManager();

            // create Host in the worker app domain
            return appManager.CreateObject(appId, hostType, virtualPath, physicalPath, false);
        }

        public void OnStart()
        {
            Start();

            WorkUnitContext.LogLine("Path: " + PhysicalPath);
            WorkUnitContext.LogLine("Url: " + RootUrl);
            WorkUnitContext.LogLine("The server is listening for connections.");
        }

        public void OnStop()
        {
            Stop();
        }

        public WorkUnitContext WorkUnitContext { get; set; }
    }
}
