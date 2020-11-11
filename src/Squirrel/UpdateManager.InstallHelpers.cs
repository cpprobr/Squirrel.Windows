using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using NuGet;
using Splat;
using System.Reflection;

namespace Squirrel
{
    public sealed partial class UpdateManager
    {
        internal class InstallHelperImpl : IEnableLogger
        {
            readonly string applicationName;
            readonly string rootAppDirectory;

            public InstallHelperImpl(string applicationName, string rootAppDirectory)
            {
                this.applicationName = applicationName;
                this.rootAppDirectory = rootAppDirectory;
            }

            const string currentVersionRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            const string uninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            public async Task<RegistryKey> CreateUninstallerRegistryEntry(string uninstallCmd, string quietSwitch)
            {
                var releaseContent = File.ReadAllText(Path.Combine(rootAppDirectory, "packages", "RELEASES"), Encoding.UTF8);
                var releases = ReleaseEntry.ParseReleaseFile(releaseContent);
                var latest = releases.Where(x => !x.IsDelta).OrderByDescending(x => x.Version).First();

                // Download the icon and PNG => ICO it. If this doesn't work, who cares
                var pkgPath = Path.Combine(rootAppDirectory, "packages", latest.Filename);
                var zp = new ZipPackage(pkgPath);

                // NB: Sometimes the Uninstall key doesn't exist
                using (var parentKey =
                    RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                        .CreateSubKey("Uninstall", RegistryKeyPermissionCheck.ReadWriteSubTree)) { ; }

                var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .CreateSubKey(uninstallRegSubKey + "\\" + applicationName, RegistryKeyPermissionCheck.ReadWriteSubTree);

                async Task DefineDisplayIcon(string inputFile, Stream inputStream)
                {
                    try
                    {
                        var outputFile = Path.Combine(rootAppDirectory, "app.ico");

                        using (var outputStream = new FileStream(outputFile, FileMode.Create))
                        {
                            if (inputFile.EndsWith("ico"))
                                await inputStream.CopyToAsync(outputStream);
                            else
                                IconHelper.ConvertToIcon(inputStream, outputStream, 64);
                        }

                        key.SetValue("DisplayIcon", outputFile, RegistryValueKind.String);
                    }
                    catch (Exception ex)
                    {
                        this.Log().InfoException("Couldn't write uninstall icon, don't care", ex);
                    }
                }

                if (zp.IconUrl != null) {
                    var targetPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

                    try {
                        using (var wc = Utility.CreateWebClient())
                        {
                            await wc.DownloadFileTaskAsync(zp.IconUrl, targetPng);
                        }

                        using (var inputStream = File.OpenRead(targetPng))
                        {
                            await DefineDisplayIcon(targetPng, inputStream);
                        }
                    } finally {
                        File.Delete(targetPng);
                    }
                }

                if (zp.Icon != null)
                {
                    var iconFile = zp.GetFiles().FirstOrDefault(x => x.Path == zp.Icon);

                    if (iconFile != null)
                    {
                        using (var inputStream = iconFile.GetStream())
                        {
                            await DefineDisplayIcon(iconFile.Path, inputStream);
                        }
                    }
                }

                var stringsToWrite = new[] {
                    new { Key = "DisplayName", Value = zp.Title ?? zp.Description ?? zp.Summary },
                    new { Key = "DisplayVersion", Value = zp.Version.ToString() },
                    new { Key = "InstallDate", Value = DateTime.Now.ToString("yyyyMMdd") },
                    new { Key = "InstallLocation", Value = rootAppDirectory },
                    new { Key = "Publisher", Value = String.Join(",", zp.Authors) },
                    new { Key = "QuietUninstallString", Value = String.Format("{0} {1}", uninstallCmd, quietSwitch) },
                    new { Key = "UninstallString", Value = uninstallCmd },
                    new { Key = "URLUpdateInfo", Value = zp.ProjectUrl != null ? zp.ProjectUrl.ToString() : "", }
                };

                var dwordsToWrite = new[] {
                    new { Key = "EstimatedSize", Value = (int)((new FileInfo(pkgPath)).Length / 1024) },
                    new { Key = "NoModify", Value = 1 },
                    new { Key = "NoRepair", Value = 1 },
                    new { Key = "Language", Value = 0x0409 },
                };

                foreach (var kvp in stringsToWrite) {
                    key.SetValue(kvp.Key, kvp.Value, RegistryValueKind.String);
                }
                foreach (var kvp in dwordsToWrite) {
                    key.SetValue(kvp.Key, kvp.Value, RegistryValueKind.DWord);
                }

                return key;
            }

            public void KillAllProcessesBelongingToPackage()
            {
                var ourExe = Assembly.GetEntryAssembly();
                var ourExePath = ourExe != null ? ourExe.Location : null;

                UnsafeUtility.EnumerateProcesses()
                    .Where(x => {
                        // Processes we can't query will have an empty process name, we can't kill them
                        // anyways
                        if (String.IsNullOrWhiteSpace(x.Item1)) return false;

                        // Files that aren't in our root app directory are untouched
                        if (!x.Item1.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) return false;

                        // Never kill our own EXE
                        if (ourExePath != null && x.Item1.Equals(ourExePath, StringComparison.OrdinalIgnoreCase)) return false;

                        var name = Path.GetFileName(x.Item1).ToLowerInvariant();
                        if (name == "squirrel.exe" || name == "update.exe") return false;

                        return true;
                    })
                    .ForEach(x => {
                        try {
                            this.WarnIfThrows(() => Process.GetProcessById(x.Item2).Kill());
                        } catch {}
                    });
            }

            public Task<RegistryKey> CreateUninstallerRegistryEntry()
            {
                var updateDotExe = Path.Combine(rootAppDirectory, "Update.exe");
                return CreateUninstallerRegistryEntry(String.Format("\"{0}\" --uninstall", updateDotExe), "-s");
            }

            public void RemoveUninstallerRegistryEntry()
            {
                var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    .OpenSubKey(uninstallRegSubKey, true);
                key.DeleteSubKeyTree(applicationName);
            }
        }
    }
}
