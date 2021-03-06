// Copyright (c) 2018-2020, Els_kom org.
// https://github.com/Elskom/
// All rights reserved.
// license: MIT, see LICENSE for more details.

namespace Elskom.Generic.Libs
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Messaging;
    using System.Net;
    using System.Xml.Linq;
    using Elskom.Generic.Libs.Properties;

    /// <summary>
    /// A generic plugin update checker.
    /// </summary>
    public class PluginUpdateCheck : IDisposable
    {
        private bool disposedValue;

        /// <summary>
        /// Finalizes an instance of the <see cref="PluginUpdateCheck"/> class.
        /// </summary>
        ~PluginUpdateCheck()
            => this.Dispose(false);

        /// <summary>
        /// Event that fires when a new message should show up.
        /// </summary>
        public static event EventHandler<MessageEventArgs> MessageEvent;

        /// <summary>
        /// Gets the plugin urls used in all instances.
        /// </summary>
        public static List<string> PluginUrls { get; private protected set; }

        /// <summary>
        /// Gets a value indicating whether there are any pending updates and displays a message if there is.
        /// </summary>
        public bool ShowMessage
        {
            get
            {
                if (!string.Equals(this.InstalledVersion, this.CurrentVersion, StringComparison.Ordinal) && !string.IsNullOrEmpty(this.InstalledVersion))
                {
                    MessageEvent?.Invoke(this, new MessageEventArgs(string.Format(Resources.PluginUpdateCheck_ShowMessage_Update_for_plugin_is_availible, this.CurrentVersion, this.PluginName), Resources.PluginUpdateCheck_ShowMessage_New_plugin_update, ErrorLevel.Info));
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the plugin name this instance is pointing to.
        /// </summary>
        public string PluginName { get; private protected set; }

        /// <summary>
        /// Gets the current version of the plugin that is pointed to by this instance.
        /// </summary>
        public string CurrentVersion { get; private protected set; }

        /// <summary>
        /// Gets the installed version of the plugin that is pointed to by this instance.
        /// </summary>
        public string InstalledVersion { get; private protected set; }

        /// <summary>
        /// Gets the url to download the files to the plugin from.
        /// </summary>
        public Uri DownloadUrl { get; private protected set; }

        /// <summary>
        /// Gets the files to the plugin to download.
        /// </summary>
        public List<string> DownloadFiles { get; private protected set; }

        internal static WebClient WebClient { get; private protected set; }

        /// <summary>
        /// Checks for plugin updates from the provided plugin source urls.
        /// </summary>
        /// <param name="pluginURLs">The repository urls to the plugins.</param>
        /// <param name="pluginTypes">A list of types to the plugins to check for updates to.</param>
        /// <returns>A list of <see cref="PluginUpdateCheck"/> instances representing the plugins that needs updating or are to be installed.</returns>
        // catches the plugin urls and uses that cache to detect added urls, and only appends those to the list.
        public static List<PluginUpdateCheck> CheckForUpdates(string[] pluginURLs, List<Type> pluginTypes)
        {
            _ = pluginURLs ?? throw new ArgumentNullException(nameof(pluginURLs));
            _ = pluginTypes ?? throw new ArgumentNullException(nameof(pluginTypes));
            var pluginUpdateChecks = new List<PluginUpdateCheck>();

            // fixup the github urls (if needed).
            for (var i = 0; i < pluginURLs.Length; i++)
            {
                pluginURLs[i] = pluginURLs[i].Replace(
                    "https://github.com/",
                    "https://raw.githubusercontent.com/") + (
                    pluginURLs[i].EndsWith("/", StringComparison.Ordinal) ? "master/plugins.xml" : "/master/plugins.xml");
            }

            WebClient ??= new WebClient();
            PluginUrls ??= new List<string>();
            foreach (var pluginURL in pluginURLs)
            {
                if (!PluginUrls.Contains(pluginURL))
                {
                    try
                    {
                        var doc = XDocument.Parse(WebClient.DownloadString(pluginURL));
                        var elements = doc.Root.Elements("Plugin");
                        foreach (var element in elements)
                        {
                            var currentVersion = element.Attribute("Version").Value;
                            var pluginName = element.Attribute("Name").Value;
                            var found = false;
                            foreach (var pluginType in pluginTypes)
                            {
                                if (pluginName.Equals(pluginType.Namespace, StringComparison.Ordinal))
                                {
                                    found = true;
                                    var installedVersion = pluginType.Assembly.GetName().Version.ToString();
                                    var pluginUpdateCheck = new PluginUpdateCheck
                                    {
                                        CurrentVersion = currentVersion,
                                        InstalledVersion = installedVersion,
                                        PluginName = pluginName,
                                        DownloadUrl = new Uri($"{element.Attribute("DownloadUrl").Value}/{currentVersion}/"),
                                        DownloadFiles = element.Descendants("DownloadFile").Select(y => y.Attribute("Name").Value).ToList(),
                                    };
                                    pluginUpdateChecks.Add(pluginUpdateCheck);
                                }
                            }

                            if (!found)
                            {
                                var pluginUpdateCheck = new PluginUpdateCheck
                                {
                                    CurrentVersion = currentVersion,
                                    InstalledVersion = string.Empty,
                                    PluginName = pluginName,
                                    DownloadUrl = new Uri($"{element.Attribute("DownloadUrl").Value}/{currentVersion}/"),
                                    DownloadFiles = element.Descendants("DownloadFile").Select(y => y.Attribute("Name").Value).ToList(),
                                };
                                pluginUpdateChecks.Add(pluginUpdateCheck);
                            }
                        }
                    }
                    catch (WebException ex)
                    {
                        MessageEvent?.Invoke(typeof(PluginUpdateCheck), new MessageEventArgs(string.Format(Resources.PluginUpdateCheck_CheckForUpdates_Failed_to_download_the_plugins_sources_list_Reason, Environment.NewLine, ex.Message), Resources.PluginUpdateCheck_CheckForUpdates_Error, ErrorLevel.Error));
                    }
                }

                // append the string to the cache.
                PluginUrls.Add(pluginURL);
            }

            return pluginUpdateChecks;
        }

        /// <summary>
        /// Cleans up the resources used by <see cref="PluginUpdateCheck"/>.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Installs the files to the plugin pointed to by this instance.
        /// </summary>
        /// <param name="saveToZip">A bool indicating if the file should be installed to a zip file instead of a folder.</param>
        /// <returns>A bool indicating if anything changed.</returns>
        public bool Install(bool saveToZip)
        {
            foreach (var downloadFile in this.DownloadFiles)
            {
                try
                {
                    var path = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}plugins{Path.DirectorySeparatorChar}{downloadFile}";
                    WebClient.DownloadFile($"{this.DownloadUrl}{downloadFile}", path);
                    if (saveToZip)
                    {
                        var zippath = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}plugins.zip";
                        using (var zipFile = ZipFile.Open(zippath, ZipArchiveMode.Update))
                        {
                            foreach (var entry in zipFile.Entries)
                            {
                                if (entry.FullName.Equals(downloadFile, StringComparison.Ordinal))
                                {
                                    entry.Delete();
                                }
                            }

                            _ = zipFile.CreateEntryFromFile(path, downloadFile);
                            File.Delete(path);
                        }
                    }

                    return true;
                }
                catch (WebException ex)
                {
                    MessageEvent?.Invoke(this, new MessageEventArgs(string.Format(Resources.PluginUpdateCheck_Install_Failed_to_install_the_selected_plugin_Reason, Environment.NewLine, ex.Message), Resources.PluginUpdateCheck_CheckForUpdates_Error, ErrorLevel.Error));
                }
            }

            return false;
        }

        /// <summary>
        /// Uninstalls the files to the plugin pointed to by this instance.
        /// </summary>
        /// <param name="saveToZip">A bool indicating if the file should be uninstalled from a zip file instead of a folder. If the zip file after the operation becomes empty it is also deleted automatically.</param>
        /// <returns>A bool indicating if anything changed.</returns>
        public bool Uninstall(bool saveToZip)
        {
            try
            {
                foreach (var downloadFile in this.DownloadFiles)
                {
                    var path = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}plugins{Path.DirectorySeparatorChar}{downloadFile}";
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    if (saveToZip)
                    {
                        var zippath = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}plugins.zip";
                        using (var zipFile = ZipFile.Open(zippath, ZipArchiveMode.Update))
                        {
                            foreach (var entry in zipFile.Entries)
                            {
                                if (entry.FullName.Equals(downloadFile, StringComparison.Ordinal))
                                {
                                    entry.Delete();
                                }
                            }

                            var entries = zipFile.Entries.Count;
                            if (entries == 0)
                            {
                                File.Delete(zippath);
                            }
                        }
                    }

                    return true;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
            {
                MessageEvent?.Invoke(this, new MessageEventArgs(string.Format(Resources.PluginUpdateCheck_Uninstall_Failed_to_uninstall_the_selected_plugin_Reason, Environment.NewLine, ex.Message), Resources.PluginUpdateCheck_CheckForUpdates_Error, ErrorLevel.Error));
            }
#pragma warning restore CA1031 // Do not catch general exception types

            return false;
        }

        private protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    if (WebClient != null && Environment.HasShutdownStarted)
                    {
                        WebClient.Dispose();
                        WebClient = null;
                    }
                }

                this.disposedValue = true;
            }
        }
    }
}
