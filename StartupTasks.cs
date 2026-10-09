#pragma warning disable 1691
#pragma warning disable 8600, 8602, 8603, 8604
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace NetworkMonitor
{
    // Shared by the application and the .NET Framework installer/uninstaller.
    internal static class StartupTaskService
    {
        internal static string TaskName { get { return "NetworkMonitor-" + WindowsIdentity.GetCurrent().User.Value; } }
        private static dynamic Service()
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) throw new InvalidOperationException("Windows 任务计划程序不可用。");
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            return service;
        }
        internal static bool IsEnabled() { return IsEnabled(TaskName); }
        internal static bool IsEnabled(string taskName)
        {
            dynamic folder = Service().GetFolder("\\");
            try { return (bool)folder.GetTask(taskName).Enabled; }
            catch (Exception ex) { if ((uint)ex.HResult == 0x80070002) return false; throw; }
        }
        internal static dynamic CreateDefinition(string executable)
        {
            return CreateDefinition(Service(), executable);
        }
        private static dynamic CreateDefinition(dynamic service, string executable)
        {
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable)) throw new FileNotFoundException("请先安装软件后再启用开机启动。", executable);
            string sid = WindowsIdentity.GetCurrent().User.Value;
            dynamic task = service.NewTask(0);
            task.RegistrationInfo.Description = "网络流量监控：登录后启动并驻留系统托盘。";
            task.Principal.UserId = sid;
            task.Principal.LogonType = 3;
            task.Principal.RunLevel = 1;
            task.Settings.Enabled = true;
            task.Settings.DisallowStartIfOnBatteries = false;
            task.Settings.StopIfGoingOnBatteries = false;
            task.Settings.ExecutionTimeLimit = "PT0S";
            task.Settings.MultipleInstances = 2;
            task.Settings.StartWhenAvailable = true;
            dynamic trigger = task.Triggers.Create(9);
            trigger.UserId = sid;
            trigger.Delay = "PT5S";
            dynamic action = task.Actions.Create(0);
            action.Path = executable;
            action.Arguments = "--tray";
            action.WorkingDirectory = Path.GetDirectoryName(executable);
            return task;
        }
        internal static void SetEnabled(bool enabled, string executable) { SetEnabled(enabled, executable, TaskName); }
        internal static void RegisterDefinition(string taskName, dynamic task)
        {
            RegisterDefinition(Service().GetFolder("\\"), taskName, task);
        }
        private static void RegisterDefinition(dynamic folder, string taskName, dynamic task)
        {
            folder.RegisterTaskDefinition(taskName, task, 6, WindowsIdentity.GetCurrent().User.Value, null, 3, null);
        }
        internal static void SetEnabled(bool enabled, string executable, string taskName)
        {
            dynamic service = Service();
            dynamic folder = service.GetFolder("\\");
            if (!enabled)
            {
                try { folder.DeleteTask(taskName, 0); }
                catch (Exception ex) { if ((uint)ex.HResult != 0x80070002) throw; }
                return;
            }
            dynamic task = CreateDefinition(service, executable);
            RegisterDefinition(folder, taskName, task);
        }
    }
}
