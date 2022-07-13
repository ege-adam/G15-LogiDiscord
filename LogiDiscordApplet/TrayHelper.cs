using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LogiDiscordApplet
{
    public class TrayHelper
    {
        private NotifyIcon notifyIcon;

        public TrayHelper()
        {
            Thread notifyThread = new Thread(
                delegate ()
                {
                    notifyIcon = new NotifyIcon
                    {
                        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                        Visible = true,
                        Text = Application.ProductName
                    };

                    ContextMenu contextMenu = new ContextMenu();
                    MenuItem menuExit = new MenuItem("Exit");
                    MenuItem menuRunOnBoot = new MenuItem("Run on boot");

                    RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);

                    if (rk.GetValue(Application.ProductName) != null) menuRunOnBoot.Checked = true;

                    contextMenu.MenuItems.Add(0, menuRunOnBoot);
                    contextMenu.MenuItems.Add(1, menuExit);

                    notifyIcon.ContextMenu = contextMenu;

                    menuExit.Click += new EventHandler(OnMenuExitClick);
                    menuRunOnBoot.Click += new EventHandler(OnMenuRunOnBootClick);

                    Application.Run();
                }
            );

            notifyThread.Start();
        }

        private void OnMenuExitClick(object sender, EventArgs e)
        {
            ExitApp();
        }

        public void ExitApp()
        {
            notifyIcon.Dispose();
            Application.Exit();
            Environment.Exit(Environment.ExitCode);
        }

        private void OnMenuRunOnBootClick(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;

            RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

            if (!item.Checked)
            {
                rk.SetValue(Application.ProductName, Application.ExecutablePath);
                item.Checked = true;
            }
            else
            {
                rk.DeleteValue(Application.ProductName, false);
                item.Checked = false;
            }
        }
    }
}
