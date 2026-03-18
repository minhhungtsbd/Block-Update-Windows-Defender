using System;
using System.Collections.Generic;
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
        private const string UpdateManifestUrl = "https://raw.githubusercontent.com/minhhungtsbd/Block-Update-Windows-Defender/main/release/latest.json";
        private const string ReleaseApiUrl = "https://api.github.com/repos/minhhungtsbd/Block-Update-Windows-Defender/releases/latest";
        private const string ReleaseFolderApiUrl = "https://api.github.com/repos/minhhungtsbd/Block-Update-Windows-Defender/contents/release";
        private const string ReleaseFolderPageUrl = "https://github.com/minhhungtsbd/Block-Update-Windows-Defender/tree/main/release";
        private const string RawReleaseBaseUrl = "https://raw.githubusercontent.com/minhhungtsbd/Block-Update-Windows-Defender/main/release/";
        private const string AppExeName = "BlockUpdateWindowsDefender.exe";
        private const string ReleaseZipFilePrefix = "Block-Update-Windows-Defender-v";

        public Task<AppUpdateCheckResult> CheckForUpdateAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                    var candidate = default(UpdateCandidate);
                    var errors = new List<string>();

                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = "BlockUpdateWindowsDefender/1.0";
                        client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";

                        // Order: raw manifest -> releases API -> contents API -> HTML folder scrape
                        // so the updater keeps working even when GitHub API responds 403/404.
                        candidate = TryGetCandidate(() => GetReleaseCandidateFromManifest(client), errors);
                        candidate = candidate ?? TryGetCandidate(() => GetReleaseCandidateFromReleasesApi(client), errors);
                        candidate = candidate ?? TryGetCandidate(() => GetReleaseCandidateFromReleaseFolder(client), errors);
                        candidate = candidate ?? TryGetCandidate(() => GetReleaseCandidateFromReleaseFolderPage(client), errors);

                        if (candidate == null)
                        {
                            return new AppUpdateCheckResult
                            {
                                IsSuccess = false,
                                CurrentVersionText = currentVersion.ToString(),
                                ErrorMessage = BuildCandidateErrorMessage(errors)
                            };
                        }

                        var isUpdateAvailable = candidate.Version > currentVersion;

                        return new AppUpdateCheckResult
                        {
                            IsSuccess = true,
                            IsUpdateAvailable = isUpdateAvailable,
                            CurrentVersionText = currentVersion.ToString(),
                            LatestVersionText = candidate.VersionText,
                            DownloadUrl = candidate.DownloadUrl
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

        private static UpdateCandidate GetReleaseCandidateFromReleasesApi(WebClient client)
        {
            var json = client.DownloadString(ReleaseApiUrl);
            var tag = ExtractJsonValue(json, "tag_name");
            var downloadUrl = ExtractZipDownloadUrl(json);
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            return new UpdateCandidate
            {
                VersionText = tag,
                Version = ParseVersion(tag),
                DownloadUrl = downloadUrl
            };
        }

        private static UpdateCandidate GetReleaseCandidateFromManifest(WebClient client)
        {
            var json = client.DownloadString(UpdateManifestUrl);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var versionText = ExtractJsonValue(json, "version");
            if (string.IsNullOrWhiteSpace(versionText))
            {
                versionText = ExtractJsonValue(json, "latestVersion");
            }

            var downloadUrl = ExtractJsonValue(json, "downloadUrl");
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return null;
            }

            var parsedVersion = ParseVersion(versionText);
            if (parsedVersion.Major == 0 && parsedVersion.Minor == 0)
            {
                return null;
            }

            return new UpdateCandidate
            {
                Version = parsedVersion,
                VersionText = "v" + parsedVersion,
                DownloadUrl = downloadUrl
            };
        }

        private static UpdateCandidate GetReleaseCandidateFromReleaseFolder(WebClient client)
        {
            var json = client.DownloadString(ReleaseFolderApiUrl);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            UpdateCandidate selected = null;
            var itemMatches = Regex.Matches(
                json,
                "\"name\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]*?\"download_url\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);

            foreach (Match item in itemMatches)
            {
                if (!item.Success || item.Groups.Count < 3)
                {
                    continue;
                }

                var fileName = item.Groups[1].Value.Trim();
                var downloadUrl = item.Groups[2].Value.Trim().Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                var version = ParseVersionFromReleaseFileName(fileName);
                if (version == null)
                {
                    continue;
                }

                if (selected == null || version > selected.Version)
                {
                    selected = new UpdateCandidate
                    {
                        Version = version,
                        VersionText = "v" + version,
                        DownloadUrl = downloadUrl
                    };
                }
            }

            return selected;
        }

        private static UpdateCandidate GetReleaseCandidateFromReleaseFolderPage(WebClient client)
        {
            var html = client.DownloadString(ReleaseFolderPageUrl);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            UpdateCandidate selected = null;
            var matches = Regex.Matches(html, "Block-Update-Windows-Defender-v(\\d+(?:\\.\\d+){1,3})\\.zip", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                var versionText = match.Groups[1].Value.Trim();
                var version = ParseVersion(versionText);
                if (version.Major == 0 && version.Minor == 0)
                {
                    continue;
                }

                if (selected == null || version > selected.Version)
                {
                    var fileName = ReleaseZipFilePrefix + versionText + ".zip";
                    selected = new UpdateCandidate
                    {
                        Version = version,
                        VersionText = "v" + version,
                        DownloadUrl = RawReleaseBaseUrl + fileName
                    };
                }
            }

            return selected;
        }

        private static UpdateCandidate TryGetCandidate(Func<UpdateCandidate> candidateProvider, ICollection<string> errors)
        {
            try
            {
                return candidateProvider();
            }
            catch (Exception ex)
            {
                if (errors != null && !string.IsNullOrWhiteSpace(ex.Message))
                {
                    errors.Add(ex.Message);
                }

                return null;
            }
        }

        private static string BuildCandidateErrorMessage(IList<string> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return "Could not find a valid update package.";
            }

            var first = errors.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
            return string.IsNullOrWhiteSpace(first)
                ? "Could not find a valid update package."
                : first;
        }

        private static Version ParseVersionFromReleaseFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (!fileName.StartsWith(ReleaseZipFilePrefix, StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var versionText = fileName.Substring(
                ReleaseZipFilePrefix.Length,
                fileName.Length - ReleaseZipFilePrefix.Length - 4);

            var parsed = ParseVersion(versionText);
            return parsed.Major == 0 && parsed.Minor == 0 ? null : parsed;
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

        private class UpdateCandidate
        {
            public Version Version { get; set; }
            public string VersionText { get; set; }
            public string DownloadUrl { get; set; }
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
