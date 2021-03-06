﻿namespace Ser.ConAai
{
    #region Usings
    using Newtonsoft.Json;
    using NLog;
    using NLog.Config;
    using PeterKottas.DotNetCore.WindowsService;
    using Ser.ConAai.Config;
    using System;
    using System.IO;
    using System.Linq;
    using System.Xml;
    #endregion

    class Program
    {
        #region Logger
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public static ConnectorService Service { get; private set; }
        #endregion

        static void Main(string[] args)
        {
            try
            {
                SetLoggerSettings();

                if (args.Length > 0 && args[0] == "VersionNumber")
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Version.txt"), ConnectorVersion.GetMainVersion());
                    return;
                }

                ServiceRunner<ConnectorService>.Run(config =>
                {
                    config.SetDisplayName("AnalyticsGate Connector");
                    config.SetDescription("AGR Connector Service for Qlik");
                    var name = config.GetDefaultName();
                    config.Service(serviceConfig =>
                    {
                        serviceConfig.ServiceFactory((extraArguments, controller) =>
                        {
                            Service = new ConnectorService();
                            return Service;
                        });
                        serviceConfig.OnStart((service, extraParams) =>
                        {
                            logger.Debug($"Service {name} started");
                            service.Start();
                        });
                        serviceConfig.OnStop(service =>
                        {
                            logger.Debug($"Service {name} stopped");
                            service.Stop();
                        });
                        serviceConfig.OnError(ex =>
                        {
                            logger.Error($"Service Exception: {ex}");
                        });
                    });
                });

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                Environment.Exit(1);
            }
        }

        #region Private Methods
        private static XmlReader GetXmlReader(string path)
        {
            var jsonContent = File.ReadAllText(path);
            var xdoc = JsonConvert.DeserializeXNode(jsonContent);
            return xdoc.CreateReader();
        }

        private static void SetLoggerSettings()
        {
            var path = String.Empty;

            try
            {
                var files = Directory.GetFiles(AppContext.BaseDirectory, "*.*", SearchOption.TopDirectoryOnly)
                                     .Where(f => f.ToLowerInvariant().EndsWith("\\app.config") ||
                                                 f.ToLowerInvariant().EndsWith("\\app.json")).ToList();
                if (files != null && files.Count > 0)
                {
                    if (files.Count > 1)
                        throw new Exception("Too many logger configs found.");

                    path = files.FirstOrDefault();
                    var extention = Path.GetExtension(path);
                    switch (extention)
                    {
                        case ".json":
                            logger.Factory.Configuration = new XmlLoggingConfiguration(GetXmlReader(path), Path.GetFileName(path));
                            break;
                        case ".config":
                            logger.Factory.Configuration = new XmlLoggingConfiguration(path);
                            break;
                        default:
                            throw new Exception($"unknown log format {extention}.");
                    }
                }
                else
                {
                    throw new Exception("No logger config loaded.");
                }
            }
            catch
            {
                Console.WriteLine($"The logger setting are invalid!!!\nPlease check the {path} in the app folder.");
            }
        }
        #endregion
    }
}