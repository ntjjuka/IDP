using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace S1_BMS_IDPServiceManager
{

    public static class ServiceHelper
    {
        public static void Start(string serviceName)
        {
            using (ServiceController sc = new ServiceController(serviceName))
            {
                if (sc.Status == ServiceControllerStatus.Stopped)
                    sc.Start();
            }
        }

        public static void Stop(string serviceName)
        {
            using (ServiceController sc = new ServiceController(serviceName))
            {
                if (sc.Status == ServiceControllerStatus.Running)
                    sc.Stop();
            }
        }

        public static void Restart(string serviceName)
        {
            Stop(serviceName);
            Thread.Sleep(1000);
            Start(serviceName);
        }
    }
}
