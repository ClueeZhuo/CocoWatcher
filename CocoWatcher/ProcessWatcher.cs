using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;


namespace CocoWatcher
{
    public partial class ProcessWatcher : ServiceBase
    {
        //字段
        private string[] _processAddress;
        private object _lockerForLog = new object();
        private string _logPath = string.Empty;


        /// <summary>
        /// 构造函数
        /// </summary>
        public ProcessWatcher()
        {
            InitializeComponent();

            try
            {
                //读取监控进程全路径
                string strProcessAddress = ConfigurationManager.AppSettings["ProcessAddress"].ToString();
                if (strProcessAddress.Trim() != "")
                {
                    this._processAddress = strProcessAddress.Split(',');
                }
                else
                {
                    throw new Exception("读取配置档ProcessAddress失败，ProcessAddress为空！");
                }

                //创建日志目录
                this._logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CocoWatcherLog");
                if (!Directory.Exists(_logPath))
                {
                    Directory.CreateDirectory(_logPath);
                }
            }
            catch (Exception ex)
            {
                this.SaveLog("Watcher()初始化出错！错误描述为：" + ex.Message.ToString());
            }
        }


        /// <summary>
        /// 启动服务
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            try
            {
                this.StartWatch();
            }
            catch (Exception ex)
            {
                this.SaveLog("OnStart() 出错，错误描述：" + ex.Message.ToString());
            }
        }


        /// <summary>
        /// 停止服务
        /// </summary>
        protected override void OnStop()
        {
            try
            {

            }
            catch (Exception ex)
            {
                this.SaveLog("OnStop 出错，错误描述：" + ex.Message.ToString());
            }
        }


        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartWatch()
        {
            if (this._processAddress != null)
            {
                if (this._processAddress.Length > 0)
                {
                    foreach (string str in _processAddress)
                    {
                        if (str.Trim() != "")
                        {
                            if (File.Exists(str.Trim()))
                            {
                                this.ScanProcessList(str.Trim());
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// 扫描进程列表，判断进程对应的全路径是否与指定路径一致
        /// 如果一致，说明进程已启动
        /// 如果不一致，说明进程尚未启动
        /// </summary>
        /// <param name="strAddress"></param>
        private void ScanProcessList(string address)
        {
            Process[] arrayProcess = Process.GetProcesses();
            foreach (Process process in arrayProcess)
            {
                //System、Idle进程会拒绝访问其全路径
                if (process.ProcessName != "System" && process.ProcessName != "Idle")
                {
                    try
                    {
                        if (this.FormatPath(address) == this.FormatPath(process.MainModule.FileName.ToString()))
                        {
                            //进程已启动
                            this.WatchProcess(process, address);
                            return;
                        }
                    }
                    catch
                    {
                        //拒绝访问进程的全路径
                        this.SaveLog("进程(" + process.Id.ToString() + ")(" + process.ProcessName.ToString() + ")拒绝访问全路径！");
                    }
                }
            }

            //进程尚未启动
            Process startProcess = new Process();
            startProcess.StartInfo.FileName = address;
            startProcess.Start();
            this.WatchProcess(startProcess, address);
        }


        /// <summary>
        /// 监听进程
        /// </summary>
        /// <param name="p"></param>
        /// <param name="address"></param>
        private void WatchProcess(Process process, string address)
        {
            ProcessRestart objProcessRestart = new ProcessRestart(process, address);
            Thread thread = new Thread(new ThreadStart(objProcessRestart.HangProcess));
            thread.Start();
        }


        /// <summary>
        /// 格式化路径
        /// 去除前后空格
        /// 去除最后的"\"
        /// 字母全部转化为小写
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private string FormatPath(string path)
        {
            return path.ToLower().Trim().TrimEnd('\\');
        }


        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="content"></param>
        public void SaveLog(string content)
        {
            try
            {
                lock (_lockerForLog)
                {
                    FileStream fs;
                    fs = new FileStream(Path.Combine(this._logPath, DateTime.Now.ToString("yyyyMMdd") + ".log"), FileMode.OpenOrCreate);
                    StreamWriter streamWriter = new StreamWriter(fs);
                    streamWriter.BaseStream.Seek(0, SeekOrigin.End);
                    streamWriter.WriteLine("[" + DateTime.Now.ToString() + "]：" + content);
                    streamWriter.Flush();
                    streamWriter.Close();
                    fs.Close();
                }
            }
            catch
            {
            }
        }


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetCursorPos(ref Point pt);

        //获取窗口标题
        [DllImport("user32", SetLastError = true)]
        private static extern int GetWindowText(
            IntPtr hWnd,//窗口句柄
            StringBuilder lpString,//标题
            int nMaxCount //最大值
            );

        //获取类的名字
        [DllImport("user32.dll")]
        private static extern int GetClassName(
            IntPtr hWnd,//句柄
            StringBuilder lpString, //类名
            int nMaxCount //最大值
            );

        //根据坐标获取窗口句柄
        [DllImport("user32")]
        private static extern IntPtr WindowFromPoint(
            Point Point  //坐标
        );
    }


    public class ProcessRestart
    {
        //字段
        private Process _process;
        private string _address;


        /// <summary>
        /// 构造函数
        /// </summary>
        public ProcessRestart()
        { }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="process"></param>
        /// <param name="address"></param>
        public ProcessRestart(Process process, string address)
        {
            this._process = process;
            this._address = address;
        }


        /// <summary>
        /// 重启进程
        /// </summary>
        public void RestartProcess()
        {
            try
            {
                while (true)
                {
                    Point point = new Point(0, 0);
                    GetCursorPos(ref point);

                    //1.根据位置获取窗口句柄：    
                    IntPtr formHandle = WindowFromPoint(point);//得到窗口句柄  p为当前位置（Point）            
                    StringBuilder title = new StringBuilder(256);
                    //2.根据句柄获取窗口标题：    
                    GetWindowText(formHandle, title, title.Capacity);//得到窗口的标题

                    Console.WriteLine($"TITLE-{title}");

                    this._process.WaitForExit();
                    this._process.Close();    //释放已退出进程的句柄
                    this._process.StartInfo.FileName = this._address;
                    this._process.Start();

                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                ProcessWatcher objProcessWatcher = new ProcessWatcher();
                objProcessWatcher.SaveLog("RestartProcess() 出错，监控程序已取消对进程("
                    + this._process.Id.ToString() + ")(" + this._process.ProcessName.ToString()
                    + ")的监控，错误描述为：" + ex.Message.ToString());
            }
        }

        /// <summary>
        /// 挂起经常监控
        /// </summary>
        public void HangProcess()
        {
            while (true)
            {
                try
                {
                    //1.获取鼠标位置
                    //Point point = new Point(0, 0);
                    //GetCursorPos(ref point);

                    //2.获取窗口句柄
                    //IntPtr formHandle = WindowFromPoint(point);//得到窗口句柄  p为当前位置（Point）

                    //3.根据句柄获取窗口标题：    
                    //StringBuilder title = new StringBuilder(256);
                    //GetWindowText(formHandle, title, title.Capacity);//得到窗口的标题

                    //Spy++获取Ghost窗体句柄
                    IntPtr formHandle = FindWindow("Ghost", null);
                    if (formHandle != IntPtr.Zero)
                    {
                        Console.WriteLine("GHOST~~~");

                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(formHandle, title, title.Capacity);//得到窗口的标题

                        Console.WriteLine($"TITLE-{title}");

                        if (title.ToString().Contains("未响应"))
                        {
                            this._process.Kill();
                            this._process.Close();    //释放已退出进程的句柄
                            this._process.StartInfo.FileName = this._address;
                            this._process.Start();
                        }
                    }

                    Console.WriteLine("NEXT~~~");
                }
                catch (Exception exp)
                {
                    ProcessWatcher objProcessWatcher = new ProcessWatcher();
                    objProcessWatcher.SaveLog("RestartProcess() 出错，监控程序已取消对进程("
                        + this._process.Id.ToString() + ")(" + this._process.ProcessName.ToString()
                        + ")的监控，错误描述为：" + exp.Message.ToString());
                }
                finally
                {
                    Thread.Sleep(1000 * 3);
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetCursorPos(ref Point pt);

        //获取窗口标题
        [DllImport("user32", SetLastError = true)]
        private static extern int GetWindowText(
            IntPtr hWnd,//窗口句柄
            StringBuilder lpString,//标题
            int nMaxCount //最大值
            );

        //获取类的名字
        [DllImport("user32.dll")]
        private static extern int GetClassName(
            IntPtr hWnd,//句柄
            StringBuilder lpString, //类名
            int nMaxCount //最大值
            );

        //根据坐标获取窗口句柄
        [DllImport("user32")]
        private static extern IntPtr WindowFromPoint(
            Point Point  //坐标
        );

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lpClassName"></param>
        /// <param name="lpWindowName"></param>
        /// <returns></returns>
        [DllImport("user32.dll", EntryPoint = "FindWindow")]
        private extern static IntPtr FindWindow(string lpClassName, string lpWindowName);
    }
}
