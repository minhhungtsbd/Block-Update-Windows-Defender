using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BlockUpdateWindowsDefender.Infrastructure;
using BlockUpdateWindowsDefender.Models;
using BlockUpdateWindowsDefender.Services;

namespace BlockUpdateWindowsDefender.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly LoggerService _logger;
        private readonly SettingsService _settingsService;
        private readonly SystemInfoService _systemInfoService;
        private readonly WindowsUpdateService _windowsUpdateService;
        private readonly DefenderService _defenderService;
        private readonly TimeSyncService _timeSyncService;
        private readonly StartupService _startupService;
        private readonly BrowserInstallService _browserInstallService;
        private readonly AppUpdateService _appUpdateService;
        private readonly LocalizationService _localizationService;
        private readonly DispatcherTimer _busyAnimationTimer;
        private AppSettings _settings;
        private bool _isBusy;
        private string _busyText;
        private string _busyBaseText;
        private int _busyAnimationStep;
        private string _currentTimeZone;
        private string _currentLocalTimeText;
        private string _lastTimeSyncMessage;
        private string _windowsUpdateStatusText;
        private string _windowsUpdateDetailText;
        private string _windowsUpdatePolicyStateText;
        private string _windowsUpdateWuauservStateText;
        private string _windowsUpdateUsoSvcStateText;
        private string _windowsUpdateWaaSMedicSvcStateText;
        private string _windowsUpdateManualCapabilityText;
        private string _defenderStatusText;
        private string _defenderDetailText;
        private string _defenderVerificationStatusText;
        private string _defenderRealtimeProtectionText;
        private string _defenderAntivirusStateText;
        private string _defenderTamperProtectionText;
        private string _browserInstallStatusText;
        private string _browserInstallProfileText;
        private bool _autoSyncTimeOnStartup;
        private bool _runAtWindowsStartup;
        private bool _browserSilentInstall;
        private bool _browserChromeSelected;
        private bool _browserFirefoxSelected;
        private bool _browserEdgeSelected;
        private bool _browserBraveSelected;
        private bool _browserOperaSelected;
        private bool _browserCentbrowserSelected;
        private bool _autoUpdateAppOnStartup;
        private string _publicIpDisplayText;
        private string _currentLanguageCode;
        private string _appVersionText;
        private string _appUpdateStatusText;
        private SystemInfoModel _systemInfo;
        private readonly object _logSyncRoot = new object();

        public MainViewModel()
        {
            _logger = new LoggerService();
            _settingsService = new SettingsService();
            _systemInfoService = new SystemInfoService();
            _windowsUpdateService = new WindowsUpdateService();
            var processRunner = new ProcessRunner();
            _defenderService = new DefenderService(processRunner);
            _timeSyncService = new TimeSyncService(processRunner);
            _startupService = new StartupService();
            _browserInstallService = new BrowserInstallService();
            _appUpdateService = new AppUpdateService();
            _localizationService = new LocalizationService();
            _localizationService.InitializeFromSystem();
            _currentLanguageCode = _localizationService.CurrentLanguageCode;
            _busyAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _busyAnimationTimer.Tick += BusyAnimationTimer_Tick;

            LogEntries = new ObservableCollection<string>();
            SystemInfo = new SystemInfoModel
            {
                ComputerName = Environment.MachineName,
                Caption = T("StatusDetecting"),
                Version = "-",
                BuildNumber = "-",
                Architecture = "-"
            };

            BusyText = T("StatusReady");
            CurrentTimeZone = TimeZoneInfo.Local.DisplayName;
            CurrentLocalTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTimeSyncMessage = T("MessageNoSyncYet");
            WindowsUpdateStatusText = T("StatusDetecting");
            WindowsUpdateDetailText = T("DetailCheckingWindowsUpdate");
            WindowsUpdatePolicyStateText = T("StatusDetecting");
            WindowsUpdateWuauservStateText = T("StatusDetecting");
            WindowsUpdateUsoSvcStateText = T("StatusDetecting");
            WindowsUpdateWaaSMedicSvcStateText = T("StatusDetecting");
            WindowsUpdateManualCapabilityText = T("StatusDetecting");
            DefenderStatusText = T("StatusDetecting");
            DefenderDetailText = T("DetailCheckingDefender");
            DefenderVerificationStatusText = T("StatusDetecting");
            DefenderRealtimeProtectionText = T("StatusDetecting");
            DefenderAntivirusStateText = T("StatusDetecting");
            DefenderTamperProtectionText = T("StatusDetecting");
            BrowserInstallStatusText = T("StatusReady");
            BrowserInstallProfileText = string.Format(T("BrowserProfileFormat"), T("StatusDetecting"));
            PublicIpDisplayText = string.Format(T("PublicIpFormat"), T("PublicIpUnknown"));
            AppVersionText = _appUpdateService.GetCurrentVersionText();
            AppUpdateStatusText = T("StatusReady");
            BrowserSilentInstall = true;

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            EnableWindowsUpdateCommand = new AsyncRelayCommand(EnableWindowsUpdateAsync, () => !IsBusy);
            DisableWindowsUpdateCommand = new AsyncRelayCommand(DisableWindowsUpdateAsync, () => !IsBusy);
            EnableDefenderCommand = new AsyncRelayCommand(() => SetDefenderAsync(true), () => !IsBusy);
            DisableDefenderCommand = new AsyncRelayCommand(() => SetDefenderAsync(false), () => !IsBusy);
            SyncTimeCommand = new AsyncRelayCommand(SyncTimeAsync, () => !IsBusy);
            OpenLogFolderCommand = new AsyncRelayCommand(OpenLogFolderAsync, () => !IsBusy);
            InstallBrowsersCommand = new AsyncRelayCommand(InstallBrowsersAsync, () => !IsBusy);
            SwitchToVietnameseCommand = new AsyncRelayCommand(SwitchToVietnameseAsync, () => !IsBusy);
            SwitchToEnglishCommand = new AsyncRelayCommand(SwitchToEnglishAsync, () => !IsBusy);
            CheckAndUpdateAppCommand = new AsyncRelayCommand(CheckAndUpdateAppAsync, () => !IsBusy);
        }

        public ObservableCollection<string> LogEntries { get; }

        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand EnableWindowsUpdateCommand { get; }
        public AsyncRelayCommand DisableWindowsUpdateCommand { get; }
        public AsyncRelayCommand EnableDefenderCommand { get; }
        public AsyncRelayCommand DisableDefenderCommand { get; }
        public AsyncRelayCommand SyncTimeCommand { get; }
        public AsyncRelayCommand OpenLogFolderCommand { get; }
        public AsyncRelayCommand InstallBrowsersCommand { get; }
        public AsyncRelayCommand SwitchToVietnameseCommand { get; }
        public AsyncRelayCommand SwitchToEnglishCommand { get; }
        public AsyncRelayCommand CheckAndUpdateAppCommand { get; }

        public SystemInfoModel SystemInfo
        {
            get => _systemInfo;
            set => SetProperty(ref _systemInfo, value);
        }

        public string BusyText
        {
            get => _busyText;
            set => SetProperty(ref _busyText, value);
        }

        public string CurrentTimeZone
        {
            get => _currentTimeZone;
            set => SetProperty(ref _currentTimeZone, value);
        }

        public string CurrentLocalTimeText
        {
            get => _currentLocalTimeText;
            set => SetProperty(ref _currentLocalTimeText, value);
        }

        public string LastTimeSyncMessage
        {
            get => _lastTimeSyncMessage;
            set => SetProperty(ref _lastTimeSyncMessage, value);
        }

        public string WindowsUpdateStatusText
        {
            get => _windowsUpdateStatusText;
            set => SetProperty(ref _windowsUpdateStatusText, value);
        }

        public string WindowsUpdateDetailText
        {
            get => _windowsUpdateDetailText;
            set => SetProperty(ref _windowsUpdateDetailText, value);
        }

        public string WindowsUpdatePolicyStateText
        {
            get => _windowsUpdatePolicyStateText;
            set => SetProperty(ref _windowsUpdatePolicyStateText, value);
        }

        public string WindowsUpdateWuauservStateText
        {
            get => _windowsUpdateWuauservStateText;
            set => SetProperty(ref _windowsUpdateWuauservStateText, value);
        }

        public string WindowsUpdateUsoSvcStateText
        {
            get => _windowsUpdateUsoSvcStateText;
            set => SetProperty(ref _windowsUpdateUsoSvcStateText, value);
        }

        public string WindowsUpdateWaaSMedicSvcStateText
        {
            get => _windowsUpdateWaaSMedicSvcStateText;
            set => SetProperty(ref _windowsUpdateWaaSMedicSvcStateText, value);
        }

        public string WindowsUpdateManualCapabilityText
        {
            get => _windowsUpdateManualCapabilityText;
            set => SetProperty(ref _windowsUpdateManualCapabilityText, value);
        }

        public string DefenderStatusText
        {
            get => _defenderStatusText;
            set => SetProperty(ref _defenderStatusText, value);
        }

        public string DefenderDetailText
        {
            get => _defenderDetailText;
            set => SetProperty(ref _defenderDetailText, value);
        }

        public string DefenderVerificationStatusText
        {
            get => _defenderVerificationStatusText;
            set => SetProperty(ref _defenderVerificationStatusText, value);
        }

        public string DefenderRealtimeProtectionText
        {
            get => _defenderRealtimeProtectionText;
            set => SetProperty(ref _defenderRealtimeProtectionText, value);
        }

        public string DefenderAntivirusStateText
        {
            get => _defenderAntivirusStateText;
            set => SetProperty(ref _defenderAntivirusStateText, value);
        }

        public string DefenderTamperProtectionText
        {
            get => _defenderTamperProtectionText;
            set => SetProperty(ref _defenderTamperProtectionText, value);
        }

        public string BrowserInstallStatusText
        {
            get => _browserInstallStatusText;
            set => SetProperty(ref _browserInstallStatusText, value);
        }

        public string BrowserInstallProfileText
        {
            get => _browserInstallProfileText;
            set => SetProperty(ref _browserInstallProfileText, value);
        }

        public string PublicIpDisplayText
        {
            get => _publicIpDisplayText;
            set => SetProperty(ref _publicIpDisplayText, value);
        }

        public string CurrentLanguageCode
        {
            get => _currentLanguageCode;
            private set => SetProperty(ref _currentLanguageCode, value);
        }

        public string AppVersionText
        {
            get => _appVersionText;
            set => SetProperty(ref _appVersionText, value);
        }

        public string AppUpdateStatusText
        {
            get => _appUpdateStatusText;
            set => SetProperty(ref _appUpdateStatusText, value);
        }

        public bool AutoUpdateAppOnStartup
        {
            get => _autoUpdateAppOnStartup;
            set
            {
                if (SetProperty(ref _autoUpdateAppOnStartup, value))
                {
                    if (_settings == null)
                    {
                        return;
                    }

                    _settings.AutoUpdateAppOnStartup = value;
                    _settingsService.Save(_settings);
                }
            }
        }

        public bool AutoSyncTimeOnStartup
        {
            get => _autoSyncTimeOnStartup;
            set
            {
                if (SetProperty(ref _autoSyncTimeOnStartup, value))
                {
                    if (_settings == null)
                    {
                        return;
                    }

                    _settings.AutoSyncTimeOnStartup = value;
                    _settingsService.Save(_settings);
                    AddLog(value ? T("LogAutoSyncEnabled") : T("LogAutoSyncDisabled"));
                }
            }
        }

        public bool RunAtWindowsStartup
        {
            get => _runAtWindowsStartup;
            set
            {
                if (SetProperty(ref _runAtWindowsStartup, value))
                {
                    if (_settings == null)
                    {
                        return;
                    }

                    _settings.RunAtWindowsStartup = value;
                    _startupService.SetEnabled(value);
                    _settingsService.Save(_settings);
                    AddLog(value ? T("LogRunAtStartupEnabled") : T("LogRunAtStartupDisabled"));
                }
            }
        }

        public bool BrowserSilentInstall
        {
            get => _browserSilentInstall;
            set => SetProperty(ref _browserSilentInstall, value);
        }

        public bool BrowserChromeSelected
        {
            get => _browserChromeSelected;
            set => SetProperty(ref _browserChromeSelected, value);
        }

        public bool BrowserFirefoxSelected
        {
            get => _browserFirefoxSelected;
            set => SetProperty(ref _browserFirefoxSelected, value);
        }

        public bool BrowserEdgeSelected
        {
            get => _browserEdgeSelected;
            set => SetProperty(ref _browserEdgeSelected, value);
        }

        public bool BrowserBraveSelected
        {
            get => _browserBraveSelected;
            set => SetProperty(ref _browserBraveSelected, value);
        }

        public bool BrowserOperaSelected
        {
            get => _browserOperaSelected;
            set => SetProperty(ref _browserOperaSelected, value);
        }

        public bool BrowserCentbrowserSelected
        {
            get => _browserCentbrowserSelected;
            set => SetProperty(ref _browserCentbrowserSelected, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public async Task InitializeAsync()
        {
            _settings = _settingsService.Load();
            if (!string.IsNullOrWhiteSpace(_settings.UiLanguageCode))
            {
                _localizationService.ApplyLanguage(_settings.UiLanguageCode);
                CurrentLanguageCode = _localizationService.CurrentLanguageCode;
            }

            UpdatePublicIpDisplay(_settings.LastDetectedPublicIp, false);
            RefreshLocalizedDisplayTexts();

            _autoSyncTimeOnStartup = _settings.AutoSyncTimeOnStartup;
            _autoUpdateAppOnStartup = _settings.AutoUpdateAppOnStartup;
            _runAtWindowsStartup = _startupService.IsEnabled() || _settings.RunAtWindowsStartup;
            OnPropertyChanged(nameof(AutoSyncTimeOnStartup));
            OnPropertyChanged(nameof(AutoUpdateAppOnStartup));
            OnPropertyChanged(nameof(RunAtWindowsStartup));
            AddLog(T("LogApplicationStarted"));

            await RunBusyActionAsync(T("BusyLoadingCoreInfo"), RefreshAllCoreAsync);

            if (AutoUpdateAppOnStartup)
            {
                _ = RunStartupAutoUpdateAsync();
            }

            if (AutoSyncTimeOnStartup)
            {
                _ = RunStartupTimeSyncAsync();
            }
        }

        private Task RefreshAllAsync()
        {
            return RunBusyActionAsync(T("BusyRefreshingStatus"), RefreshAllCoreAsync);
        }

        private async Task RefreshAllCoreAsync()
        {
            CurrentTimeZone = TimeZoneInfo.Local.DisplayName;
            CurrentLocalTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var systemInfoTask = _systemInfoService.GetSystemInfoAsync();
            var browserProfileTask = _browserInstallService.DetectWindowsVersionAsync();
            var windowsUpdateStatusTask = _windowsUpdateService.GetStatusAsync();
            var defenderStatusTask = _defenderService.GetStatusAsync();

            await Task.WhenAll(systemInfoTask, browserProfileTask, windowsUpdateStatusTask, defenderStatusTask);

            SystemInfo = systemInfoTask.Result;
            BrowserInstallProfileText = string.Format(T("BrowserProfileFormat"), browserProfileTask.Result);

            var windowsUpdateStatus = windowsUpdateStatusTask.Result;
            ApplyWindowsUpdateStatus(windowsUpdateStatus);

            var defenderStatus = defenderStatusTask.Result;
            ApplyDefenderStatus(defenderStatus);

            AddLog(T("LogSystemInfoRefreshed"));
        }

        private Task EnableWindowsUpdateAsync()
        {
            return RunBusyActionAsync(T("BusyEnablingWindowsUpdate"), async () =>
            {
                var status = await _windowsUpdateService.EnableAsync();
                ApplyWindowsUpdateStatus(status);
                AddLog(T("LogWindowsUpdateEnabled"));
            });
        }

        private Task DisableWindowsUpdateAsync()
        {
            return RunBusyActionAsync(T("BusyDisablingWindowsUpdate"), async () =>
            {
                var status = await _windowsUpdateService.DisableAsync();
                ApplyWindowsUpdateStatus(status);
                AddLog(T("LogWindowsUpdateDisabled"));
            });
        }

        private Task SetDefenderAsync(bool enable)
        {
            var busyText = enable ? T("BusyEnablingDefender") : T("BusyDisablingDefender");
            return RunBusyActionAsync(busyText, async () =>
            {
                var status = await _defenderService.SetRealtimeProtectionAsync(enable);
                ApplyDefenderStatus(status);
                AddLog(enable ? T("LogDefenderEnabled") : T("LogDefenderDisabled"));
            });
        }

        private Task SyncTimeAsync()
        {
            return RunBusyActionAsync(T("BusySynchronizingTime"), SyncTimeCoreAsync);
        }

        private async Task SyncTimeCoreAsync()
        {
            var result = await _timeSyncService.SyncAsync(_localizationService.CurrentLanguageCode);
            AutoSwitchLanguageFromTimeSync(result);
            UpdatePublicIpDisplay(result.DetectedPublicIp, true);
            CurrentTimeZone = result.TimeZoneDisplayName;
            CurrentLocalTimeText = result.LocalTime.ToString("yyyy-MM-dd HH:mm:ss");
            LastTimeSyncMessage = result.Message;
            AddLog(result.Message);
        }

        private async Task RunStartupTimeSyncAsync()
        {
            try
            {
                BusyText = T("BusyAutoTimeSyncBackground");
                AddLog(T("LogAutoTimeSyncStarted"));
                await SyncTimeCoreAsync();
            }
            catch (Exception ex)
            {
                LastTimeSyncMessage = ex.Message;
                AddLog(string.Format(T("LogAutoTimeSyncErrorFormat"), ex.Message));
            }
            finally
            {
                if (!IsBusy)
                {
                    BusyText = T("StatusReady");
                }
            }
        }

        private async Task RunStartupAutoUpdateAsync()
        {
            try
            {
                AppUpdateStatusText = T("UpdateStatusChecking");
                AddLog(T("LogAutoUpdateStarted"));
                await CheckAndApplyUpdateCoreAsync(true);
            }
            catch (Exception ex)
            {
                AppUpdateStatusText = T("UpdateStatusFailed");
                AddLog(string.Format(T("LogAutoUpdateErrorFormat"), ex.Message));
            }
        }

        private Task OpenLogFolderAsync()
        {
            return RunBusyActionAsync(T("BusyOpeningLogFolder"), () =>
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_logger.LogFilePath}\"");
                AddLog(T("LogFolderOpened"));
                return Task.FromResult(0);
            });
        }

        private Task InstallBrowsersAsync()
        {
            return RunBusyActionAsync(T("BusyInstallingBrowsers"), async () =>
            {
                var selectedBrowsers = new Collection<string>();
                if (BrowserChromeSelected) selectedBrowsers.Add("Chrome");
                if (BrowserFirefoxSelected) selectedBrowsers.Add("Firefox");
                if (BrowserEdgeSelected) selectedBrowsers.Add("Edge");
                if (BrowserBraveSelected) selectedBrowsers.Add("Brave");
                if (BrowserOperaSelected) selectedBrowsers.Add("Opera");
                if (BrowserCentbrowserSelected) selectedBrowsers.Add("Centbrowser");

                if (selectedBrowsers.Count == 0)
                {
                    BrowserInstallStatusText = T("BrowserStatusNoSelection");
                    AddLog(T("LogNoBrowserSelected"));
                    return;
                }

                BrowserInstallStatusText = T("BrowserStatusRunning");
                AddLog(string.Format(T("LogStartingBrowserInstallFormat"), selectedBrowsers.Count));
                await _browserInstallService.InstallBrowsersAsync(selectedBrowsers, BrowserSilentInstall, AddLog);
                BrowserInstallStatusText = T("BrowserStatusCompleted");
            });
        }

        private Task CheckAndUpdateAppAsync()
        {
            return RunBusyActionAsync(T("BusyCheckingAppUpdate"), () => CheckAndApplyUpdateCoreAsync(false));
        }

        private async Task CheckAndApplyUpdateCoreAsync(bool isStartupAutoCheck)
        {
            AppUpdateStatusText = T("UpdateStatusChecking");
            AddLog(T("LogCheckingForUpdates"));

            var checkResult = await _appUpdateService.CheckForUpdateAsync();
            if (!checkResult.IsSuccess)
            {
                AppUpdateStatusText = T("UpdateStatusFailed");
                AddLog(string.Format(T("LogUpdateCheckFailedFormat"), checkResult.ErrorMessage));
                return;
            }

            if (!string.IsNullOrWhiteSpace(checkResult.CurrentVersionText))
            {
                AppVersionText = checkResult.CurrentVersionText;
            }

            RefreshLocalizedDisplayTexts();

            if (!checkResult.IsUpdateAvailable)
            {
                AppUpdateStatusText = T("UpdateStatusUpToDate");
                if (!isStartupAutoCheck)
                {
                    AddLog(T("LogNoUpdateAvailable"));
                }

                return;
            }

            AppUpdateStatusText = string.Format(T("UpdateStatusAvailableFormat"), checkResult.LatestVersionText);
            AddLog(string.Format(T("LogUpdateAvailableFormat"), checkResult.LatestVersionText));

            var applyResult = await _appUpdateService.ApplyUpdateAsync(checkResult.DownloadUrl);
            if (!applyResult.IsSuccess)
            {
                AppUpdateStatusText = T("UpdateStatusFailed");
                AddLog(string.Format(T("LogUpdateApplyFailedFormat"), applyResult.ErrorMessage));
                return;
            }

            AppUpdateStatusText = T("UpdateStatusApplying");
            AddLog(T("LogUpdateApplied"));
            Application.Current.Shutdown();
        }

        private async Task RunBusyActionAsync(string text, Func<Task> action)
        {
            try
            {
                IsBusy = true;
                StartBusyIndicator(text);
                await action();
            }
            catch (Exception ex)
            {
                LastTimeSyncMessage = ex.Message;
                AddLog(string.Format(T("LogErrorFormat"), ex.Message));
            }
            finally
            {
                StopBusyIndicator();
                BusyText = T("StatusReady");
                IsBusy = false;
            }
        }

        private void StartBusyIndicator(string text)
        {
            _busyBaseText = text.TrimEnd('.', ' ');
            _busyAnimationStep = 0;
            BusyText = _busyBaseText;
            _busyAnimationTimer.Start();
        }

        private void StopBusyIndicator()
        {
            _busyAnimationTimer.Stop();
            _busyAnimationStep = 0;
            _busyBaseText = string.Empty;
        }

        private void BusyAnimationTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_busyBaseText))
            {
                return;
            }

            _busyAnimationStep = (_busyAnimationStep + 1) % 4;
            BusyText = _busyBaseText + new string('.', _busyAnimationStep);
        }

        private Task SwitchToVietnameseAsync()
        {
            return RunBusyActionAsync(T("BusySwitchingLanguage"), () => ApplyManualLanguageSwitchAsync("vi"));
        }

        private Task SwitchToEnglishAsync()
        {
            return RunBusyActionAsync(T("BusySwitchingLanguage"), () => ApplyManualLanguageSwitchAsync("en"));
        }

        private async Task ApplyManualLanguageSwitchAsync(string languageCode)
        {
            ApplyLanguageRuntime(languageCode, true, true);
            await RefreshAllCoreAsync();
        }

        private void AutoSwitchLanguageFromTimeSync(TimeSyncResult result)
        {
            if (result == null)
            {
                return;
            }

            var previous = CurrentLanguageCode;
            ApplyLanguageRuntime(result.SuggestedLanguageCode, true, false);
            if (!string.Equals(previous, CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                AddLog(CurrentLanguageCode == "vi"
                    ? T("LogLanguageSwitchedVietnamese")
                    : T("LogLanguageSwitchedEnglish"));
            }
        }

        private void ApplyLanguageRuntime(string languageCode, bool persistToSettings, bool logManualSwitch)
        {
            var normalized = _localizationService.NormalizeLanguageCode(languageCode);
            var changed = _localizationService.ApplyLanguage(normalized);
            CurrentLanguageCode = normalized;

            if (_settings != null && persistToSettings)
            {
                _settings.UiLanguageCode = normalized;
                _settingsService.Save(_settings);
            }

            RefreshLocalizedDisplayTexts();

            if (logManualSwitch && changed)
            {
                AddLog(normalized == "vi" ? T("LogLanguageSetVietnamese") : T("LogLanguageSetEnglish"));
            }
        }

        private void RefreshLocalizedDisplayTexts()
        {
            if (!IsBusy)
            {
                BusyText = T("StatusReady");
            }

            var lastMessage = LastTimeSyncMessage ?? string.Empty;
            if (string.Equals(lastMessage, "No sync yet.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lastMessage, "Chua dong bo.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lastMessage, "Chưa đồng bộ.", StringComparison.OrdinalIgnoreCase))
            {
                LastTimeSyncMessage = T("MessageNoSyncYet");
            }

            BrowserInstallStatusText = LocalizeBrowserStatus(BrowserInstallStatusText);
            BrowserInstallProfileText = string.Format(T("BrowserProfileFormat"), ExtractCompatibilityProfile(BrowserInstallProfileText));
            AppUpdateStatusText = LocalizeAppUpdateStatus(AppUpdateStatusText);

            WindowsUpdateStatusText = LocalizeWindowsUpdateStatus(WindowsUpdateStatusText);
            WindowsUpdateDetailText = LocalizeWindowsUpdateDetail(WindowsUpdateDetailText);
            WindowsUpdatePolicyStateText = LocalizePolicyState(WindowsUpdatePolicyStateText);
            WindowsUpdateWuauservStateText = LocalizeServiceState(WindowsUpdateWuauservStateText);
            WindowsUpdateUsoSvcStateText = LocalizeServiceState(WindowsUpdateUsoSvcStateText);
            WindowsUpdateWaaSMedicSvcStateText = LocalizeServiceState(WindowsUpdateWaaSMedicSvcStateText);
            WindowsUpdateManualCapabilityText = LocalizeManualUpdateCapability(WindowsUpdateManualCapabilityText);

            DefenderStatusText = LocalizeDefenderStatus(DefenderStatusText);
            DefenderDetailText = LocalizeDefenderDetail(DefenderDetailText);
            DefenderVerificationStatusText = LocalizeDefenderStatus(DefenderVerificationStatusText);
            DefenderRealtimeProtectionText = LocalizeDefenderStatus(DefenderRealtimeProtectionText);
            DefenderAntivirusStateText = LocalizeDefenderStatus(DefenderAntivirusStateText);
            DefenderTamperProtectionText = LocalizeDefenderStatus(DefenderTamperProtectionText);

            UpdatePublicIpDisplay(null, false);
        }

        private void ApplyWindowsUpdateStatus(WindowsUpdateStatus status)
        {
            if (status == null)
            {
                return;
            }

            WindowsUpdateStatusText = LocalizeWindowsUpdateStatus(status.StatusText);
            WindowsUpdateDetailText = LocalizeWindowsUpdateDetail(status.DetailText);
            WindowsUpdatePolicyStateText = LocalizePolicyState(status.PolicyStateText);
            WindowsUpdateWuauservStateText = LocalizeServiceState(status.WuauservStateText);
            WindowsUpdateUsoSvcStateText = LocalizeServiceState(status.UsoSvcStateText);
            WindowsUpdateWaaSMedicSvcStateText = LocalizeServiceState(status.WaaSMedicSvcStateText);
            WindowsUpdateManualCapabilityText = LocalizeManualUpdateCapability(status.ManualUpdateCapabilityText);
        }

        private void ApplyDefenderStatus(DefenderStatus status)
        {
            if (status == null)
            {
                return;
            }

            DefenderStatusText = LocalizeDefenderStatus(status.StatusText);
            DefenderDetailText = LocalizeDefenderDetail(status.DetailText);

            if (!status.IsSupported)
            {
                var unsupported = LocalizeDefenderStatus("Unsupported");
                DefenderVerificationStatusText = unsupported;
                DefenderRealtimeProtectionText = unsupported;
                DefenderAntivirusStateText = unsupported;
                DefenderTamperProtectionText = unsupported;
                return;
            }

            DefenderVerificationStatusText = LocalizeDefenderStatus(status.StatusText);
            DefenderRealtimeProtectionText = LocalizeDefenderStatus(status.IsRealtimeProtectionEnabled ? "Enabled" : "Disabled");
            DefenderAntivirusStateText = LocalizeDefenderStatus(status.IsAntivirusEnabled ? "Enabled" : "Disabled");
            DefenderTamperProtectionText = LocalizeDefenderStatus(status.IsTamperProtected ? "Enabled" : "Disabled");
        }

        private void UpdatePublicIpDisplay(string ip, bool persist)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                if (_settings != null)
                {
                    _settings.LastDetectedPublicIp = ip;
                    if (persist)
                    {
                        _settingsService.Save(_settings);
                    }
                }
            }

            var value = !string.IsNullOrWhiteSpace(_settings?.LastDetectedPublicIp)
                ? _settings.LastDetectedPublicIp
                : T("PublicIpUnknown");

            PublicIpDisplayText = string.Format(T("PublicIpFormat"), value);
        }

        private string LocalizeWindowsUpdateStatus(string value)
        {
            return LocalizeExact(value,
                "Disabled (Hardened)", T("UpdateStatusDisabledHardened"),
                "Disabled", T("UpdateStatusDisabled"),
                "Enabled", T("UpdateStatusEnabled"));
        }

        private string LocalizeWindowsUpdateDetail(string value)
        {
            return LocalizeExact(value,
                "Automatic Updates is disabled by policy and core update services are hardened where Windows allows it. Review Verification for exact service states.", T("UpdateDetailHardened"),
                "Automatic Updates is disabled by local policy. Review Verification to confirm how much manual update access remains.", T("UpdateDetailDisabled"),
                "Automatic Updates is enabled. Windows Update services are allowed to run normally.", T("UpdateDetailEnabled"));
        }

        private string LocalizePolicyState(string value)
        {
            return LocalizeExact(value,
                "Automatic Updates disabled and Windows Update access blocked", T("PolicyDisabledAndBlocked"),
                "Automatic Updates disabled", T("PolicyAutoDisabled"),
                "Windows Update access blocked", T("PolicyAccessBlocked"),
                "No blocking policy", T("PolicyNone"));
        }

        private string LocalizeManualUpdateCapability(string value)
        {
            return LocalizeExact(value,
                "Likely blocked", T("ManualLikelyBlocked"),
                "Partially blocked (WaaSMedicSvc may recover update components)", T("ManualPartiallyBlocked"),
                "Still possible", T("ManualStillPossible"),
                "Available", T("ManualAvailable"));
        }

        private string LocalizeDefenderStatus(string value)
        {
            return LocalizeExact(value,
                "Unsupported", T("DefenderUnsupported"),
                "Enabled", T("DefenderEnabled"),
                "Disabled", T("DefenderDisabled"),
                "Action Failed", T("DefenderActionFailed"));
        }

        private string LocalizeDefenderDetail(string value)
        {
            return LocalizeExact(value,
                "Tamper Protection is enabled. Some changes can be blocked by Windows.", T("DefenderDetailTamper"),
                "Microsoft Defender is available and can be managed by this app.", T("DefenderDetailManaged"),
                "Microsoft Defender is not available on this Windows version.", T("DefenderDetailUnavailable"),
                "Windows blocked the action.", T("DefenderDetailBlocked"));
        }

        private string LocalizeServiceState(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var text = value;
            if (CurrentLanguageCode == "vi")
            {
                text = text.Replace("Running", "Đang chạy")
                           .Replace("Stopped", "Đã dừng")
                           .Replace("Manual", "Thủ công")
                           .Replace("Automatic", "Tự động")
                           .Replace("Disabled", "Vô hiệu hóa")
                           .Replace("Unavailable", "Không khả dụng")
                           .Replace("Unknown", "Không rõ");
            }
            else
            {
                text = text.Replace("Đang chạy", "Running")
                           .Replace("Đã dừng", "Stopped")
                           .Replace("Thủ công", "Manual")
                           .Replace("Tự động", "Automatic")
                           .Replace("Vô hiệu hóa", "Disabled")
                           .Replace("Không khả dụng", "Unavailable")
                           .Replace("Không rõ", "Unknown");
            }

            return text;
        }

        private string LocalizeBrowserStatus(string value)
        {
            return LocalizeExact(value,
                "Ready", T("StatusReady"),
                "No browser selected", T("BrowserStatusNoSelection"),
                "Running...", T("BrowserStatusRunning"),
                "Completed", T("BrowserStatusCompleted"));
        }

        private string LocalizeAppUpdateStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var englishPrefix = "Update available:";
            var vietnamesePrefix = "Có bản mới:";
            if (value.StartsWith(englishPrefix, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(vietnamesePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var marker = value.IndexOf(':');
                var versionPart = marker >= 0 && marker < value.Length - 1
                    ? value.Substring(marker + 1).Trim()
                    : string.Empty;
                return string.Format(T("UpdateStatusAvailableFormat"), versionPart);
            }

            return LocalizeExact(value,
                "Checking...", T("UpdateStatusChecking"),
                "Up to date", T("UpdateStatusUpToDate"),
                "Applying update...", T("UpdateStatusApplying"),
                "Update failed", T("UpdateStatusFailed"));
        }

        private string LocalizeExact(string value, params string[] pairs)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                var english = pairs[i];
                var localized = pairs[i + 1];
                if (string.Equals(value, english, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, localized, StringComparison.OrdinalIgnoreCase))
                {
                    return CurrentLanguageCode == "vi" ? localized : english;
                }
            }

            return value;
        }

        private string ExtractCompatibilityProfile(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return "10.0";
            }

            var match = Regex.Match(source, @"\b\d+\.\d+\b");
            return match.Success ? match.Value : "10.0";
        }

        private string T(string key)
        {
            return _localizationService.GetText(key);
        }

        private void AddLog(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {message}";

            Action appendEntry = () =>
            {
                lock (_logSyncRoot)
                {
                    LogEntries.Insert(0, line);
                    while (LogEntries.Count > 200)
                    {
                        LogEntries.RemoveAt(LogEntries.Count - 1);
                    }
                }
            };

            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(appendEntry);
            }
            else
            {
                appendEntry();
            }

            _logger.Log(message);
        }

        private void RaiseCommandStates()
        {
            RefreshCommand.RaiseCanExecuteChanged();
            EnableWindowsUpdateCommand.RaiseCanExecuteChanged();
            DisableWindowsUpdateCommand.RaiseCanExecuteChanged();
            EnableDefenderCommand.RaiseCanExecuteChanged();
            DisableDefenderCommand.RaiseCanExecuteChanged();
            SyncTimeCommand.RaiseCanExecuteChanged();
            OpenLogFolderCommand.RaiseCanExecuteChanged();
            InstallBrowsersCommand.RaiseCanExecuteChanged();
            SwitchToVietnameseCommand.RaiseCanExecuteChanged();
            SwitchToEnglishCommand.RaiseCanExecuteChanged();
            CheckAndUpdateAppCommand.RaiseCanExecuteChanged();
        }
    }
}
