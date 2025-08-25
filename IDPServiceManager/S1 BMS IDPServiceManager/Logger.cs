using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S1_BMS_IDPServiceManager
{
    public class LogControl
    {
        /// <summary>
        /// 디버그 모드로 실행 시 콘솔창으로 로그 출력하는 함수
        /// </summary>
        /// <param name="message">로그 내용</param>
        public void ConsoleLogWrite(string message)
        {
            try
            {
                Console.WriteLine(message);
            }
            catch (Exception a)
            {
                Console.WriteLine(a.Message);
            }
        }

        /// <summary>
        /// 파일로 로그 출력하는 함수
        /// </summary>
        /// <param name="DriverName">드라이버명</param>
        /// <param name="strName">함수명 or 로그 타이틀</param>
        /// <param name="strData">로그 내용</param>
        public void LogWrite(string DriverName, string strName, string strData)
        {
            try
            {
                DirectoryInfo topDir = System.IO.Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);
                string PreDirPath = topDir.Parent.FullName;
                string FolderPath = string.Format("{0}\\{1}\\{2}", "Log", DateTime.Now.ToString("yyyyMM"), DateTime.Now.ToString("dd"));
                string DirPath = System.IO.Path.Combine(PreDirPath, FolderPath);
                string FilePath = DirPath + string.Format("\\{0}.log", DriverName);
                string temp;

                DirectoryInfo di = new DirectoryInfo(DirPath);
                FileInfo fi = new FileInfo(FilePath);

                if (!di.Exists) Directory.CreateDirectory(DirPath);

                if (!fi.Exists)
                {
                    using (StreamWriter sw = new StreamWriter(FilePath))
                    {
                        temp = string.Format("[{0}] {1} : {2}", DateTime.Now, strName, strData);
                        sw.WriteLine(temp);
                        sw.Close();
                    }
                }
                else
                {
                    using (StreamWriter sw = File.AppendText(FilePath))
                    {
                        temp = string.Format("[{0}] {1} : {2}", DateTime.Now, strName, strData);
                        sw.WriteLine(temp);
                        sw.Close();
                    }
                }
            }
            catch (Exception a)
            {
                Console.WriteLine(a.Message);
            }
        }
    }
}