using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlockUpdateWindowsDefender.Services
{
    public class AppUpdateService
    {
        private const string ReleaseApiUrl = "https://api.github.com/repos/minhhungtsbd/Block-Update-Windows-Defender/releases/latest";
        private const string AppExeName = "BlockUpdateWindowsDefender.exe";

        public Task<AppUpdateCheckResult> CheckForUpdateAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "BlockUpdateWindowsDefender/1.0";
                        var json = client.DownloadString(ReleaseApiUrl);

                        var tag = ExtractJsonValue(json, "tag_name");
                        var downloadUrl = ExtractZipDownloadUrl(json);

                        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(downloadUrl))
                        {
                            return new AppUpdateCheckResult
                            {
                                IsSuccess = false,
                                ErrorMessage = "Could not read release metadata from GitHub."
                            };
                        }

                        var latestVersion = ParseVersion(tag);
                        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                        var isUpdateAvailable = latestVersion > currentVersion;

                        return new AppUpdateCheckResult
                        {
                            IsSuccess = true,
                            IsUpdateAvailable = isUpdateAvailable,
                            CurrentVersionText = currentVersion.ToString(),
                            LatestVersionText = tag,
                            DownloadUrl = downloadUrl
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new AppUpdateCheckResult
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        public Task<AppUpdateApplyResult> ApplyUpdateAsync(string downloadUrl)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        return new AppUpdateApplyResult
                        {
                            IsSuccess = false,
                            ErrorMessage = "Invalid update download URL."
                        };
                    }

                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    var tempRoot = Path.Combine(Path.GetTempPath(), "BlockUpdateWindowsDefender", "update_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Directory.CreateDirectory(tempRoot);

                    var zipPath = Path.Combine(tempRoot, "update.zip");
                    var extractPath = Path.Combine(tempRoot, "extract");
                    Directory.CreateDirectory(extractPath);

                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "BlockUpdateWindowsDefender/1.0";
                        client.DownloadFile(downloadUrl, zipPath);
                    }

                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                    var newExePath = Directory.GetFiles(extractPath, AppExeName, SearchOption.AllDirectories).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(newExePath))
                    {
                        return new AppUpdateApplyResult
                        {
                            IsSuccess = false,
                            ErrorMessage = "Downloaded package does not contain app executable."
                        };
                    }

                    var sourceDir = Path.GetDirectoryName(newExePath);
                    var targetDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                    var scriptPath = Path.Combine(tempRoot, "apply_update.cmd");

                    var script = BuildUpdateScript(sourceDir, targetDir, AppExeName);
                    File.WriteAllText(scriptPath, script);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/C \"" + scriptPath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Process.Start(psi);
                    return new AppUpdateApplyResult { IsSuccess = true };
                }
                catch (Exception ex)
                {
                    return new AppUpdateApplyResult
                    {
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        public string GetCurrentVersionText()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private static string BuildUpdateScript(string sourceDir, string targetDir, string exeName)
        {
            return "@echo off\r\n" +
                   "setlocal\r\n" +
                   "set \"SOURCE=" + EscapeForBatch(sourceDir) + "\"\r\n" +
                   "set \"TARGET=" + EscapeForBatch(targetDir) + "\"\r\n" +
                   "set \"APP=" + EscapeForBatch(exeName) + "\"\r\n" +
                   "for /L %%i in (1,1,60) do (\r\n" +
                   "  tasklist /FI \"IMAGENAME eq %APP%\" 2>NUL | find /I \"%APP%\" >NUL\r\n" +
                   "  if errorlevel 1 goto copyfiles\r\n" +
                   "  timeout /t 1 /nobreak >NUL\r\n" +
                   ")\r\n" +
                   ":copyfiles\r\n" +
                   "robocopy \"%SOURCE%\" \"%TARGET%\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NC /NS >NUL\r\n" +
                   "start \"\" \"%TARGET%\\%APP%\"\r\n" +
                   "endlocal\r\n";
        }

        private static string EscapeForBatch(string value)
        {
            return (value ?? string.Empty).Replace("^", "^^").Replace("&", "^&");
        }

        private static string ExtractZipDownloadUrl(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var matches = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                var url = match.Groups[1].Value.Replace("\\/", "/");
                if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }

            return null;
        }

        private static string ExtractJsonValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups.Count < 2)
            {
                return null;
            }

            return match.Groups[1].Value.Replace("\\/", "/");
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return new Version(0, 0);
            }

            var cleaned = tag.Trim().TrimStart('v', 'V');
            Version parsed;
            if (Version.TryParse(cleaned, out parsed))
            {
                return NormalizeVersion(parsed);
            }

            var match = Regex.Match(cleaned, "(\\d+\\.\\d+(?:\\.\\d+){0,2})");
            if (match.Success && Version.TryParse(match.Groups[1].Value, out parsed))
            {
                return NormalizeVersion(parsed);
            }

            return new Version(0, 0);
        }

        private static Version NormalizeVersion(Version version)
        {
            if (version == null)
            {
                return new Version(0, 0);
            }

            var build = version.Build < 0 ? 0 : version.Build;
            var revision = version.Revision < 0 ? 0 : version.Revision;
            return new Version(version.Major, version.Minor, build, revision);
        }
    }

    public class AppUpdateCheckResult
    {
        public bool IsSuccess { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersionText { get; set; }
        public string LatestVersionText { get; set; }
        public string DownloadUrl { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class AppUpdateApplyResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }
}
