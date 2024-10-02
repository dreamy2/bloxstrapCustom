// To debug the automatic updater:
// - Uncomment the definition below
// - Publish the executable
// - Launch the executable (click no when it asks you to upgrade)
// - Launch Roblox (for testing web launches, run it from the command prompt)
// - To re-test the same executable, delete it from the installation folder

// #define DEBUG_UPDATER

#if DEBUG_UPDATER
#warning "Automatic updater debugging is enabled"
#endif

using System.Windows;
using System.Windows.Forms;

using Microsoft.Win32;

using Bloxstrap.AppData;

namespace Bloxstrap
{
    public class Bootstrapper
    {
        #region Properties
        private const int ProgressBarMaximum = 10000;
      
        private const string AppSettings =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
            "<Settings>\r\n" +
            "	<ContentFolder>content</ContentFolder>\r\n" +
            "	<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
            "</Settings>\r\n";

        private readonly CancellationTokenSource _cancelTokenSource = new();

        private readonly IAppData AppData;

        private string _launchCommandLine = App.LaunchSettings.RobloxLaunchArgs;
        private LaunchMode _launchMode = App.LaunchSettings.RobloxLaunchMode;
        private string _latestVersionGuid = null!;
        private PackageManifest _versionPackageManifest = null!;

        private bool _isInstalling = false;
        private double _progressIncrement;
        private long _totalDownloadedBytes = 0;

        private bool _mustUpgrade => String.IsNullOrEmpty(AppData.State.VersionGuid) || File.Exists(AppData.LockFilePath) || !File.Exists(AppData.ExecutablePath);
        private bool _noConnection = false;

        public IBootstrapperDialog? Dialog = null;

        public bool IsStudioLaunch => _launchMode != LaunchMode.Player;
        #endregion

        #region Core
        public Bootstrapper()
        {
            AppData = IsStudioLaunch ? new RobloxStudioData() : new RobloxPlayerData();
        }

        private void SetStatus(string message)
        {
            App.Logger.WriteLine("Bootstrapper::SetStatus", message);

            message = message.Replace("{product}", AppData.ProductName);

            if (Dialog is not null)
                Dialog.Message = message;
        }

        private void UpdateProgressBar()
        {
            if (Dialog is null)
                return;

            int progressValue = (int)Math.Floor(_progressIncrement * _totalDownloadedBytes);

            // bugcheck: if we're restoring a file from a package, it'll incorrectly increment the progress beyond 100
            // too lazy to fix properly so lol
            progressValue = Math.Clamp(progressValue, 0, ProgressBarMaximum);

            Dialog.ProgressValue = progressValue;
        }
        
        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";

            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");

            if (App.LaunchSettings.IsUninstall)
            {
                Uninstall();
                return;
            }

            // connectivity check

            App.Logger.WriteLine(LOG_IDENT, "Performing connectivity check...");

            SetStatus(Strings.Bootstrapper_Status_Connecting);

            try
            {
                await RobloxDeployment.GetInfo(RobloxDeployment.DefaultChannel);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Connectivity check failed!");
                App.Logger.WriteException(LOG_IDENT, ex);

                string message = Strings.Bootstrapper_Connectivity_Preventing;

                if (ex.GetType() == typeof(HttpResponseException))
                    message = Strings.Bootstrapper_Connectivity_RobloxDown;
                else if (ex.GetType() == typeof(TaskCanceledException))
                    message = Strings.Bootstrapper_Connectivity_TimedOut;
                else if (ex.GetType() == typeof(AggregateException))
                    ex = ex.InnerException!;

                Frontend.ShowConnectivityDialog(Strings.Dialog_Connectivity_UnableToConnect, message, ex);

            if (_mustUpgrade)
                App.Terminate(ErrorCode.ERROR_CANCELLED);
        }
        
        public async Task Run()
        {
            const string LOG_IDENT = "Bootstrapper::Run";

            App.Logger.WriteLine(LOG_IDENT, "Running bootstrapper");

            SetStatus(Strings.Bootstrapper_Status_Connecting);

            var connectionResult = await RobloxDeployment.InitializeConnectivity();

            App.Logger.WriteLine(LOG_IDENT, "Connectivity check finished");

            if (connectionResult is not null)
                HandleConnectionError(connectionResult);
            
#if !DEBUG || DEBUG_UPDATER
            if (App.Settings.Prop.CheckForUpdates && !App.LaunchSettings.UpgradeFlag.Active)
            {
                bool updatePresent = await CheckForUpdates();
                
                if (updatePresent)
                    return;
            }
#endif

            // ensure only one instance of the bootstrapper is running at the time
            // so that we don't have stuff like two updates happening simultaneously

            bool mutexExists = false;

            try
            {
                Mutex.OpenExisting("Bloxstrap_SingletonMutex").Close();
                App.Logger.WriteLine(LOG_IDENT, "Bloxstrap_SingletonMutex mutex exists, waiting...");
                SetStatus(Strings.Bootstrapper_Status_WaitingOtherInstances);
                mutexExists = true;
            }
            catch (Exception)
            {
                // no mutex exists
            }

            // wait for mutex to be released if it's not yet
            await using var mutex = new AsyncMutex(false, "Bloxstrap-Bootstrapper");
            await mutex.AcquireAsync(_cancelTokenSource.Token);

            // reload our configs since they've likely changed by now
            if (mutexExists)
            {
                App.Settings.Load();
                App.State.Load();
            }

            if (!_noConnection)
            {
                try
                {
                    await GetLatestVersionInfo();
                }
                catch (Exception ex)
                {
                    HandleConnectionError(ex);
                }
            }

            if (!_noConnection)
            {
                if (AppData.State.VersionGuid != _latestVersionGuid || _mustUpgrade)
                    await UpgradeRoblox();

                // we require deployment details for applying modifications for a worst case scenario,
                // where we'd need to restore files from a package that isn't present on disk and needs to be redownloaded
                await ApplyModifications();
            }

            // check registry entries for every launch, just in case the stock bootstrapper changes it back

            if (IsStudioLaunch)
                WindowsRegistry.RegisterStudio();
            else
                WindowsRegistry.RegisterPlayer();

            await mutex.ReleaseAsync();

            if (!App.LaunchSettings.NoLaunchFlag.Active && !_cancelTokenSource.IsCancellationRequested)
                StartRoblox();

            Dialog?.CloseBootstrapper();
        }

        private async Task GetLatestVersionInfo()
        {
            const string LOG_IDENT = "Bootstrapper::GetLatestVersionInfo";

            // at this point we've finished updating our configs
            App.Settings.Save();
            App.State.Save();
            App.ShouldSaveConfigs = false;

            await mutex.ReleaseAsync();

            if (App.IsFirstRun && App.LaunchSettings.IsNoLaunch)
                Dialog?.ShowSuccess(Strings.Bootstrapper_SuccessfullyInstalled);
            else if (!App.LaunchSettings.IsNoLaunch && !_cancelFired)
                await StartRoblox();
        }

        private async Task CheckLatestVersion()
        {
            const string LOG_IDENT = "Bootstrapper::CheckLatestVersion";

            ClientVersion clientVersion;

            try
            {
                clientVersion = await RobloxDeployment.GetInfo(channel, AppData.BinaryType);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode is not HttpStatusCode.Unauthorized 
                    and not HttpStatusCode.Forbidden 
                    and not HttpStatusCode.NotFound)
                    throw;

                App.Logger.WriteLine(LOG_IDENT, $"Changing channel from {channel} to {RobloxDeployment.DefaultChannel} because HTTP {(int)ex.StatusCode}");

                channel = RobloxDeployment.DefaultChannel;
                clientVersion = await RobloxDeployment.GetInfo(channel, AppData.BinaryType);
            }

            if (clientVersion.IsBehindDefaultChannel)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Changing channel from {channel} to {RobloxDeployment.DefaultChannel} because channel is behind production");

                channel = RobloxDeployment.DefaultChannel;
                clientVersion = await RobloxDeployment.GetInfo(channel, AppData.BinaryType);
            }

            key.SetValueSafe("www.roblox.com", channel);

            _latestVersionGuid = clientVersion.VersionGuid;

            string pkgManifestUrl = RobloxDeployment.GetLocation($"/{_latestVersionGuid}-rbxPkgManifest.txt");
            var pkgManifestData = await App.HttpClient.GetStringAsync(pkgManifestUrl);

            _versionPackageManifest = new(pkgManifestData);
        }

        private void StartRoblox()
        {
            const string LOG_IDENT = "Bootstrapper::StartRoblox";

            SetStatus(Strings.Bootstrapper_Status_Starting);

            if (!File.Exists(Path.Combine(Paths.System, "mfplat.dll")))
            {
                Frontend.ShowMessageBox(
                    Strings.Bootstrapper_WMFNotFound, 
                    MessageBoxImage.Error
                );

                if (!App.LaunchSettings.IsQuiet)
                    Utilities.ShellExecute("https://support.microsoft.com/en-us/topic/media-feature-pack-list-for-windows-n-editions-c1c6fffa-d052-8338-7a79-a4bb980a700a");

                Dialog?.CloseBootstrapper();
                return;
            }

            if (_launchMode != LaunchMode.StudioAuth)
            {
                _launchCommandLine = _launchCommandLine.Replace("LAUNCHTIMEPLACEHOLDER", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());


                if (_launchCommandLine.StartsWith("roblox-player:1"))
                    _launchCommandLine += "+channel:";
                else
                    _launchCommandLine += " -channel ";

                if (App.Settings.Prop.Channel.ToLowerInvariant() == RobloxDeployment.DefaultChannel.ToLowerInvariant())
                    _launchCommandLine += "production";
                else
                    _launchCommandLine += App.Settings.Prop.Channel.ToLowerInvariant();

                if (App.Settings.Prop.ForceRobloxLanguage)
                {
                    var match = Regex.Match(_launchCommandLine, "gameLocale:([a-z_]+)", RegexOptions.CultureInvariant);

                    if (match.Groups.Count == 2)
                        _launchCommandLine = _launchCommandLine.Replace("robloxLocale:en_us", $"robloxLocale:{match.Groups[1].Value}", StringComparison.InvariantCultureIgnoreCase);
                }

                if (!String.IsNullOrEmpty(_launchCommandLine))
                    _launchCommandLine += " ";

                _launchCommandLine += "-isInstallerLaunch";
            }

            var startInfo = new ProcessStartInfo()
            {
                FileName = AppData.ExecutablePath,
                Arguments = _launchCommandLine,
                WorkingDirectory = AppData.Directory
            };

            if (_launchMode == LaunchMode.StudioAuth)
            {
                Process.Start(startInfo);
                return;
            }

            int gameClientPid;
            bool startEventSignalled;

            // TODO: figure out why this is causing roblox to block for some users
            using (var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset, AppData.StartEvent))
            {
                startEvent.Reset();

                // v2.2.0 - byfron will trip if we keep a process handle open for over a minute, so we're doing this now
                using (var process = Process.Start(startInfo)!)
                {
                    gameClientPid = process.Id;
                }

                App.Logger.WriteLine(LOG_IDENT, $"Started Roblox (PID {gameClientPid}), waiting for start event");

                startEventSignalled = startEvent.WaitOne(TimeSpan.FromSeconds(30));
            }

            if (!startEventSignalled)
            {
                Frontend.ShowPlayerErrorDialog();
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, "Start event signalled");

            if (IsStudioLaunch)
                return;

            var autoclosePids = new List<int>();

            // launch custom integrations now
            foreach (var integration in App.Settings.Prop.CustomIntegrations)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Launching custom integration '{integration.Name}' ({integration.Location} {integration.LaunchArgs} - autoclose is {integration.AutoClose})");

                int pid = 0;
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = integration.Location,
                        Arguments = integration.LaunchArgs.Replace("\r\n", " "),
                        WorkingDirectory = Path.GetDirectoryName(integration.Location),
                        UseShellExecute = true
                    })!;

                    pid = process.Id;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to launch integration '{integration.Name}'!");
                    App.Logger.WriteLine(LOG_IDENT, $"{ex.Message}");
                }

                if (integration.AutoClose && pid != 0)
                    autoclosePids.Add(pid);
            }

            string args = gameClientPid.ToString();

            if (autoclosePids.Any())
                args += $";{String.Join(',', autoclosePids)}";

            if (App.Settings.Prop.EnableActivityTracking || autoclosePids.Any())
            {
                using var ipl = new InterProcessLock("Watcher", TimeSpan.FromSeconds(5));

                // TODO: look into if this needs to be launched *before* roblox starts
                if (ipl.IsAcquired)
                    Process.Start(Paths.Process, $"-watcher \"{args}\"");
            }
        }

        // TODO: the bootstrapper dialogs call this function directly.
        // this should probably be behind an event invocation.
        public void Cancel()
        {
            const string LOG_IDENT = "Bootstrapper::Cancel";

            if (!_isInstalling)
            {
                // TODO: this sucks and needs to be done better
                App.Terminate(ErrorCode.ERROR_CANCELLED);
                return;
            }

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            App.Logger.WriteLine(LOG_IDENT, "Cancelling launch...");

            _cancelTokenSource.Cancel();

            if (_isInstalling)
            {
                try
                {
                    // clean up install
                    if (Directory.Exists(AppData.Directory))
                        Directory.Delete(AppData.Directory, true);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not fully clean up installation!");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            Dialog?.CloseBootstrapper();

            App.Terminate(ErrorCode.ERROR_CANCELLED);
        }
#endregion

        #region App Install
        private async Task<bool> CheckForUpdates()
        {
            const string LOG_IDENT = "Bootstrapper::CheckForUpdates";
            
            // don't update if there's another instance running (likely running in the background)
            // i don't like this, but there isn't much better way of doing it /shrug
            if (Process.GetProcessesByName(App.ProjectName).Length > 1)
            {
                App.Logger.WriteLine(LOG_IDENT, $"More than one Bloxstrap instance running, aborting update check");
                return false;
            }

            App.Logger.WriteLine(LOG_IDENT, "Checking for updates...");

#if !DEBUG_UPDATER
            var releaseInfo = await App.GetLatestRelease();

            if (releaseInfo is null)
                return false;

            var versionComparison = Utilities.CompareVersions(App.Version, releaseInfo.TagName);

            // check if we aren't using a deployed build, so we can update to one if a new version comes out
            if (App.IsProductionBuild && versionComparison == VersionComparison.Equal || versionComparison == VersionComparison.GreaterThan)
            {
                App.Logger.WriteLine(LOG_IDENT, "No updates found");
                return false;
            }

            string version = releaseInfo.TagName;
#else
            string version = App.Version;
#endif

            if (Environment.ProcessPath is not null && Environment.ProcessPath != Paths.Application)
            {
                // in case the user is reinstalling
                if (File.Exists(Paths.Application) && App.IsFirstRun)
                {
                    Filesystem.AssertReadOnly(Paths.Application);
                    File.Delete(Paths.Application);
                }

                // check to make sure bootstrapper is in the install folder
                if (!File.Exists(Paths.Application))
                    File.Copy(Environment.ProcessPath, Paths.Application);
            }

            // this SHOULD go under Register(),
            // but then people who have Bloxstrap v1.0.0 installed won't have this without a reinstall
            // maybe in a later version?
            if (!Directory.Exists(Paths.StartMenu))
            {
                Directory.CreateDirectory(Paths.StartMenu);
            }
            else
            {
                // v2.0.0 - rebadge configuration menu as just "Bloxstrap Menu"
                string oldMenuShortcut = Path.Combine(Paths.StartMenu, $"Configure {App.ProjectName}.lnk");

                if (File.Exists(oldMenuShortcut))
                    File.Delete(oldMenuShortcut);
            }

            Utility.Shortcut.Create(Paths.Application, "", Path.Combine(Paths.StartMenu, "Play Roblox.lnk"));
            Utility.Shortcut.Create(Paths.Application, "-menu", Path.Combine(Paths.StartMenu, $"{App.ProjectName} Menu.lnk"));
#if STUDIO_FEATURES
            Utility.Shortcut.Create(Paths.Application, "-ide", Path.Combine(Paths.StartMenu, $"Roblox Studio ({App.ProjectName}).lnk"));
#endif

            if (App.Settings.Prop.CreateDesktopIcon)
            {
                try
                {
                    Utility.Shortcut.Create(Paths.Application, "", Path.Combine(Paths.Desktop, "Play Roblox.lnk"));

                    // one-time toggle, set it back to false
                    App.Settings.Prop.CreateDesktopIcon = false;
                }
                catch (Exception)
                {
                    // suppress, we likely just don't have write perms for the desktop folder
                }
            }
        }

        private async Task CheckForUpdates()
        {
            const string LOG_IDENT = "Bootstrapper::CheckForUpdates";
            
            // don't update if there's another instance running (likely running in the background)
            if (Process.GetProcessesByName(App.ProjectName).Count() > 1)
            {
                App.Logger.WriteLine(LOG_IDENT, $"More than one Bloxstrap instance running, aborting update check");
                return;
            }

            App.Logger.WriteLine(LOG_IDENT, $"Checking for updates...");

            GithubRelease? releaseInfo;
            try
            {
                releaseInfo = await Http.GetJson<GithubRelease>($"https://api.github.com/repos/{App.ProjectRepository}/releases/latest");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to fetch releases: {ex}");
                return;
            }

            if (releaseInfo is null || releaseInfo.Assets is null)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No updates found");
                return;
            }

            int versionComparison = Utilities.CompareVersions(App.Version, releaseInfo.TagName);

            // check if we aren't using a deployed build, so we can update to one if a new version comes out
            if (versionComparison == 0 && App.BuildMetadata.CommitRef.StartsWith("tag") || versionComparison == 1)
            {
                App.Logger.WriteLine(LOG_IDENT, $"No updates found");
                return;
            }

            SetStatus(Strings.Bootstrapper_Status_UpgradingBloxstrap);
            
            try
            {
                // 64-bit is always the first option
                GithubReleaseAsset asset = releaseInfo.Assets[0];
                string downloadLocation = Path.Combine(Paths.LocalAppData, "Temp", asset.Name);

                App.Logger.WriteLine(LOG_IDENT, $"Downloading {releaseInfo.TagName}...");
                
                if (!File.Exists(downloadLocation))
                {
                    var response = await App.HttpClient.GetAsync(asset.BrowserDownloadUrl);

                    await using var fileStream = new FileStream(downloadLocation, FileMode.OpenOrCreate, FileAccess.Write);
                    await response.Content.CopyToAsync(fileStream);
                }
#endif

                App.Logger.WriteLine(LOG_IDENT, $"Starting {version}...");

                ProcessStartInfo startInfo = new()
                {
                    FileName = downloadLocation,
                };

                startInfo.ArgumentList.Add("-upgrade");

                foreach (string arg in App.LaunchSettings.Args)
                    startInfo.ArgumentList.Add(arg);

                if (_launchMode == LaunchMode.Player && !startInfo.ArgumentList.Contains("-player"))
                    startInfo.ArgumentList.Add("-player");
                else if (_launchMode == LaunchMode.Studio && !startInfo.ArgumentList.Contains("-studio"))
                    startInfo.ArgumentList.Add("-studio");

                App.Settings.Save();

                new InterProcessLock("AutoUpdater");
                
                Process.Start(startInfo);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "An exception occurred when running the auto-updater");
                App.Logger.WriteException(LOG_IDENT, ex);

                Frontend.ShowMessageBox(
                    string.Format(Strings.Bootstrapper_AutoUpdateFailed, releaseInfo.TagName),
                    MessageBoxImage.Information
                );
            }
        }

        private void Uninstall()
        {
            const string LOG_IDENT = "Bootstrapper::Uninstall";
            
            // prompt to shutdown roblox if its currently running
            if (Process.GetProcessesByName(App.RobloxPlayerAppName).Any() || Process.GetProcessesByName(App.RobloxStudioAppName).Any())
            {
                App.Logger.WriteLine(LOG_IDENT, $"Prompting to shut down all open Roblox instances");
                
                MessageBoxResult result = Frontend.ShowMessageBox(
                    Strings.Bootstrapper_Uninstall_RobloxRunning,
                    MessageBoxImage.Information,
                    MessageBoxButton.OKCancel
                );

                if (result != MessageBoxResult.OK)
                    App.Terminate(ErrorCode.ERROR_CANCELLED);

                try
                {
                    foreach (Process process in Process.GetProcessesByName(App.RobloxPlayerAppName))
                    {
                        process.Kill();
                        process.Close();
                    }

#if STUDIO_FEATURES
                    foreach (Process process in Process.GetProcessesByName(App.RobloxStudioAppName))
                    {
                        process.Kill();
                        process.Close();
                    }
#endif
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Failed to close process! {ex}");
                }

                App.Logger.WriteLine(LOG_IDENT, $"All Roblox processes closed");
            }
            
            SetStatus(Strings.Bootstrapper_Status_Uninstalling);

            App.ShouldSaveConfigs = false;
            bool robloxPlayerStillInstalled = true;
            bool robloxStudioStillInstalled = true;

            // check if stock bootstrapper is still installed
            using RegistryKey? bootstrapperKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-player");
            if (bootstrapperKey is null)
            {
                robloxPlayerStillInstalled = false;

                ProtocolHandler.Unregister("roblox");
                ProtocolHandler.Unregister("roblox-player");
            }
            else
            {
                // revert launch uri handler to stock bootstrapper

                string bootstrapperLocation = (string?)bootstrapperKey.GetValue("InstallLocation") + "RobloxPlayerLauncher.exe";

                ProtocolHandler.Register("roblox", "Roblox", bootstrapperLocation);
                ProtocolHandler.Register("roblox-player", "Roblox", bootstrapperLocation);
            }

#if STUDIO_FEATURES
            using RegistryKey? studioBootstrapperKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\roblox-studio");
            if (studioBootstrapperKey is null)
            {
                robloxStudioStillInstalled = false;

                ProtocolHandler.Unregister("roblox-studio");
                ProtocolHandler.Unregister("roblox-studio-auth");

                ProtocolHandler.Unregister("Roblox.Place");
                ProtocolHandler.Unregister(".rbxl");
                ProtocolHandler.Unregister(".rbxlx");
            }
            else
            {
                string studioLocation = (string?)studioBootstrapperKey.GetValue("InstallLocation") + "RobloxStudioBeta.exe"; // points to studio exe instead of bootstrapper
                ProtocolHandler.Register("roblox-studio", "Roblox", studioLocation);
                ProtocolHandler.Register("roblox-studio-auth", "Roblox", studioLocation);

                ProtocolHandler.RegisterRobloxPlace(studioLocation);
            }
#endif

            // if the folder we're installed to does not end with "Bloxstrap", we're installed to a user-selected folder
            // in which case, chances are they chose to install to somewhere they didn't really mean to (prior to the added warning in 2.4.0)
            // if so, we're walking on eggshells and have to ensure we only clean up what we need to clean up
            bool cautiousUninstall = !Paths.Base.ToLower().EndsWith(App.ProjectName.ToLower());

            var cleanupSequence = new List<Action>
            {
                () => Registry.CurrentUser.DeleteSubKey($@"Software\{App.ProjectName}"),
                () => Directory.Delete(Paths.StartMenu, true),
                () => File.Delete(Path.Combine(Paths.Desktop, "Play Roblox.lnk")),
                () => Registry.CurrentUser.DeleteSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{App.ProjectName}")
            };

            if (cautiousUninstall)
            {
                cleanupSequence.Add(() => Directory.Delete(Paths.Downloads, true));
                cleanupSequence.Add(() => Directory.Delete(Paths.Modifications, true));
                cleanupSequence.Add(() => Directory.Delete(Paths.Versions, true));
                cleanupSequence.Add(() => Directory.Delete(Paths.Logs, true));
                
                cleanupSequence.Add(() => File.Delete(App.Settings.FileLocation));
                cleanupSequence.Add(() => File.Delete(App.State.FileLocation));
            }
            else
            {
                cleanupSequence.Add(() => Directory.Delete(Paths.Base, true));
            }

            string robloxFolder = Path.Combine(Paths.LocalAppData, "Roblox");

            if (Directory.Exists(AppData.Directory))
            {
                try
                {
                    // gross hack to see if roblox is still running
                    // i don't want to rely on mutexes because they can change, and will false flag for
                    // running installations that are not by bloxstrap
                    File.Delete(AppData.ExecutablePath);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not delete executable/folder, Roblox may still be running. Aborting update.");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }

            Action? callback = null;

            if (Directory.Exists(Paths.Base))
            {
                callback = delegate
                {
                    // this is definitely one of the workaround hacks of all time
                    // could antiviruses falsely detect this as malicious behaviour though?
                    // "hmm whats this program doing running a cmd command chain quietly in the background that auto deletes an entire folder"

                    string deleteCommand;

                    if (cautiousUninstall)
                        deleteCommand = $"del /Q \"{Paths.Application}\"";
                    else
                        deleteCommand = $"del /Q \"{Paths.Base}\\*\" && rmdir \"{Paths.Base}\"";

                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout 5 && {deleteCommand}",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                };
            }

            Dialog?.ShowSuccess(Strings.Bootstrapper_SuccessfullyUninstalled, callback);
        }
#endregion

        #region Roblox Install
        private async Task InstallLatestVersion()
        {
            const string LOG_IDENT = "Bootstrapper::InstallLatestVersion";
            
            _isInstalling = true;

            SetStatus(FreshInstall ? Strings.Bootstrapper_Status_Installing : Strings.Bootstrapper_Status_Upgrading);

            Directory.CreateDirectory(Paths.Base);
            Directory.CreateDirectory(Paths.Downloads);
            Directory.CreateDirectory(Paths.Versions);

            // package manifest states packed size and uncompressed size in exact bytes
            int totalSizeRequired = 0;

            // packed size only matters if we don't already have the package cached on disk
            totalSizeRequired += _versionPackageManifest.Where(x => !cachedPackageHashes.Contains(x.Signature)).Sum(x => x.PackedSize);
            totalSizeRequired += _versionPackageManifest.Sum(x => x.Size);
            
            if (Filesystem.GetFreeDiskSpace(Paths.Base) < totalSizeRequired)
            {
                Frontend.ShowMessageBox(
                    Strings.Bootstrapper_NotEnoughSpace, 
                    MessageBoxImage.Error
                );

                App.Terminate(ErrorCode.ERROR_INSTALL_FAILURE);
                return;
            }

            if (Dialog is not null)
            {
                // TODO: cancelling needs to always be enabled
                Dialog.CancelEnabled = true;
                Dialog.ProgressStyle = ProgressBarStyle.Continuous;

                Dialog.ProgressMaximum = ProgressBarMaximum;

                // compute total bytes to download
                _progressIncrement = (double)ProgressBarMaximum / _versionPackageManifest.Sum(package => package.PackedSize);
            }

            var extractionTasks = new List<Task>();

            foreach (var package in _versionPackageManifest)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                // download all the packages synchronously
                await DownloadPackage(package);

                // we'll extract the runtime installer later if we need to
                if (package.Name == "WebView2RuntimeInstaller.zip")
                    continue;

                // extract the package async immediately after download
                extractionTasks.Add(Task.Run(() => ExtractPackage(package), _cancelTokenSource.Token));
            }

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (Dialog is not null)
            {
                // allow progress bar to 100% before continuing (purely ux reasons lol)
                // TODO: come up with a better way of handling this that is non-blocking
                await Task.Delay(1000);

                Dialog.ProgressStyle = ProgressBarStyle.Marquee;
                SetStatus(Strings.Bootstrapper_Status_Configuring);
            }

            await Task.WhenAll(extractionTasks);
            
            App.Logger.WriteLine(LOG_IDENT, "Writing AppSettings.xml...");
            await File.WriteAllTextAsync(Path.Combine(AppData.Directory, "AppSettings.xml"), AppSettings);

            if (_cancelTokenSource.IsCancellationRequested)
                return;

            if (App.State.Prop.PromptWebView2Install)
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                using var hkcuKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");

                if (hklmKey is not null || hkcuKey is not null)
                {
                    // reset prompt state if the user has it installed
                    App.State.Prop.PromptWebView2Install = true;
                }   
                else
                {
                    var result = Frontend.ShowMessageBox(Strings.Bootstrapper_WebView2NotFound, MessageBoxImage.Warning, MessageBoxButton.YesNo, MessageBoxResult.Yes);

                    if (result != MessageBoxResult.Yes)
                    {
                        App.State.Prop.PromptWebView2Install = false;
                    }
                    else
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Installing WebView2 runtime...");

                        var package = _versionPackageManifest.Find(x => x.Name == "WebView2RuntimeInstaller.zip");

                        if (package is null)
                        {
                            App.Logger.WriteLine(LOG_IDENT, "Aborted runtime install because package does not exist, has WebView2 been added in this Roblox version yet?");
                            return;
                        }

                        string baseDirectory = Path.Combine(AppData.Directory, AppData.PackageDirectoryMap[package.Name]);

            SetStatus(Strings.Bootstrapper_Status_InstallingWebView2);

                        SetStatus(Strings.Bootstrapper_Status_InstallingWebView2);

                        var startInfo = new ProcessStartInfo()
                        {
                            WorkingDirectory = baseDirectory,
                            FileName = Path.Combine(baseDirectory, "MicrosoftEdgeWebview2Setup.exe"),
                            Arguments = "/silent /install"
                        };

                        await Process.Start(startInfo)!.WaitForExitAsync();

            App.Logger.WriteLine(LOG_IDENT, "Finished installing runtime");
        }

        public static void MigrateIntegrations()
        {
            // v2.2.0 - remove rbxfpsunlocker
            string rbxfpsunlocker = Path.Combine(Paths.Integrations, "rbxfpsunlocker");

            if (Directory.Exists(rbxfpsunlocker))
                Directory.Delete(rbxfpsunlocker, true);

            // v2.3.0 - remove reshade
            string injectorLocation = Path.Combine(Paths.Modifications, "dxgi.dll");
            string configLocation = Path.Combine(Paths.Modifications, "ReShade.ini");

            if (File.Exists(injectorLocation))
            {
                Frontend.ShowMessageBox(
                    Strings.Bootstrapper_HyperionUpdateInfo,
                    MessageBoxImage.Warning
                );

                File.Delete(injectorLocation);
            }

            if (File.Exists(configLocation))
                File.Delete(configLocation);
        }

        private async Task ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";
            
            if (Process.GetProcessesByName(_playerFileName[..^4]).Any())
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox is running, aborting mod check");
                return;
            }

            SetStatus(Strings.Bootstrapper_Status_ApplyingModifications);

            // set executable flags for fullscreen optimizations
            App.Logger.WriteLine(LOG_IDENT, "Checking executable flags...");
            using (RegistryKey appFlagsKey = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers"))
            {
                string flag = " DISABLEDXMAXIMIZEDWINDOWEDMODE";
                string? appFlags = (string?)appFlagsKey.GetValue(_playerLocation);

                if (App.Settings.Prop.DisableFullscreenOptimizations)
                {
                    if (appFlags is null)
                        appFlagsKey.SetValue(_playerLocation, $"~{flag}");
                    else if (!appFlags.Contains(flag))
                        appFlagsKey.SetValue(_playerLocation, appFlags + flag);
                }
                else if (appFlags is not null && appFlags.Contains(flag))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Deleting unused package {hash}");
                        
                    try
                    {
                        File.Delete(Path.Combine(Paths.Downloads, hash));
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Failed to delete {hash}!");
                        App.Logger.WriteException(LOG_IDENT, ex);
                    }
                }
            }

            App.Logger.WriteLine(LOG_IDENT, "Registering approximate program size...");

            int distributionSize = _versionPackageManifest.Sum(x => x.Size + x.PackedSize) / 1024;

            AppData.State.Size = distributionSize;

            int totalSize = App.State.Prop.Player.Size + App.State.Prop.Studio.Size;

            using (var uninstallKey = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                uninstallKey.SetValueSafe("EstimatedSize", totalSize);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Registered as {totalSize} KB");

            App.State.Save();

            lockFile.Delete();

            if (Dialog is not null)
                Dialog.CancelEnabled = false;

            _isInstalling = false;
        }

        private async Task ApplyModifications()
        {
            const string LOG_IDENT = "Bootstrapper::ApplyModifications";

            SetStatus(Strings.Bootstrapper_Status_ApplyingModifications);

            // handle file mods
            App.Logger.WriteLine(LOG_IDENT, "Checking file mods...");

            // manifest has been moved to State.json
            File.Delete(Path.Combine(Paths.Base, "ModManifest.txt"));

            List<string> modFolderFiles = new();

            Directory.CreateDirectory(Paths.Modifications);

            // check custom font mod
            // instead of replacing the fonts themselves, we'll just alter the font family manifests

            string modFontFamiliesFolder = Path.Combine(Paths.Modifications, "content\\fonts\\families");

            if (File.Exists(Paths.CustomFont))
            {
                App.Logger.WriteLine(LOG_IDENT, "Begin font check");

                Directory.CreateDirectory(modFontFamiliesFolder);

                const string path = "rbxasset://fonts/CustomFont.ttf";

                foreach (string jsonFilePath in Directory.GetFiles(Path.Combine(AppData.Directory, "content\\fonts\\families")))
                {
                    string jsonFilename = Path.GetFileName(jsonFilePath);
                    string modFilepath = Path.Combine(modFontFamiliesFolder, jsonFilename);

                    if (File.Exists(modFilepath))
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, $"Setting font for {jsonFilename}");

                    var fontFamilyData = JsonSerializer.Deserialize<FontFamily>(File.ReadAllText(jsonFilePath));

                    if (fontFamilyData is null)
                        continue;

                    bool shouldWrite = false;

                    foreach (var fontFace in fontFamilyData.Faces)
                    {
                        if (fontFace.AssetId != path)
                        {
                            fontFace.AssetId = path;
                            shouldWrite = true;
                        }
                    }

                    if (shouldWrite)
                        File.WriteAllText(modFilepath, JsonSerializer.Serialize(fontFamilyData, new JsonSerializerOptions { WriteIndented = true }));
                }

                App.Logger.WriteLine(LOG_IDENT, "End font check");
            }
            else if (Directory.Exists(modFontFamiliesFolder))
            {
                Directory.Delete(modFontFamiliesFolder, true);
            }

            foreach (string file in Directory.GetFiles(Paths.Modifications, "*.*", SearchOption.AllDirectories))
            {
                // get relative directory path
                string relativeFile = file.Substring(Paths.Modifications.Length + 1);

                // v1.7.0 - README has been moved to the preferences menu now
                if (relativeFile == "README.txt")
                {
                    File.Delete(file);
                    continue;
                }

                if (!App.Settings.Prop.UseFastFlagManager && String.Equals(relativeFile, "ClientSettings\\ClientAppSettings.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (relativeFile.EndsWith(".lock"))
                    continue;

                modFolderFiles.Add(relativeFile);

                string fileModFolder = Path.Combine(Paths.Modifications, relativeFile);
                string fileVersionFolder = Path.Combine(AppData.Directory, relativeFile);

                if (File.Exists(fileVersionFolder) && MD5Hash.FromFile(fileModFolder) == MD5Hash.FromFile(fileVersionFolder))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{relativeFile} already exists in the version folder, and is a match");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fileVersionFolder)!);

                Filesystem.AssertReadOnly(fileVersionFolder);
                File.Copy(fileModFolder, fileVersionFolder, true);
                Filesystem.AssertReadOnly(fileVersionFolder);

                App.Logger.WriteLine(LOG_IDENT, $"{relativeFile} has been copied to the version folder");
            }

            // the manifest is primarily here to keep track of what files have been
            // deleted from the modifications folder, so that we know when to restore the original files from the downloaded packages
            // now check for files that have been deleted from the mod folder according to the manifest

            var fileRestoreMap = new Dictionary<string, List<string>>();

            foreach (string fileLocation in App.State.Prop.ModManifest)
            {
                if (modFolderFiles.Contains(fileLocation))
                    continue;

                var packageMapEntry = AppData.PackageDirectoryMap.SingleOrDefault(x => !String.IsNullOrEmpty(x.Value) && fileLocation.StartsWith(x.Value));
                string packageName = packageMapEntry.Key;

                // package doesn't exist, likely mistakenly placed file
                if (String.IsNullOrEmpty(packageName))
                {
                    App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod but does not belong to a package");

                    string versionFileLocation = Path.Combine(AppData.Directory, fileLocation);

                    if (File.Exists(versionFileLocation))
                        File.Delete(versionFileLocation);

                    continue;
                }

                string fileName = fileLocation.Substring(packageMapEntry.Value.Length);

                if (!fileRestoreMap.ContainsKey(packageName))
                    fileRestoreMap[packageName] = new();

                fileRestoreMap[packageName].Add(fileName);

                App.Logger.WriteLine(LOG_IDENT, $"{fileLocation} was removed as a mod, restoring from {packageName}");
            }

            foreach (var entry in fileRestoreMap)
            {
                var package = _versionPackageManifest.Find(x => x.Name == entry.Key);

                if (package is not null)
                {
                    await DownloadPackage(package);
                    ExtractPackage(package, entry.Value);
                }
            }

            App.State.Prop.ModManifest = modFolderFiles;
            App.State.Save();

            App.Logger.WriteLine(LOG_IDENT, $"Finished checking file mods");
        }

        private async Task DownloadPackage(Package package)
        {
            string LOG_IDENT = $"Bootstrapper::DownloadPackage.{package.Name}";
            
            if (_cancelTokenSource.IsCancellationRequested)
                return;

            string packageUrl = RobloxDeployment.GetLocation($"/{_latestVersionGuid}-{package.Name}");
            string robloxPackageLocation = Path.Combine(Paths.LocalAppData, "Roblox", "Downloads", package.Signature);

            if (File.Exists(package.DownloadPath))
            {
                var file = new FileInfo(package.DownloadPath);

                string calculatedMD5 = MD5Hash.FromFile(package.DownloadPath);

                if (calculatedMD5 != package.Signature)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is corrupted ({calculatedMD5} != {package.Signature})! Deleting and re-downloading...");
                    file.Delete();
                }
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Package is already downloaded, skipping...");

                    _totalDownloadedBytes += package.PackedSize;
                    UpdateProgressBar();

                    return;
                }
            }
            else if (File.Exists(robloxPackageLocation))
            {
                // let's cheat! if the stock bootstrapper already previously downloaded the file,
                // then we can just copy the one from there

                App.Logger.WriteLine(LOG_IDENT, $"Found existing copy at '{robloxPackageLocation}'! Copying to Downloads folder...");
                File.Copy(robloxPackageLocation, package.DownloadPath);

                _totalDownloadedBytes += package.PackedSize;
                UpdateProgressBar();

                return;
            }

            if (File.Exists(package.DownloadPath))
                return;

            // TODO: telemetry for this. chances are that this is completely unnecessary and that it can be removed.
            // but, we need to ensure this doesn't work before we can do that.

            const int maxTries = 5;

            bool statIsRetrying = false;
            bool statIsHttp = false;

            App.Logger.WriteLine(LOG_IDENT, "Downloading...");

            var buffer = new byte[4096];

            for (int i = 1; i <= maxTries; i++)
            {
                if (_cancelTokenSource.IsCancellationRequested)
                    return;

                int totalBytesRead = 0;

                try
                {
                    var response = await App.HttpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, _cancelTokenSource.Token);
                    await using var stream = await response.Content.ReadAsStreamAsync(_cancelTokenSource.Token);
                    await using var fileStream = new FileStream(package.DownloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);

                    while (true)
                    {
                        if (_cancelTokenSource.IsCancellationRequested)
                        {
                            stream.Close();
                            fileStream.Close();
                            return;
                        }

                        int bytesRead = await stream.ReadAsync(buffer, _cancelTokenSource.Token);

                        if (bytesRead == 0)
                            break;

                        totalBytesRead += bytesRead;

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cancelTokenSource.Token);

                        _totalDownloadedBytes += bytesRead;
                        UpdateProgressBar();
                    }

                    string hash = MD5Hash.FromStream(fileStream);

                    if (hash != package.Signature)
                        throw new ChecksumFailedException($"Failed to verify download of {packageUrl}\n\nExpected hash: {package.Signature}\nGot hash: {hash}");

                    App.Logger.WriteLine(LOG_IDENT, $"Finished downloading! ({totalBytesRead} bytes total)");
                    break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"An exception occurred after downloading {totalBytesRead} bytes. ({i}/{maxTries})");
                    App.Logger.WriteException(LOG_IDENT, ex);

                    statIsRetrying = true;

                    if (ex.GetType() == typeof(ChecksumFailedException))
                    {
                        _ = App.HttpClient.GetAsync($"http://bloxstraplabs.com/metrics/post?key=packageDownloadState&value=httpFail");

                        Frontend.ShowConnectivityDialog(
                            Strings.Dialog_Connectivity_UnableToDownload,
                            String.Format(Strings.Dialog_Connectivity_UnableToDownloadReason, "[https://github.com/pizzaboxer/bloxstrap/wiki/Bloxstrap-is-unable-to-download-Roblox](https://github.com/pizzaboxer/bloxstrap/wiki/Bloxstrap-is-unable-to-download-Roblox)"),
                            MessageBoxImage.Error,
                            ex
                        );

                        App.Terminate(ErrorCode.ERROR_CANCELLED);
                    }
                    else if (i >= maxTries)
                        throw;

                    if (File.Exists(package.DownloadPath))
                        File.Delete(package.DownloadPath);

                    _totalDownloadedBytes -= totalBytesRead;
                    UpdateProgressBar();

                    // attempt download over HTTP
                    // this isn't actually that unsafe - signatures were fetched earlier over HTTPS
                    // so we've already established that our signatures are legit, and that there's very likely no MITM anyway
                    if (ex.GetType() == typeof(IOException) && !packageUrl.StartsWith("http://"))
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Retrying download over HTTP...");
                        packageUrl = packageUrl.Replace("https://", "http://");
                        statIsHttp = true;
                    }
                }
            }

            if (statIsRetrying)
            {
                string stat = statIsHttp ? "httpSuccess" : "retrySuccess";
                _ = App.HttpClient.GetAsync($"http://bloxstraplabs.com/metrics/post?key=packageDownloadState&value={stat}");
            }
        }

        private void ExtractPackage(Package package, List<string>? files = null)
        {
            const string LOG_IDENT = "Bootstrapper::ExtractPackage";

            string packageFolder = Path.Combine(AppData.Directory, AppData.PackageDirectoryMap[package.Name]);
            string? fileFilter = null;

            // for sharpziplib, each file in the filter 
            if (files is not null)
            {
                var regexList = new List<string>();

                foreach (string file in files)
                    regexList.Add("^" + file.Replace("\\", "\\\\") + "$");

                fileFilter = String.Join(';', regexList);
            }

            App.Logger.WriteLine(LOG_IDENT, $"Extracting {package.Name}...");

            var fastZip = new ICSharpCode.SharpZipLib.Zip.FastZip();
            fastZip.ExtractZip(package.DownloadPath, packageFolder, fileFilter);

            App.Logger.WriteLine(LOG_IDENT, $"Finished extracting {package.Name}");
        }
        #endregion
    }
}
