using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DisplayDeckApp
{
    internal sealed partial class MainWindow : Window
    {
        private void ActivateProfile(ProfileRecord profile, bool safeTest, bool silent)
        {
            if (busy) return;
            bool safe = !silent && (safeTest || settings.ConfirmEverySwitch);
            string previousId = currentProfileId;
            string revertPath = safe ? Path.Combine(AppPaths.TempDir, "revert-" + Guid.NewGuid().ToString("N") + ".nvprofile.json") : null;
            SetBusy(true, "Switching to “" + profile.Name + "”…");

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                if (safe && !NvDisplayEngine.Capture(revertPath, out error))
                {
                    Dispatcher.BeginInvoke((Action)delegate { SetBusy(false, "Could not create safety snapshot"); ShowError("Safe test unavailable", error); });
                    return;
                }

                bool ok = NvDisplayEngine.Apply(ProfileStore.GetProfilePath(profile), "apply " + profile.Name, out error);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (!ok)
                    {
                        SetBusy(false, "Switch failed");
                        ShowError("Could not activate profile", error);
                        TryDelete(revertPath);
                        return;
                    }

                    if (!safe)
                    {
                        currentProfileId = profile.Id;
                        WriteCurrentProfile(profile.Id);
                        SetBusy(false, "Active: " + profile.Name);
                        RefreshProfiles();
                        return;
                    }

                    SetBusy(false, "Testing: " + profile.Name);
                    SafeConfirmDialog confirm = new SafeConfirmDialog(profile.Name, settings.AutoRevertSeconds);
                    confirm.Owner = null;
                    bool? keep = confirm.ShowDialog();
                    if (keep == true)
                    {
                        currentProfileId = profile.Id;
                        WriteCurrentProfile(profile.Id);
                        TryDelete(revertPath);
                        SetStatus("Active: " + profile.Name, true);
                        RefreshProfiles();
                    }
                    else
                    {
                        SetBusy(true, "Reverting display configuration…");
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            string revertError;
                            bool reverted = NvDisplayEngine.Apply(revertPath, "auto-revert", out revertError);
                            TryDelete(revertPath);
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                currentProfileId = previousId;
                                WriteCurrentProfile(previousId);
                                SetBusy(false, reverted ? "Reverted safely" : "Revert failed");
                                RefreshProfiles();
                                if (!reverted) ShowError("Automatic revert failed", revertError);
                            });
                        });
                    }
                });
            });
        }
    }
}
