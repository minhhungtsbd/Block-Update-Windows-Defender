using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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
        private AppSettings _settings;
        private bool _isBusy;
        private string _busyText;
        private string _currentTimeZone;
        private string _currentLocalTimeText;
        private string _lastTimeSyncMessage;
        private string _windowsUpdateStatusText;
        private string _windowsUpdateDetailText;
        private string _defenderStatusText;
        private string _defenderDetailText;
        private bool _autoSyncTimeOnStartup;
        private bool _runAtWindowsStartup;
        private SystemInfoModel _systemInfo;

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

            LogEntries = new ObservableCollection<string>();
            SystemInfo = new SystemInfoModel
            {
                ComputerName = Environment.MachineName,
                Caption = "Detecting...",
                Version = "-",
                BuildNumber = "-",
                Architecture = "-"
            };

            BusyText = "Ready";
            CurrentTimeZone = TimeZoneInfo.Local.DisplayName;
            CurrentLocalTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LastTimeSyncMessage = "No sync yet.";
            WindowsUpdateStatusText = "Detecting...";
            WindowsUpdateDetailText = "Checking Windows Update status.";
            DefenderStatusText = "Detecting...";
            DefenderDetailText = "Checking Microsoft Defender status.";

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            EnableWindowsUpdateCommand = new AsyncRelayCommand(EnableWindowsUpdateAsync, () => !IsBusy);
            DisableWindowsUpdateCommand = new AsyncRelayCommand(DisableWindowsUpdateAsync, () => !IsBusy);
            EnableDefenderCommand = new AsyncRelayCommand(() => SetDefenderAsync(true), () => !IsBusy);
            DisableDefenderCommand = new AsyncRelayCommand(() => SetDefenderAsync(false), () => !IsBusy);
            SyncTimeCommand = new AsyncRelayCommand(SyncTimeAsync, () => !IsBusy);
            OpenLogFolderCommand = new AsyncRelayCommand(OpenLogFolderAsync, () => !IsBusy);
        }

        public ObservableCollection<string> LogEntries { get; }

        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand EnableWindowsUpdateCommand { get; }
        public AsyncRelayCommand DisableWindowsUpdateCommand { get; }
        public AsyncRelayCommand EnableDefenderCommand { get; }
        public AsyncRelayCommand DisableDefenderCommand { get; }
        public AsyncRelayCommand SyncTimeCommand { get; }
        public AsyncRelayCommand OpenLogFolderCommand { get; }

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
                    AddLog($"Auto sync at startup set to {(value ? "enabled" : "disabled")}.");
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
                    AddLog($"Run at Windows startup set to {(value ? "enabled" : "disabled")}.");
                }
            }
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
            _autoSyncTimeOnStartup = _settings.AutoSyncTimeOnStartup;
            _runAtWindowsStartup = _startupService.IsEnabled() || _settings.RunAtWindowsStartup;
            OnPropertyChanged(nameof(AutoSyncTimeOnStartup));
            OnPropertyChanged(nameof(RunAtWindowsStartup));
            AddLog("Application started.");

            if (AutoSyncTimeOnStartup)
            {
                await RunBusyActionAsync("Synchronizing time...", SyncTimeCoreAsync);
            }

            await RunBusyActionAsync("Detecting system status...", RefreshAllCoreAsync);
        }

        private Task RefreshAllAsync()
        {
            return RunBusyActionAsync("Refreshing status...", RefreshAllCoreAsync);
        }

        private async Task RefreshAllCoreAsync()
        {
            SystemInfo = await _systemInfoService.GetSystemInfoAsync();
            CurrentTimeZone = TimeZoneInfo.Local.DisplayName;
            CurrentLocalTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var windowsUpdateStatus = await _windowsUpdateService.GetStatusAsync();
            WindowsUpdateStatusText = windowsUpdateStatus.StatusText;
            WindowsUpdateDetailText = windowsUpdateStatus.DetailText;

            var defenderStatus = await _defenderService.GetStatusAsync();
            DefenderStatusText = defenderStatus.StatusText;
            DefenderDetailText = defenderStatus.DetailText;

            AddLog("System information refreshed.");
        }

        private Task EnableWindowsUpdateAsync()
        {
            return RunBusyActionAsync("Enabling Windows Update...", async () =>
            {
                var status = await _windowsUpdateService.EnableAsync();
                WindowsUpdateStatusText = status.StatusText;
                WindowsUpdateDetailText = status.DetailText;
                AddLog("Windows Update enabled.");
            });
        }

        private Task DisableWindowsUpdateAsync()
        {
            return RunBusyActionAsync("Disabling Windows Update...", async () =>
            {
                var status = await _windowsUpdateService.DisableAsync();
                WindowsUpdateStatusText = status.StatusText;
                WindowsUpdateDetailText = status.DetailText;
                AddLog("Windows Update disabled.");
            });
        }

        private Task SetDefenderAsync(bool enable)
        {
            var busyText = enable ? "Enabling Defender..." : "Disabling Defender...";
            return RunBusyActionAsync(busyText, async () =>
            {
                var status = await _defenderService.SetRealtimeProtectionAsync(enable);
                DefenderStatusText = status.StatusText;
                DefenderDetailText = status.DetailText;
                AddLog(enable ? "Microsoft Defender enabled." : "Microsoft Defender disabled.");
            });
        }

        private Task SyncTimeAsync()
        {
            return RunBusyActionAsync("Synchronizing time...", SyncTimeCoreAsync);
        }

        private async Task SyncTimeCoreAsync()
        {
            var result = await _timeSyncService.SyncAsync();
            CurrentTimeZone = result.TimeZoneDisplayName;
            CurrentLocalTimeText = result.LocalTime.ToString("yyyy-MM-dd HH:mm:ss");
            LastTimeSyncMessage = result.Message;
            AddLog(result.Message);
        }

        private Task OpenLogFolderAsync()
        {
            return RunBusyActionAsync("Opening log folder...", () =>
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_logger.LogFilePath}\"");
                AddLog("Log folder opened.");
                return Task.FromResult(0);
            });
        }

        private async Task RunBusyActionAsync(string text, Func<Task> action)
        {
            try
            {
                IsBusy = true;
                BusyText = text;
                await action();
            }
            catch (Exception ex)
            {
                LastTimeSyncMessage = ex.Message;
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                BusyText = "Ready";
                IsBusy = false;
            }
        }

        private void AddLog(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {message}";
            LogEntries.Insert(0, line);
            while (LogEntries.Count > 200)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
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
        }
    }
}
