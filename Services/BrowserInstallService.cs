using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BlockUpdateWindowsDefender.Services
{
    public class BrowserInstallService
    {
        private readonly Dictionary<string, BrowserDefinition> _definitions;

        public BrowserInstallService()
        {
            _definitions = new Dictionary<string, BrowserDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Chrome",
                    new BrowserDefinition
                    {
                        Name = "Chrome",
                        FileName = "chrome_installer.exe",
                        Url63 = "https://files.cloudmini.net/ChromeSetup.exe",
                        Url10 = "https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi",
                        Fallback = "https://archive.org/download/browser_02.05.2022/Browser/ChromeSetup.exe",
                        VerificationPaths = new[]
                        {
                            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                        }
                    }
                },
                {
                    "Firefox",
                    new BrowserDefinition
                    {
                        Name = "Firefox",
                        FileName = "firefox_installer.exe",
                        Url63 = "https://download.mozilla.org/?product=firefox-esr115-latest-ssl&os=win64&lang=en-US",
                        Url10 = "https://download.mozilla.org/?product=firefox-latest&os=win64&lang=en-US",
                        Fallback = "https://files.cloudmini.net/FirefoxSetup.exe",
                        VerificationPaths = new[]
                        {
                            @"C:\Program Files\Mozilla Firefox\firefox.exe",
                            @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
                        }
                    }
                },
                {
                    "Edge",
                    new BrowserDefinition
                    {
                        Name = "Edge",
                        FileName = "edge_installer.exe",
                        Url63 = "https://files.cloudmini.net/MicrosoftEdgeSetup.exe",
                        Url10 = "https://c2rsetup.officeapps.live.com/c2r/downloadEdge.aspx?ProductreleaseID=Edge&platform=Default&version=Edge&source=EdgeStablePage&Channel=Stable&language=en",
                        Fallback = "https://files.cloudmini.net/MicrosoftEdgeSetup.exe",
                        VerificationPaths = new[]
                        {
                            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
                        }
                    }
                },
                {
                    "Brave",
                    new BrowserDefinition
                    {
                        Name = "Brave",
                        FileName = "brave_installer.exe",
                        Url63 = "https://github.com/brave/brave-browser/releases/download/v1.43.93/BraveBrowserStandaloneSilentSetup.exe",
                        Url10 = "https://laptop-updates.brave.com/latest/winx64",
                        Fallback = "https://files.cloudmini.net/BraveBrowserSetup.exe",
                        VerificationPaths = new[]
                        {
                            @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                            @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe"
                        }
                    }
                },
                {
                    "Opera",
                    new BrowserDefinition
                    {
                        Name = "Opera",
                        FileName = "opera_installer.exe",
                        Url63 = "https://download.opera.com/download/get/?id=63649&nothanks=yes&sub=marine&utm_tryagain=yes",
                        Url10 = "https://download.opera.com/download/get/?id=74098&nothanks=yes&sub=marine&utm_tryagain=yes",
                        Fallback = "https://files.cloudmini.net/Opera_10.exe",
                        Fallback63 = "https://files.cloudmini.net/Opera_6.3.exe"
                    }
                },
                {
                    "Centbrowser",
                    new BrowserDefinition
                    {
                        Name = "Centbrowser",
                        FileName = "centbrowser_installer.exe",
                        Url10 = "https://static.centbrowser.com/win_stable/5.2.1168.83/centbrowser_5.2.1168.83_x64.exe",
                        Fallback = "https://files.cloudmini.net/CentbrowserSetup.exe"
                    }
                }
            };
        }

        public Task<string> DetectWindowsVersionAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        if (key != null)
                        {
                            var major = key.GetValue("CurrentMajorVersionNumber");
                            var minor = key.GetValue("CurrentMinorVersionNumber");
                            if (major is int && minor is int)
                            {
                                return $"{major}.{minor}";
                            }

                            var currentVersion = key.GetValue("CurrentVersion") as string;
                            if (!string.IsNullOrWhiteSpace(currentVersion))
                            {
                                return currentVersion.StartsWith("6.3", StringComparison.OrdinalIgnoreCase)
                                    ? "6.3"
                                    : "10.0";
                            }
                        }
                    }
                }
                catch
                {
                }

                return Environment.OSVersion.Version.Major >= 10 ? "10.0" : "6.3";
            });
        }

        public async Task InstallBrowsersAsync(IEnumerable<string> browserNames, bool silentInstall, Action<string> log)
        {
            var browserList = browserNames?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            if (!browserList.Any())
            {
                log("No browser selected for installation.");
                return;
            }

            var windowsVersion = await DetectWindowsVersionAsync();
            log($"Browser install profile: Windows {windowsVersion}");

            foreach (var browserName in browserList)
            {
                BrowserDefinition definition;
                if (!_definitions.TryGetValue(browserName, out definition))
                {
                    log($"Unsupported browser: {browserName}");
                    continue;
                }

                await InstallBrowserAsync(definition, windowsVersion, silentInstall, log);
            }
        }

        private async Task InstallBrowserAsync(BrowserDefinition definition, string windowsVersion, bool silentInstall, Action<string> log)
        {
            var tempFileBasePath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(definition.FileName));
            var tempFilePath = GetDownloadPath(definition, null, tempFileBasePath);
            var urlsToTry = GetUrlsToTry(definition, windowsVersion).ToList();

            try
            {
                log($"Preparing {definition.Name} installation...");
                log($"Downloading {definition.Name}...");

                var downloaded = false;
                foreach (var url in urlsToTry)
                {
                    tempFilePath = GetDownloadPath(definition, url, tempFileBasePath);
                    log($"Trying URL: {url}");
                    downloaded = await TryDownloadAsync(url, tempFilePath, log);
                    if (downloaded)
                    {
                        break;
                    }
                }

                if (!downloaded)
                {
                    log($"Failed to download {definition.Name} from all configured URLs.");
                    return;
                }

                if (definition.Name.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
                {
                    tempFilePath = EnsureChromeInstallerExtension(tempFilePath, log);
                }

                log($"Starting {definition.Name} installation...");

                var installSuccess = definition.Name.Equals("Chrome", StringComparison.OrdinalIgnoreCase)
                    ? await InstallChromeAsync(tempFilePath, definition, log)
                    : await InstallGenericBrowserAsync(definition, tempFilePath, windowsVersion, silentInstall, log);

                log(installSuccess
                    ? $"{definition.Name} installation completed."
                    : $"{definition.Name} installation failed.");
            }
            finally
            {
                TryDeleteFile(tempFilePath);
                TryDeleteFile(Path.ChangeExtension(tempFileBasePath, ".exe"));
                TryDeleteFile(Path.ChangeExtension(tempFileBasePath, ".msi"));
            }
        }

        private IEnumerable<string> GetUrlsToTry(BrowserDefinition definition, string windowsVersion)
        {
            if (windowsVersion == "6.3" && !string.IsNullOrWhiteSpace(definition.Url63))
            {
                yield return definition.Url63;
            }

            if (windowsVersion != "6.3" && !string.IsNullOrWhiteSpace(definition.Url10))
            {
                yield return definition.Url10;
            }

            if (windowsVersion == "6.3" && !string.IsNullOrWhiteSpace(definition.Fallback63))
            {
                yield return definition.Fallback63;
            }

            if (!string.IsNullOrWhiteSpace(definition.Fallback))
            {
                yield return definition.Fallback;
            }
        }

        private async Task<bool> TryDownloadAsync(string url, string filePath, Action<string> log)
        {
            return await Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] =
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                        client.Headers[HttpRequestHeader.Accept] =
                            "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
                        client.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.5";

                        client.DownloadFile(url, filePath);
                    }

                    if (!File.Exists(filePath))
                    {
                        return false;
                    }

                    var size = new FileInfo(filePath).Length;
                    log($"{Path.GetFileName(filePath)} downloaded ({size:N0} bytes).");
                    return size > 0;
                }
                catch (Exception ex)
                {
                    log($"Download failed: {ex.Message}");
                    TryDeleteFile(filePath);
                    return false;
                }
            });
        }

        private async Task<bool> InstallChromeAsync(string installerPath, BrowserDefinition definition, Action<string> log)
        {
            var methods = new List<InstallMethod>();
            var isMsi = installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            if (isMsi)
            {
                methods.Add(new InstallMethod
                {
                    Label = "msiexec /qn",
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\" /qn /norestart",
                    TimeoutMs = 300000
                });
                methods.Add(new InstallMethod
                {
                    Label = "msiexec /passive",
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\" /passive /norestart",
                    TimeoutMs = 300000
                });
            }
            else
            {
                methods.Add(new InstallMethod
                {
                    Label = "Chrome silent install",
                    FileName = installerPath,
                    Arguments = "/silent /install",
                    TimeoutMs = 300000
                });
                methods.Add(new InstallMethod
                {
                    Label = "Chrome interactive install",
                    FileName = installerPath,
                    Arguments = string.Empty,
                    TimeoutMs = 300000
                });
            }

            foreach (var method in methods)
            {
                log($"Trying {method.Label}...");
                var result = await ExecuteInstallerAsync(method.FileName, method.Arguments, method.TimeoutMs);
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    log($"{method.Label} could not start: {result.ErrorMessage}");
                    continue;
                }

                if (result.TimedOut)
                {
                    log($"{method.Label} timed out.");
                    continue;
                }

                if (result.ExitCode == 0)
                {
                    await Task.Delay(3000);
                    if (IsInstalled(definition))
                    {
                        log($"{method.Label} succeeded.");
                        return true;
                    }

                    log($"{method.Label} returned success, but Chrome was not detected.");
                    continue;
                }

                log($"{method.Label} failed with exit code {result.ExitCode}.");
            }

            return false;
        }

        private async Task<bool> InstallGenericBrowserAsync(BrowserDefinition definition, string installerPath, string windowsVersion, bool silentInstall, Action<string> log)
        {
            var arguments = GetInstallArguments(definition.Name, windowsVersion, silentInstall);
            var timeoutMs = definition.Name.Equals("Brave", StringComparison.OrdinalIgnoreCase) ? 600000 : 450000;

            if (windowsVersion == "6.3" &&
                (definition.Name.Equals("Edge", StringComparison.OrdinalIgnoreCase) ||
                 definition.Name.Equals("Brave", StringComparison.OrdinalIgnoreCase)))
            {
                log($"{definition.Name} on Windows Server 2012 R2 may require interactive installation.");
            }

            var result = await ExecuteInstallerAsync(installerPath, arguments, timeoutMs);
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                log($"{definition.Name} installer could not start: {result.ErrorMessage}");
                return false;
            }

            if (result.TimedOut)
            {
                log($"{definition.Name} installer timed out.");
                return false;
            }

            if (result.ExitCode != 0)
            {
                log($"{definition.Name} installer returned exit code {result.ExitCode}.");
                return false;
            }

            if (definition.VerificationPaths != null && definition.VerificationPaths.Any())
            {
                await Task.Delay(3000);
                return IsInstalled(definition);
            }

            return true;
        }

        private static string GetInstallArguments(string browserName, string windowsVersion, bool silentInstall)
        {
            if (!silentInstall)
            {
                return string.Empty;
            }

            switch (browserName)
            {
                case "Firefox":
                    return "-ms";
                case "Edge":
                    return windowsVersion == "6.3" ? string.Empty : "/silent /install";
                case "Opera":
                    return "--silent --launchopera=0";
                case "Brave":
                    return string.Empty;
                case "Centbrowser":
                    return "--cb-auto-update --do-not-launch-chrome --system-level";
                default:
                    return "/S";
            }
        }

        private static string GetDownloadPath(BrowserDefinition definition, string url, string tempFileBasePath)
        {
            var extension = Path.GetExtension(definition.FileName);

            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var uri = new Uri(url);
                    var urlExtension = Path.GetExtension(uri.AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(urlExtension) &&
                        (string.Equals(urlExtension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(urlExtension, ".msi", StringComparison.OrdinalIgnoreCase)))
                    {
                        extension = urlExtension;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".exe";
            }

            return tempFileBasePath + extension;
        }

        private static string EnsureChromeInstallerExtension(string filePath, Action<string> log)
        {
            if (filePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                return filePath;
            }

            try
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var header = new byte[8];
                            var bytesRead = stream.Read(header, 0, header.Length);
                            if (bytesRead == 8 &&
                                header[0] == 0xD0 &&
                                header[1] == 0xCF &&
                                header[2] == 0x11 &&
                                header[3] == 0xE0 &&
                                header[4] == 0xA1 &&
                                header[5] == 0xB1 &&
                                header[6] == 0x1A &&
                                header[7] == 0xE1 &&
                                filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                var newPath = Path.ChangeExtension(filePath, ".msi");
                                TryDeleteFile(newPath);
                                File.Move(filePath, newPath);
                                log($"Detected MSI payload. Renamed installer to {Path.GetFileName(newPath)}.");
                                return newPath;
                            }

                            return filePath;
                        }
                    }
                    catch (IOException)
                    {
                        if (attempt == 4)
                        {
                            throw;
                        }

                        System.Threading.Thread.Sleep(250);
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Could not inspect Chrome installer header: {ex.Message}");
            }

            return filePath;
        }

        private static bool IsInstalled(BrowserDefinition definition)
        {
            return definition.VerificationPaths != null &&
                   definition.VerificationPaths.Any(File.Exists);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static Task<ProcessExecutionResult> ExecuteInstallerAsync(string fileName, string arguments, int timeoutMs)
        {
            return Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments ?? string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = new Process { StartInfo = startInfo })
                    {
                        process.Start();

                        if (!process.WaitForExit(timeoutMs))
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                            }

                            return new ProcessExecutionResult { ExitCode = -1, TimedOut = true };
                        }

                        return new ProcessExecutionResult
                        {
                            ExitCode = process.ExitCode,
                            TimedOut = false
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new ProcessExecutionResult
                    {
                        ExitCode = -2,
                        TimedOut = false,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        private class BrowserDefinition
        {
            public string Name { get; set; }
            public string FileName { get; set; }
            public string Url63 { get; set; }
            public string Url10 { get; set; }
            public string Fallback { get; set; }
            public string Fallback63 { get; set; }
            public string[] VerificationPaths { get; set; }
        }

        private class InstallMethod
        {
            public string Label { get; set; }
            public string FileName { get; set; }
            public string Arguments { get; set; }
            public int TimeoutMs { get; set; }
        }

        private class ProcessExecutionResult
        {
            public int ExitCode { get; set; }
            public bool TimedOut { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}
