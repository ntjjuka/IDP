using System;
using System.Threading;

namespace IDPSsoAgent.Startup
{
    public static class SingleInstance
    {
        private static Mutex _mutex;

        // 전역 Mutex. 로그인 세션이 달라도 하나만.
        private const string MutexName = @"Global\IDPSsoAgent_Singleton";

        public static bool Acquire()
        {
            try
            {
                bool createdNew;
                _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);
                return createdNew; // false면 이미 실행 중
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Release()
        {
            try
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch { /* ignore */ }
        }
    }
}
