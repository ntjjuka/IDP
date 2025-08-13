using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace S1_BMS_IDPServiceManager
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "installMe":
                        SelfInstaller.InstallMe();
                        break;
                    case "uninstallMe":
                        SelfInstaller.UninstallMe();
                        break;
                    case "start":
                        ServiceHelper.Start("KeycloakService");
                        break;
                    case "stop":
                        ServiceHelper.Stop("KeycloakService");
                        break;
                    case "restart":
                        ServiceHelper.Restart("KeycloakService");
                        break;
                }
                return;
            }

            ServiceBase.Run(new MyService());
        }
    }
}
