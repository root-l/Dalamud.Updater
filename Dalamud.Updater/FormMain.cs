using AutoUpdaterDotNET;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using XIVLauncher.Common.Dalamud;

namespace Dalamud.Updater
{
    public partial class FormMain : Form
    {
        private const string UPDATEURL = "https://raw.githubusercontent.com/dohwacorp/DalamudResource/main/UpdaterVersionInfo";
        private const string OTTERHOME = "문의 : 달라가브 KR 디스코드 https://discord.gg/Fdb9TTW9aD";

        // private List<string> pidList = new List<string>();
        private bool firstHideHint = true;
        private bool isThreadRunning = true;
        private bool dotnetDownloadFinished = false;
        private bool desktopDownloadFinished = false;
        private Config config;
        private DalamudLoadingOverlay dalamudLoadingOverlay;

        private readonly DirectoryInfo addonDirectory;
        private readonly DirectoryInfo runtimeDirectory;
        private readonly DirectoryInfo xivlauncherDirectory;
        private readonly DirectoryInfo assetDirectory;
        private readonly DirectoryInfo configDirectory;

        private readonly DalamudUpdater dalamudUpdater;

        public string windowsTitle = "Dalamud Updater KR v" + Assembly.GetExecutingAssembly().GetName().Version;

        private void CheckUpdate()
        {

            dalamudUpdater.Run();
        }

        private Version GetUpdaterVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        private string getVersion()
        {
            var rgx = new Regex(@"^\d+\.\d+\.\d+\.\d+$");
            var stgRgx = new Regex(@"^[\da-zA-Z]{8}$");
            var di = new DirectoryInfo(Path.Combine(addonDirectory.FullName, "Hooks"));
            var version = new Version("0.0.0.0");
            if (!di.Exists)
                return version.ToString();
            var dirs = di.GetDirectories("*", SearchOption.TopDirectoryOnly).Where(dir => rgx.IsMatch(dir.Name)).ToArray();
            bool releaseVersionExists = false;
            foreach (var dir in dirs)
            {
                var newVersion = new Version(dir.Name);
                if (newVersion > version)
                {
                    releaseVersionExists = true;
                    version = newVersion;
                }
            }
            if (!releaseVersionExists)
            {
                var stgDirs = di.GetDirectories("*", SearchOption.TopDirectoryOnly).Where(dir => stgRgx.IsMatch(dir.Name)).ToArray();
                if (stgDirs.Length > 0)
                {
                    return stgDirs[0].Name;
                }
            }
            return version.ToString();
        }


        public FormMain()
        {
            InitLogging();
            InitializeComponent();
            InitializePIDCheck();
            InitializeDeleteShit();
            addonDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
            dalamudLoadingOverlay = new DalamudLoadingOverlay(this);
            dalamudLoadingOverlay.OnProgressBar += setProgressBar;
            dalamudLoadingOverlay.OnSetVisible += setVisible;
            dalamudLoadingOverlay.OnStatusLabel += setStatus;
            addonDirectory = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher", "addon"));
            runtimeDirectory = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher", "runtime"));
            xivlauncherDirectory = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher"));
            assetDirectory = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher", "dalamudAssets"));
            configDirectory = new DirectoryInfo(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "XIVLauncher"));
            //labelVersion.Text = string.Format("卫月版本 : {0}", getVersion());
            string[] strArgs = Environment.GetCommandLineArgs();
            if (strArgs.Length >= 2 && strArgs[1].Equals("-startup"))
            {
                //this.WindowState = FormWindowState.Minimized;
                //this.ShowInTaskbar = false;
                if (firstHideHint)
                {
                    firstHideHint = false;
                    this.DalamudUpdaterIcon.ShowBalloonTip(2000, "자동 시작", "백그라운드에서 자동으로 시작했습니다.", ToolTipIcon.Info);
                }
            }
            dalamudUpdater = new DalamudUpdater(addonDirectory, runtimeDirectory, assetDirectory, configDirectory);
            dalamudUpdater.Overlay = dalamudLoadingOverlay;
            dalamudUpdater.OnUpdateEvent += DalamudUpdater_OnUpdateEvent;
            InitializeConfig();
            labelVer.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
            UpdateFormConfig();
            UpdateSelf();

            SetDalamudVersion();

            CheckUpdate();
        }

        private void DalamudUpdater_OnUpdateEvent(DalamudUpdater.DownloadState value)
        {
            switch (value)
            {
                case DalamudUpdater.DownloadState.Failed:
                    MessageBox.Show("Failed Update Dalamud", windowsTitle);
                    setStatus("Failed Update Dalamud");
                    break;
                case DalamudUpdater.DownloadState.Unknown:
                    setStatus("Unknown Error");
                    break;
                case DalamudUpdater.DownloadState.NoIntegrity:
                    setStatus("Version Imcompatible");
                    break;
                case DalamudUpdater.DownloadState.Done:
                    SetDalamudVersion();
                    setStatus("Success Update Dalamud");
                    break;
                case DalamudUpdater.DownloadState.Checking:
                    setStatus("Checking Update...");
                    break;
            }
        }

        public void SetDalamudVersion()
        {
            var verStr = string.Format("Dalamud KR : {0}", getVersion());
            if (this.labelVersion.InvokeRequired)
            {
                Action<string> actionDelegate = (x) => { labelVersion.Text = x; };
                this.labelVersion.Invoke(actionDelegate, verStr);
            }
            else
            {
                labelVersion.Text = verStr;
            }
        }
        #region init
        private static void InitLogging()
        {
            var baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var logPath = Path.Combine(baseDirectory, "Dalamud.Updater.log");

            var levelSwitch = new LoggingLevelSwitch();

#if DEBUG
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
#else
            levelSwitch.MinimumLevel = LogEventLevel.Information;
#endif


            Log.Logger = new LoggerConfiguration()
                //.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
                .WriteTo.Async(a => a.File(logPath))
                .MinimumLevel.ControlledBy(levelSwitch)
                .CreateLogger();
        }
        private void InitializeConfig()
        {
            this.config = Config.Load(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "DalamudUpdaterConfig.json"));
        }

        private void InitializeDeleteShit()
        {
            var shitConfig = Path.Combine(Directory.GetCurrentDirectory(), "Dalamud.Updater.exe.config");
            if (File.Exists(shitConfig))
            {
                File.Delete(shitConfig);
            }

            var shitInjector = Path.Combine(Directory.GetCurrentDirectory(), "Dalamud.Injector.exe");
            if (File.Exists(shitInjector))
            {
                File.Delete(shitInjector);
            }

            var shitDalamud = Path.Combine(Directory.GetCurrentDirectory(), "6.3.0.9");
            if (Directory.Exists(shitDalamud))
            {
                Directory.Delete(shitDalamud, true);
            }

            var shitUIRes = Path.Combine(Directory.GetCurrentDirectory(), "XIVLauncher", "dalamudAssets", "UIRes");
            if (Directory.Exists(shitUIRes))
            {
                Directory.Delete(shitUIRes, true);
            }

            var shitAddon = Path.Combine(Directory.GetCurrentDirectory(), "addon");
            if (Directory.Exists(shitAddon))
            {
                Directory.Delete(shitAddon, true);
            }

            var shitRuntime = Path.Combine(Directory.GetCurrentDirectory(), "runtime");
            if (Directory.Exists(shitRuntime))
            {
                Directory.Delete(shitRuntime, true);
            }
        }

        private void InitializePIDCheck()
        {
            var thread = new Thread(() =>
            {
                while (this.isThreadRunning)
                {
                    try
                    {
                        //var newPidList = Process.GetProcessesByName("ffxiv_dx11").Where(process =>
                        //{
                        //    return !process.MainWindowTitle.Contains("FINAL FANTASY XIV");
                        //}).ToList().ConvertAll(process => process.Id.ToString()).ToArray();
                        //为什么我开了FF检测不到啊.jpg
                        var newPidList = Process.GetProcesses().Where(process =>{
                            return process.ProcessName == "ffxiv_dx11" || process.ProcessName == "ffxiv";
                        }).ToList().ConvertAll(process => process.Id.ToString()).ToArray();
                        var newHash = String.Join(", ", newPidList).GetHashCode();
                        var oldPidList = this.comboBoxFFXIV.Items.Cast<Object>().Select(item => item.ToString()).ToArray();
                        var oldHash = String.Join(", ", oldPidList).GetHashCode();
                        if (oldHash != newHash && this.comboBoxFFXIV.IsHandleCreated)
                        {
                            this.comboBoxFFXIV.Invoke((MethodInvoker)delegate
                            {
                                // Running on the UI thread
                                comboBoxFFXIV.Items.Clear();
                                comboBoxFFXIV.Items.AddRange(newPidList.Cast<object>().ToArray());
                                if (!comboBoxFFXIV.DroppedDown && comboBoxFFXIV.Items.Count > 0)
                                {
                                    this.comboBoxFFXIV.SelectedIndex = 0;
                                }
                            });

                            if (newPidList.Length > 0 &&this.checkBoxAutoInject.Checked)
                            {
                                while (dalamudUpdater.State != DalamudUpdater.DownloadState.Done)
                                {
                                    Thread.Sleep(1000);
                                }

                                foreach (var pidStr in newPidList)
                                {
                                    var pid = int.Parse(pidStr);
                                    if (Process.GetProcessById(pid).ProcessName != "ffxiv_dx11")
                                    {
                                        this.DalamudUpdaterIcon.ShowBalloonTip(2000, "자동 적용 실패", $"Process ID : {pid}는 DirectX11 버전이 아닙니다.", ToolTipIcon.Warning);
                                        Log.Information("{pid} is not dx11", pid);
                                        continue;
                                    }
                                    if (this.Inject(pid, (int)(this.config.InjectDelaySeconds * 1000)))
                                    {
                                        this.DalamudUpdaterIcon.ShowBalloonTip(2000, "자동 적용", $"Process ID : {pid}, 적용 완료.", ToolTipIcon.Info);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {

                    }
                    Thread.Sleep(1000);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        #endregion
        private void FormMain_Load(object sender, EventArgs e)
        {
        }

        private void UpdateFormConfig()
        {
            this.checkBoxAutoInject.Checked = this.config.AutoInject.Value;
            this.checkBoxAutoStart.Checked = this.config.AutoStart.Value;
            this.delayBox.Value = (decimal)this.config.InjectDelaySeconds;
            this.checkBoxSafeMode.Checked = this.config.SafeMode.Value;
        }

        private void UpdateSelf()
        {
            AutoUpdater.ApplicationExitEvent += () =>
            {
                this.Text = @"Closing application...";
                Thread.Sleep(5000);
                this.Dispose();
                this.DalamudUpdaterIcon.Dispose();
                Application.Exit();
            };
            //AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.InstalledVersion = GetUpdaterVersion();
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.UpdateMode = Mode.Normal;
            try
            {
                AutoUpdater.ParseUpdateInfoEvent += (args) =>
                {
                    try
                    {
#if DEBUG
                        var json = JsonConvert.DeserializeObject<VersionInfo>(File.ReadAllText(@"version.json"), new JsonSerializerSettings
                        {
                            TypeNameHandling = TypeNameHandling.All,
                            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                            Formatting = Formatting.Indented,
                            NullValueHandling = NullValueHandling.Ignore,

                        });
#else
                        var json = JsonConvert.DeserializeObject<VersionInfo>(args.RemoteData, new JsonSerializerSettings
                        {
                            TypeNameHandling = TypeNameHandling.All,
                            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                            Formatting = Formatting.Indented,
                            NullValueHandling = NullValueHandling.Ignore,
                        });
#endif
                        if (json.Version == null || json.DownloadUrl == null)
                        {
                            throw new Exception($"업데이트 정보 확인 실패:\n {args.RemoteData}");
                        }

                        args.UpdateInfo = new UpdateInfoEventArgs
                        {
                            CurrentVersion = json.Version,
                            ChangelogURL = json.ChangeLog,
                            DownloadURL = json.DownloadUrl,
                        };
                        if (json.Config != null && this.config != null)
                        {
                            var type = typeof(Config);
                            foreach (var property in type.GetProperties())
                            {
                                //if (property.Name.Equals())
                                var remoteValue = property.GetValue(json.Config, null);
                                if (remoteValue != null)
                                {
                                    property.SetValue(this.config, remoteValue);
                                    Log.Information($"Change config {property.Name} value: {property.GetValue(this.config)} -> {remoteValue}");
                                }
                            }
                            this.checkBoxSafeMode.Invoke(UpdateFormConfig);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"{ex.Message}\n\n{OTTERHOME}", "업데이트 실패",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };
                //AutoUpdater.ShowUpdateForm();
                AutoUpdater.Start(UPDATEURL);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex.Message}\n\n{OTTERHOME}", "업데이트 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private void FormMain_Disposed(object sender, EventArgs e)
        {
            this.isThreadRunning = false;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            //this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            //this.ShowInTaskbar = false;
            //this.Visible = false;
            if (firstHideHint)
            {
                firstHideHint = false;
                this.DalamudUpdaterIcon.ShowBalloonTip(2000, "달라가브 최소화", "트레이 아이콘을 눌러 메뉴를 열 수 있습니다.", ToolTipIcon.Info);
            }
        }

        private void DalamudUpdaterIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                //if (this.WindowState == FormWindowState.Minimized)
                //{
                //    this.WindowState = FormWindowState.Normal;
                //    this.FormBorderStyle = FormBorderStyle.FixedDialog;
                //    this.ShowInTaskbar = true;
                //}
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.Show();
                    this.WindowState = FormWindowState.Normal;
                }
                this.Activate();
            }
        }

        private void MenuToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //WindowState = FormWindowState.Normal;
            if (!this.Visible) this.Visible = true;
            this.Activate();
        }
        private void QuitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Dispose();
            //this.Close();
            this.DalamudUpdaterIcon.Dispose();
            Application.Exit();
        }

        private void ButtonCheckForUpdate_Click(object sender, EventArgs e)
        {
            if (this.comboBoxFFXIV.SelectedItem != null)
            {
                var pid = int.Parse((string)this.comboBoxFFXIV.SelectedItem);
                var process = Process.GetProcessById(pid);
                if (isInjected(process))
                {
                    var choice = MessageBox.Show("이미 달라가브가 적용되어있습니다.\n업데이트 확인을 위해서 게임을 종료하시겠습니까?", windowsTitle,
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);
                    if (choice == DialogResult.Yes)
                    {
                        process.Kill();
                    }
                    else
                    {
                        return;
                    }
                }
            }
            CheckUpdate();
        }

        private void comboBoxFFXIV_Clicked(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://discord.gg/Fdb9TTW9aD");
        }

        private DalamudStartInfo GeneratingDalamudStartInfo(Process process, string dalamudPath, int injectDelay)
        {
            var ffxivDir = Path.GetDirectoryName(process.MainModule.FileName);
            var appDataDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            var xivlauncherDir = xivlauncherDirectory.FullName;

            var gameVerStr = File.ReadAllText(Path.Combine(ffxivDir, "ffxivgame.ver"));

            var startInfo = new DalamudStartInfo
            {
                ConfigurationPath = Path.Combine(xivlauncherDir, "dalamudConfig.json"),
                PluginDirectory = Path.Combine(xivlauncherDir, "installedPlugins"),
                DefaultPluginDirectory = Path.Combine(xivlauncherDir, "devPlugins"),
                RuntimeDirectory = runtimeDirectory.FullName,
                AssetDirectory = this.dalamudUpdater.AssetDirectory.FullName,
                GameVersion = gameVerStr,
                Language = "4",
                OptOutMbCollection = false,
                WorkingDirectory = dalamudPath,
                DelayInitializeMs = injectDelay
            };

            return startInfo;
        }

        private bool isInjected(Process process)
        {
            try
            {
                for (var j = 0; j < process.Modules.Count; j++)
                {
                    if (process.Modules[j].ModuleName == "Dalamud.dll")
                    {
                        return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        private bool IsZombieProcess(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var mainModule = process.MainModule;
                var handle = SystemHelper.OpenProcess(0x001F0FFF, true, process.Id);
                if (handle == IntPtr.Zero)
                    throw new Exception("ERROR: OpenProcess()");
                SystemHelper.CloseHandle(handle);
            }
            catch (Exception ex)
            {
                MessageBox.Show("""
                    프로세스에 액세스 할 수 없습니다.
                    1. 보안 프로그램을 확인하고 Dalamud 프로그램을 제외해주세요.
                    2. 작업 관리자를 열고 완전히 종료되지 않거나 응답하지 않는 FFXIV 프로세스를 종료해주세요.
                    3. 컴퓨터를 다시 시작해 보세요.

                    """ + ex.Message, windowsTitle, MessageBoxButtons.YesNo);
                return true;
            }
            return false;
        }

        private bool Inject(int pid, int injectDelay = 0)
        {
            var process = Process.GetProcessById(pid);
            if (process.ProcessName != "ffxiv_dx11")
            {
                Log.Error("{pid} is not dx11", pid);
                MessageBox.Show("DriectX 11에서만 지원합니다.", windowsTitle);
                return false;
            }
            if (IsZombieProcess(pid)) {
                return false;
            }
            if (isInjected(process))
            {
                return false;
            }
            //var dalamudStartInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(GeneratingDalamudStartInfo(process)));
            //var startInfo = new ProcessStartInfo(injectorFile, $"{pid} {dalamudStartInfo}");
            //startInfo.WorkingDirectory = dalamudPath.FullName;
            //Process.Start(startInfo);
            Log.Information($"[Updater] dalamudUpdater.State:{dalamudUpdater.State}");
            if (dalamudUpdater.State == DalamudUpdater.DownloadState.NoIntegrity)
            {
                if (MessageBox.Show("현재 달라가브 버전이 게임과 호환되지 않을 수 있습니다. 그래도 적용하시겠습니까?", windowsTitle, MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return false;
                }
            }
            //return false;
            var dalamudStartInfo = GeneratingDalamudStartInfo(process, Directory.GetParent(dalamudUpdater.Runner.FullName).FullName, injectDelay);
            var environment = new Dictionary<string, string>();
            // No use cuz we're injecting instead of launching, the Dalamud.Boot.dll is reading environment variables from ffxiv_dx11.exe
            /*
            var prevDalamudRuntime = Environment.GetEnvironmentVariable("DALAMUD_RUNTIME");
            if (string.IsNullOrWhiteSpace(prevDalamudRuntime))
                environment.Add("DALAMUD_RUNTIME", runtimeDirectory.FullName);
            */
            WindowsDalamudRunner.Inject(dalamudUpdater.Runner, process.Id, environment, DalamudLoadMethod.DllInject, dalamudStartInfo, this.safeMode);
            return true;
        }

        private void ButtonInject_Click(object sender, EventArgs e)
        {
            if (this.comboBoxFFXIV.SelectedItem != null
                && this.comboBoxFFXIV.SelectedItem.ToString().Length > 0)
            {
                var pidStr = this.comboBoxFFXIV.SelectedItem.ToString();
                if (int.TryParse(pidStr, out var pid))
                {
                    if (Inject(pid))
                    {
                        Log.Information("[DINJECT] Inject finished.");
                    }
                }
                else
                {
                    MessageBox.Show("게임 프로세스를 찾지 못했습니다.", "달라가브 적용 실패",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("선택된 게임 프로세스가 없습니다.", "달라가브 적용 실패",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }

        }
        private void SetAutoRun(bool value)
        {
            string strFilePath = Application.ExecutablePath;
            try
            {
                SystemHelper.SetAutoRun($"\"{strFilePath}\"" + " -startup", "DalamudAutoInjector", value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkBoxAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            this.config.AutoStart = checkBoxAutoStart.Checked;
            SetAutoRun(this.config.AutoStart.Value);
            this.config.Save();
        }

        private void checkBoxAutoInject_CheckedChanged(object sender, EventArgs e)
        {
            this.config.AutoInject = checkBoxAutoInject.Checked;
            this.config.Save();
        }

        private void delayBox_ValueChanged(object sender, EventArgs e)
        {
            this.config.InjectDelaySeconds = (double)delayBox.Value;
            this.config.Save();
        }

        private void setProgressBar(int v)
        {
            if (this.toolStripProgressBar1.Owner.InvokeRequired)
            {
                Action<int> actionDelegate = (x) => { toolStripProgressBar1.Value = x; };
                this.toolStripProgressBar1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                this.toolStripProgressBar1.Value = v;
            }
        }
        private void setStatus(string v)
        {
            if (toolStripStatusLabel1.Owner.InvokeRequired)
            {
                Action<string> actionDelegate = (x) => { toolStripStatusLabel1.Text = x; };
                this.toolStripStatusLabel1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                this.toolStripStatusLabel1.Text = v;
            }
        }
        private void setVisible(bool v)
        {
            if (toolStripProgressBar1.Owner.InvokeRequired)
            {
                Action<bool> actionDelegate = (x) =>
                {
                    toolStripProgressBar1.Visible = x;
                    //toolStripStatusLabel1.Visible = v; 
                };
                this.toolStripStatusLabel1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                toolStripProgressBar1.Visible = v;
                //toolStripStatusLabel1.Visible = v;
            }
        }

        private bool safeMode = false;
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            this.safeMode = this.checkBoxSafeMode.Checked;
        }
    }
}
