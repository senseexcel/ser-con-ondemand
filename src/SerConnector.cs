﻿#region License
/*
Copyright (c) 2018 Konrad Mattheis und Martin Berthold
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */
#endregion

namespace Ser.ConAai
{
    #region Usings
    using Grpc.Core;
    using Microsoft.Extensions.PlatformAbstractions;
    using Newtonsoft.Json;
    using NLog;
    using Qlik.Sse;
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using Hjson;
    using Ser.Api;
    using System.Collections.Generic;
    using Newtonsoft.Json.Linq;
    using PeterKottas.DotNetCore.WindowsService.Interfaces;
    using PeterKottas.DotNetCore.WindowsService.Base;
    using Q2g.HelperPem;
    using System.Net;
    #endregion

    public class SSEtoSER : MicroService, IMicroService
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties & Variables
        private Server server;
        private SerEvaluator serEvaluator;
        #endregion

        #region Private Methods
        private static void CreateCertificate(string certFile, string privateKeyFile)
        {
            try
            {
                var cert = new X509Certificate2();
                cert = cert.GenerateQlikJWTConformCert($"CN=SER-{Environment.MachineName}",
                                                       $"CN=SER-{Environment.MachineName}-CA");
                cert.SavePem(certFile, privateKeyFile);
                logger.Debug($"Certificate created under {certFile}.");
                logger.Debug($"Private key file created under {privateKeyFile}.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"The Method {nameof(CreateCertificate)} was failed.");
            }
        }
        #endregion

        #region Public Methods
        public void Start()
        {
            try
            {
                var configPath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "config.hjson");
                if (!File.Exists(configPath))
                {
                    var exampleConfigPath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "config.hjson.example");
                    if (File.Exists(exampleConfigPath))
                        File.Copy(exampleConfigPath, configPath);
                    else
                        throw new Exception($"config file {configPath} not found.");
                }
                var json = HjsonValue.Load(configPath).ToString();
                var configObject = JObject.Parse(json);

                //Gernerate virtual config for default values
                var serverName = Environment.MachineName;
                var fullQualifiedHostname = Dns.GetHostEntry(serverName).HostName;
                var vconnection = new SerOnDemandConfig()
                {
                    Connection = new SerConnection()
                    {
                        ServerUri = new Uri($"https://{fullQualifiedHostname}/ser")
                    }
                };
                var virtConnection = JObject.Parse(JsonConvert.SerializeObject(vconnection, Formatting.Indented));
                virtConnection.Merge(configObject);
                var config = JsonConvert.DeserializeObject<SerOnDemandConfig>(virtConnection.ToString());
                logger.Debug($"ServerUri: {config.Connection.ServerUri}");
                
                //check to generate certifiate and private key if not exists
                var certFile = config?.Connection?.Credentials?.Cert ?? null;
                certFile = PathUtils.GetFullPathFromApp(certFile);
                if (!File.Exists(certFile))
                {
                    var privateKeyFile = config?.Connection?.Credentials?.PrivateKey ?? null;
                    privateKeyFile = PathUtils.GetFullPathFromApp(privateKeyFile);
                    if (File.Exists(privateKeyFile))
                        privateKeyFile = null;

                    CreateCertificate(certFile, privateKeyFile);
                }
                
                logger.Info($"Version: {Ser.ConAai.GitVersionInformation.InformationalVersion}");
                logger.Debug($"Plattfom: {config.OS}");
                logger.Debug($"Architecture: {config.Architecture}");
                logger.Debug($"Framework: {config.Framework}");
                logger.Debug("Service running...");
                logger.Debug($"Start Service on Port \"{config.BindingPort}\" with Host \"{config.BindingHost}");
                logger.Debug($"Server start...");

                using (serEvaluator = new SerEvaluator(config))
                {
                    server = new Server()
                    {
                        Services = { Connector.BindService(serEvaluator) },
                        Ports = { new ServerPort(config.BindingHost, config.BindingPort, ServerCredentials.Insecure) },
                    };

                    server.Start();
                    logger.Info($"gRPC listening on port {config.BindingPort} on Host {config.BindingHost}");
                    logger.Info($"Ready...");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Service could not be started.");
            }
        }

        public void Stop()
        {
            try
            {
                logger.Info("Shutdown SSEtoSER...");
                server?.ShutdownAsync().Wait();
                serEvaluator.Dispose();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Service could not be stoppt.");
            }
        }
        #endregion
    }
}