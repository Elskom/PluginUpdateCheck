﻿// Copyright (c) 2018, Els_kom org.
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
    using System.Net;
    using System.Windows.Forms;
    using System.Xml.Linq;

    /// <summary>
    /// A generic plugin update checker.
    /// </summary>
    public class PluginUpdateCheck : IDisposable
    {
        private bool disposedValue = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginUpdateCheck"/> class.
        ///</summary>
        public PluginUpdateCheck()
        {
        }

        internal static WebClient WebClient { get; private protected set; }

        /// <summary>
        /// Gets or sets the notification icon to use in all instances of this class.
        /// </summary>
        public static NotifyIcon NotifyIcon { get; set; } = null;

        /// <summary>
        /// Gets the plugin urls used in all instances.
        /// </summary>
        public static string[] PluginUrls { get; private protected set; }

        /// <summary>
        /// Gets if there is any pending updates and displays a message.
        /// </summary>
        public DialogResult ShowMessage
            => !this.InstalledVersion.Equals(this.CurrentVersion) && !string.IsNullOrEmpty(this.InstalledVersion)
                ? MessageManager.ShowInfo(
                    $"Update {this.CurrentVersion} for plugin {this.PluginName} is availible.",
                    "New plugin update.",
                    NotifyIcon,
                    Convert.ToBoolean(Convert.ToInt32(SettingsFile.Settingsxml?.TryRead("UseNotifications") != string.Empty ? SettingsFile.Settingsxml?.TryRead("UseNotifications") : "0")))
                : DialogResult.OK;

        /// <summary>
        /// </summary>
        public string PluginName { get; private protected set; }

        /// <summary>
        /// </summary>
        public string CurrentVersion { get; private protected set; }

        /// <summary>
        /// </summary>
        public string InstalledVersion { get; private protected set; }

        /// <summary>
        /// Gets the url to download the files to the plugin from.
        /// </summary>
        public string DownloadUrl { get; private protected set; }

        /// <summary>
        /// Gets the files to the plugin to download.
        /// </summary>
        public string[] DownloadFiles { get; private protected set; }

        /// <summary>
        /// Cleans up the resources used by <see cref="PluginUpdateCheck"/>.
        /// </summary>
        public void Dispose() => this.Dispose(true);

        /// <summary>
        /// Checks for plugin updates from the provided plugin source urls.
        /// </summary>
        /// <param name="pluginURLs">The repository urls to the plugins.</param>
        // catches the plugin urls and uses that cache to detect added urls, and only appends those to the list.
        public static List<PluginUpdateCheck> CheckForUpdates(string[] pluginURLs)
        {
            var pluginUpdateChecks = new List<PluginUpdateCheck>();

            // fixup the github urls (if needed).
            for (var i = 0; i < pluginURLs.Length; i++)
            {
                pluginURLs[i] = pluginURLs[i].Replace(
                    "https://github.com/",
                    "https://raw.githubusercontent.com/") + (
                    pluginURLs[i].EndsWith("/") ? "master/plugins.xml" : "/master/plugins.xml");
            }

            if (WebClient == null)
            {
                WebClient = new WebClient();
            }

            if (PluginUrls == null)
            {
                PluginUrls = new List<string>().ToArray();
            }

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
                            foreach (var callbackplugin in ExecutionManager.Callbackplugins)
                            {
                                if (pluginName.Equals(callbackplugin.GetType().Namespace))
                                {
                                    found = true;
                                    var installedVersion = callbackplugin.GetType().Assembly.GetName().Version.ToString();
                                    var pluginUpdateCheck = new PluginUpdateCheck
                                    {
                                        CurrentVersion = currentVersion,
                                        InstalledVersion = installedVersion,
                                        PluginName = pluginName,
                                        DownloadUrl = $"{element.Attribute("DownloadUrl").Value}/{currentVersion}/",
                                        DownloadFiles = element.Descendants("DownloadFile").Select(y => y.Attribute("Name").Value).ToArray(),
                                    };
                                    pluginUpdateChecks.Add(pluginUpdateCheck);
                                }
                            }

                            foreach (var komplugin in KOMManager.Komplugins)
                            {
                                if (pluginName.Equals(komplugin.GetType().Namespace))
                                {
                                    found = true;
                                    var installedVersion = komplugin.GetType().Assembly.GetName().Version.ToString();
                                    var pluginUpdateCheck = new PluginUpdateCheck
                                    {
                                        CurrentVersion = currentVersion,
                                        InstalledVersion = installedVersion,
                                        PluginName = pluginName,
                                        DownloadUrl = $"{element.Attribute("DownloadUrl").Value}/{currentVersion}/",
                                        DownloadFiles = element.Descendants("DownloadFile").Select(y => y.Attribute("Name").Value).ToArray(),
                                    };
                                    pluginUpdateChecks.Add(pluginUpdateCheck);
                                }
                            }

                            // list these as well so the plugin updater form works.
                            if (!found)
                            {
                                var pluginUpdateCheck = new PluginUpdateCheck
                                {
                                    CurrentVersion = currentVersion,
                                    InstalledVersion = string.Empty,
                                    PluginName = pluginName,
                                    DownloadUrl = $"{element.Attribute("DownloadUrl").Value}/{currentVersion}/",
                                    DownloadFiles = element.Descendants("DownloadFile").Select(y => y.Attribute("Name").Value).ToArray(),
                                };
                                pluginUpdateChecks.Add(pluginUpdateCheck);
                            }
                        }
                    }
                    catch (WebException ex)
                    {
                        MessageManager.ShowError(
                            $"Failed to download the plugins sources list.{Environment.NewLine}Reason: {ex.Message}",
                            "Error!",
                            NotifyIcon,
                            Convert.ToBoolean(Convert.ToInt32(SettingsFile.Settingsxml?.TryRead("UseNotifications") != string.Empty ? SettingsFile.Settingsxml?.TryRead("UseNotifications") : "0")));
                    }
                }

                // append the string to the cache.
                PluginUrls = PluginUrls.Append(pluginURL).ToArray();
            }

            return pluginUpdateChecks;
        }

        /// <summary>
        /// Installs the files to the plugin pointed to by this instance.
        /// </summary>
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
                        var zipFile = ZipFile.Open(zippath, ZipArchiveMode.Update);
                        foreach (var entry in zipFile.Entries)
                        {
                            if (entry.FullName.Equals(downloadFile))
                            {
                                entry.Delete();
                            }
                        }

                        zipFile.CreateEntryFromFile(path, downloadFile);
                        File.Delete(path);
                        zipFile.Dispose();
                    }

                    return true;
                }
                catch (WebException ex)
                {
                    MessageManager.ShowError(
                        $"Failed to install the selected plugin.{Environment.NewLine}Reason: {ex.Message}",
                        "Error!",
                        NotifyIcon,
                        Convert.ToBoolean(Convert.ToInt32(SettingsFile.Settingsxml?.TryRead("UseNotifications") != string.Empty ? SettingsFile.Settingsxml?.TryRead("UseNotifications") : "0")));
                }
            }

            return false;
        }

        /// <summary>
        /// Uninstalls the files to the plugin pointed to by this instance.
        /// </summary>
        /// <returns>A bool indicating if anything changed.</returns>
        public bool Uninstall(bool saveToZip)
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
                    var zipFile = ZipFile.Open(zippath, ZipArchiveMode.Update);
                    foreach (var entry in zipFile.Entries)
                    {
                        if (entry.FullName.Equals(downloadFile))
                        {
                            entry.Delete();
                        }
                    }

                    var entries = zipFile.Entries.Count;
                    zipFile.Dispose();
                    if (entries == 0)
                    {
                        File.Delete(zippath);
                    }
                }

                return true;
            }

            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    if (WebClient != null)
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