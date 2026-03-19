using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using BlockUpdateWindowsDefender.Models;

namespace BlockUpdateWindowsDefender.Services
{
    public class RdpHistoryService
    {
        private static readonly Regex Ipv4AnyRegex = new Regex(@"(?<!\d)(\d{1,3}(?:\.\d{1,3}){3})(?!\d)", RegexOptions.Compiled);
        private readonly ProcessRunner _processRunner;

        public RdpHistoryService(ProcessRunner processRunner)
        {
            _processRunner = processRunner;
        }

        public async Task<RdpHistoryResult> GetRecentHistoryAsync(int days = 30, int maxEvents = 40)
        {
            var results = new List<RdpHistoryResult>
            {
                await TryGetFromPowerShellAsync(days, Math.Max(maxEvents * 3, 80)),
                await TryGetFromWevtutilXmlAsync(Math.Max(maxEvents * 4, 120)),
                await TryGetFromWevtutilTextAggregateAsync(Math.Max(maxEvents * 4, 120))
            };

            var successful = results.Where(item => item.IsSuccess).ToList();
            if (successful.Count > 0)
            {
                var mergedRecords = MergeRecords(successful, maxEvents);
                var mergedSource = string.Join(" + ",
                    successful
                        .Select(item => item.Source)
                        .Where(source => !string.IsNullOrWhiteSpace(source))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray());

                return new RdpHistoryResult
                {
                    IsSuccess = true,
                    Source = string.IsNullOrWhiteSpace(mergedSource) ? "RDP logs" : mergedSource,
                    Records = mergedRecords
                };
            }

            var errors = results
                .Where(item => !string.IsNullOrWhiteSpace(item.ErrorMessage))
                .Select(item => item.ErrorMessage.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new RdpHistoryResult
            {
                IsSuccess = false,
                Source = "PowerShell + wevtutil",
                ErrorMessage = errors.Length == 0
                    ? "Could not read RDP history from available logs."
                    : string.Join(" | ", errors)
            };
        }

        private async Task<RdpHistoryResult> TryGetFromPowerShellAsync(int days, int maxEvents)
        {
            var script = @"
$ErrorActionPreference='Stop'
$start=(Get-Date).AddDays(-" + days + @")
$max=" + maxEvents + @"
$events=Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4624; StartTime=$start} -MaxEvents 1200 |
ForEach-Object {
  try {
    $xml=[xml]$_.ToXml()
    $data=@{}
    foreach($d in $xml.Event.EventData.Data){
      if($d.Name){ $data[$d.Name]=$d.'#text' }
    }

    $lt=$data['LogonType']
    $ip=$data['IpAddress']
    if([string]::IsNullOrWhiteSpace($ip)){ $ip=$data['SourceNetworkAddress'] }
    if([string]::IsNullOrWhiteSpace($ip)){ $ip=$data['ClientAddress'] }

    if($lt -eq '10' -and -not [string]::IsNullOrWhiteSpace($ip) -and $ip -ne '-' -and $ip -ne '::1' -and $ip -ne '127.0.0.1'){
      $user=$data['TargetUserName']
      if([string]::IsNullOrWhiteSpace($user)){ $user='Unknown' }
      $host=$data['WorkstationName']
      if([string]::IsNullOrWhiteSpace($host)){ $host='-' }
      [PSCustomObject]@{
        Time=$_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')
        User=$user
        IP=$ip
        Host=$host
      }
    }
  } catch {}
} | Select-Object -First $max

if($events){
  $events | ForEach-Object { ""$($_.Time)|$($_.User)|$($_.IP)|$($_.Host)"" }
}";

            var result = await _processRunner.RunPowerShellAsync(script);
            if (!result.IsSuccess)
            {
                return new RdpHistoryResult
                {
                    IsSuccess = false,
                    Source = "PowerShell Get-WinEvent",
                    ErrorMessage = FirstError(result)
                };
            }

            var records = ParsePipeOutput(result.StandardOutput);
            return new RdpHistoryResult
            {
                IsSuccess = true,
                Source = "PowerShell Security 4624",
                Records = records
            };
        }

        private async Task<RdpHistoryResult> TryGetFromWevtutilXmlAsync(int maxEvents)
        {
            var result = await _processRunner.RunAsync(
                "wevtutil.exe",
                "qe Security /q:\"*[System[(EventID=4624)]]\" /f:xml /rd:true /c:240");

            if (!result.IsSuccess)
            {
                return new RdpHistoryResult
                {
                    IsSuccess = false,
                    Source = "wevtutil XML Security",
                    ErrorMessage = FirstError(result)
                };
            }

            try
            {
                var xml = (result.StandardOutput ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(xml))
                {
                    return new RdpHistoryResult
                    {
                        IsSuccess = true,
                        Source = "wevtutil XML Security",
                        Records = new List<RdpLoginRecord>()
                    };
                }

                xml = Regex.Replace(xml, @"<\?xml[^>]*\?>", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (!xml.StartsWith("<Events", StringComparison.OrdinalIgnoreCase))
                {
                    xml = "<Events>" + xml + "</Events>";
                }

                var document = XDocument.Parse(xml);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                var records = new List<RdpLoginRecord>();

                foreach (var eventNode in document.Descendants(ns + "Event"))
                {
                    var dataMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var dataNode in eventNode.Descendants(ns + "Data"))
                    {
                        var name = dataNode.Attribute("Name")?.Value;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        dataMap[name] = (dataNode.Value ?? string.Empty).Trim();
                    }

                    var logonType = ReadFirst(dataMap, "LogonType");
                    if (!string.Equals(logonType, "10", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var ipRaw = ReadFirst(dataMap, "IpAddress", "SourceNetworkAddress", "ClientAddress");
                    var ip = NormalizeRemoteIpv4(ipRaw);
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        continue;
                    }

                    var user = ReadFirst(dataMap, "TargetUserName", "AccountName");
                    if (string.IsNullOrWhiteSpace(user))
                    {
                        user = "Unknown";
                    }

                    var host = ReadFirst(dataMap, "WorkstationName", "ClientName");
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        host = "-";
                    }

                    var timeRaw = eventNode.Descendants(ns + "TimeCreated").FirstOrDefault()?.Attribute("SystemTime")?.Value;
                    var time = ParseEventTime(timeRaw);

                    records.Add(new RdpLoginRecord
                    {
                        Time = time == DateTime.MinValue ? "-" : time.ToString("yyyy-MM-dd HH:mm:ss"),
                        User = user,
                        IpAddress = ip,
                        Host = host
                    });
                }

                records = OrderAndTrim(records, maxEvents);

                return new RdpHistoryResult
                {
                    IsSuccess = true,
                    Source = "wevtutil XML Security",
                    Records = records
                };
            }
            catch (Exception ex)
            {
                return new RdpHistoryResult
                {
                    IsSuccess = false,
                    Source = "wevtutil XML Security",
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<RdpHistoryResult> TryGetFromWevtutilTextAggregateAsync(int maxEvents)
        {
            var queries = new List<WevtutilQuery>
            {
                new WevtutilQuery
                {
                    Source = "wevtutil text Security 4624",
                    Arguments = "qe Security /q:\"*[System[(EventID=4624)]]\" /f:text /rd:true /c:220",
                    RequireLogonType10 = true
                },
                new WevtutilQuery
                {
                    Source = "wevtutil text Security 4648",
                    Arguments = "qe Security /q:\"*[System[(EventID=4648)]]\" /f:text /rd:true /c:120",
                    RequireLogonType10 = false
                },
                new WevtutilQuery
                {
                    Source = "wevtutil text TS-RemoteConnectionManager 1149",
                    Arguments = "qe \"Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational\" /q:\"*[System[(EventID=1149)]]\" /f:text /rd:true /c:120",
                    RequireLogonType10 = false
                },
                new WevtutilQuery
                {
                    Source = "wevtutil text TS-LocalSessionManager 21/24",
                    Arguments = "qe \"Microsoft-Windows-TerminalServices-LocalSessionManager/Operational\" /q:\"*[System[(EventID=21 or EventID=24)]]\" /f:text /rd:true /c:120",
                    RequireLogonType10 = false
                }
            };

            var allRecords = new List<RdpLoginRecord>();
            var successfulSources = new List<string>();
            var errors = new List<string>();

            for (var i = 0; i < queries.Count; i++)
            {
                var query = queries[i];
                var result = await _processRunner.RunAsync("wevtutil.exe", query.Arguments);
                if (!result.IsSuccess)
                {
                    errors.Add(query.Source + ": " + FirstError(result));
                    continue;
                }

                successfulSources.Add(query.Source);
                var parsedRecords = ParseWevtutilTextEvents(result.StandardOutput, query.RequireLogonType10);
                if (parsedRecords.Count > 0)
                {
                    allRecords.AddRange(parsedRecords);
                }
            }

            var deduplicated = OrderAndTrim(DeduplicateRecords(allRecords), maxEvents);
            if (successfulSources.Count > 0)
            {
                return new RdpHistoryResult
                {
                    IsSuccess = true,
                    Source = string.Join(" + ", successfulSources.ToArray()),
                    Records = deduplicated
                };
            }

            return new RdpHistoryResult
            {
                IsSuccess = false,
                Source = "wevtutil text aggregate",
                ErrorMessage = errors.Count == 0
                    ? "Could not read wevtutil text output."
                    : string.Join(" | ", errors.ToArray())
            };
        }

        private static List<RdpLoginRecord> ParsePipeOutput(string output)
        {
            var records = new List<RdpLoginRecord>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return records;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|');
                if (parts.Length < 4)
                {
                    continue;
                }

                var normalizedIp = NormalizeRemoteIpv4(parts[2]);
                if (string.IsNullOrWhiteSpace(normalizedIp))
                {
                    continue;
                }

                records.Add(new RdpLoginRecord
                {
                    Time = parts[0].Trim(),
                    User = string.IsNullOrWhiteSpace(parts[1]) ? "Unknown" : parts[1].Trim(),
                    IpAddress = normalizedIp,
                    Host = string.IsNullOrWhiteSpace(parts[3]) ? "-" : parts[3].Trim()
                });
            }

            return records;
        }

        private static List<RdpLoginRecord> ParseWevtutilTextEvents(string output, bool requireLogonType10)
        {
            var records = new List<RdpLoginRecord>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return records;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var hasBlock = false;
            var currentTime = "-";
            var currentUser = "Unknown";
            var currentHost = "-";
            var currentIpRaw = string.Empty;
            var hasLogonType10 = !requireLogonType10;

            Action flushCurrent = () =>
            {
                if (!hasBlock)
                {
                    return;
                }

                if (requireLogonType10 && !hasLogonType10)
                {
                    return;
                }

                var ip = NormalizeRemoteIpv4(currentIpRaw);
                if (string.IsNullOrWhiteSpace(ip))
                {
                    return;
                }

                records.Add(new RdpLoginRecord
                {
                    Time = string.IsNullOrWhiteSpace(currentTime) ? "-" : currentTime,
                    User = string.IsNullOrWhiteSpace(currentUser) ? "Unknown" : currentUser.Trim(),
                    IpAddress = ip,
                    Host = string.IsNullOrWhiteSpace(currentHost) ? "-" : currentHost.Trim()
                });
            };

            for (var i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i] ?? string.Empty;
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("Event[", StringComparison.OrdinalIgnoreCase))
                {
                    flushCurrent();
                    hasBlock = true;
                    currentTime = "-";
                    currentUser = "Unknown";
                    currentHost = "-";
                    currentIpRaw = string.Empty;
                    hasLogonType10 = !requireLogonType10;
                    continue;
                }

                if (!hasBlock)
                {
                    hasBlock = true;
                }

                if (line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                {
                    var dateText = ExtractAfterFirstColon(line);
                    if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
                    {
                        currentTime = parsedDate.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else if (DateTime.TryParse(dateText, out parsedDate))
                    {
                        currentTime = parsedDate.ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    continue;
                }

                if (line.IndexOf("Logon Type:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("LogonType:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var value = ExtractAfterFirstColon(line);
                    hasLogonType10 = string.Equals(value, "10", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (line.IndexOf("Account Name:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("User:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var value = ExtractAfterFirstColon(line);
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !string.Equals(value, "-", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value, "ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase))
                    {
                        var slashIndex = value.LastIndexOf('\\');
                        currentUser = slashIndex >= 0 ? value.Substring(slashIndex + 1).Trim() : value.Trim();
                    }
                }

                if (line.IndexOf("Workstation Name:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Workstation:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Client Name:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var value = ExtractAfterFirstColon(line);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        currentHost = value.Trim();
                    }
                }

                var ipFromLine = NormalizeRemoteIpv4(line);
                if (!string.IsNullOrWhiteSpace(ipFromLine))
                {
                    currentIpRaw = ipFromLine;
                }
            }

            flushCurrent();
            return records;
        }

        private static List<RdpLoginRecord> MergeRecords(List<RdpHistoryResult> successfulResults, int maxEvents)
        {
            var all = new List<RdpLoginRecord>();
            for (var i = 0; i < successfulResults.Count; i++)
            {
                if (successfulResults[i].Records != null && successfulResults[i].Records.Count > 0)
                {
                    all.AddRange(successfulResults[i].Records);
                }
            }

            return OrderAndTrim(DeduplicateRecords(all), maxEvents);
        }

        private static List<RdpLoginRecord> DeduplicateRecords(List<RdpLoginRecord> records)
        {
            var dedup = new List<RdpLoginRecord>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < records.Count; i++)
            {
                var current = records[i];
                if (current == null)
                {
                    continue;
                }

                var ip = NormalizeRemoteIpv4(current.IpAddress);
                if (string.IsNullOrWhiteSpace(ip))
                {
                    continue;
                }

                var time = string.IsNullOrWhiteSpace(current.Time) ? "-" : current.Time.Trim();
                var user = string.IsNullOrWhiteSpace(current.User) ? "Unknown" : current.User.Trim();
                var host = string.IsNullOrWhiteSpace(current.Host) ? "-" : current.Host.Trim();
                var key = time + "|" + user + "|" + ip + "|" + host;

                if (!keys.Add(key))
                {
                    continue;
                }

                dedup.Add(new RdpLoginRecord
                {
                    Time = time,
                    User = user,
                    IpAddress = ip,
                    Host = host
                });
            }

            return dedup;
        }

        private static List<RdpLoginRecord> OrderAndTrim(List<RdpLoginRecord> records, int maxEvents)
        {
            return records
                .OrderByDescending(item =>
                {
                    if (DateTime.TryParseExact(
                        item.Time,
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed))
                    {
                        return parsed;
                    }

                    return DateTime.MinValue;
                })
                .Take(maxEvents)
                .ToList();
        }

        private static DateTime ParseEventTime(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return DateTime.MinValue;
            }

            if (DateTime.TryParse(source, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.ToLocalTime();
            }

            return DateTime.MinValue;
        }

        private static string ReadFirst(Dictionary<string, string> map, params string[] keys)
        {
            if (map == null || keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                if (map.TryGetValue(keys[i], out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static string ExtractAfterFirstColon(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var index = text.IndexOf(':');
            if (index < 0 || index >= text.Length - 1)
            {
                return string.Empty;
            }

            return text.Substring(index + 1).Trim();
        }

        private static string NormalizeRemoteIpv4(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var candidate = input.Trim();
            if (candidate.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring("::ffff:".Length);
            }

            var match = Ipv4AnyRegex.Match(candidate);
            if (!match.Success)
            {
                return null;
            }

            var ip = match.Groups[1].Value;
            if (!IsValidIpv4(ip))
            {
                return null;
            }

            if (string.Equals(ip, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ip, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ip;
        }

        private static bool IsValidIpv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            var parts = ip.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var octet))
                {
                    return false;
                }

                if (octet < 0 || octet > 255)
                {
                    return false;
                }
            }

            return true;
        }

        private static string FirstError(ProcessResult result)
        {
            if (result == null)
            {
                return "Unknown error.";
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                return result.StandardError.Trim();
            }

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result.StandardOutput.Trim();
            }

            return "Unknown error.";
        }

        private class WevtutilQuery
        {
            public string Source { get; set; }
            public string Arguments { get; set; }
            public bool RequireLogonType10 { get; set; }
        }
    }
}
