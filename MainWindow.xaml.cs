using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace testICAM
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        static JObject json;
        static System.Windows.Threading.DispatcherTimer timerSend;    
        
        static bool isSerialPortMode = false, isHex = false, isRepeat = false, isSending = false;
        static int periodSendig = 5;
        
        [Flags]
        enum LogFlags { None = 0, noReturn = 1, toFile = 2, noTime = 4 };

        static SerialPort serialPort;

        static TcpClient tcpClient;
        static TcpListener tcpListener;
        private bool isConnected;
        private bool isImitation;
        private Dictionary<string, string> imitationDict;


        public MainWindow()
        {
            InitializeComponent();

            imitationDict = new Dictionary<string, string>();
            //Load settings            
            string jsonFile = AppDomain.CurrentDomain.BaseDirectory + "settings.json";
            try
            {
                json = (JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(jsonFile));
                if (json != null)
                {
                    fMain.Width = (int)json["Form"]["Width"];
                    fMain.Height = (int)json["Form"]["Height"];
                    fMain.Top = (int)json["Form"]["Top"];
                    fMain.Left = (int)json["Form"]["Left"];

                    cbSend.FillComboFromJson( json, "LastMessages", "Message", true);
                    cbIP.FillComboFromJson( (JObject)json["Network"], "LastAddresses", "Address");
                    cbPort.FillComboFromJson( (JObject)json["Network"], "LastPorts", "Port");

                    imitationDict.FillDictionaryFromJson(json, "Simulator", "Request", "Answer");
                }
            }
            catch (Exception e)
            {
                LogAdd($"Load config error [{jsonFile}] {Environment.NewLine} {e.Message}");
            }
            cbSerialPort.SelectedIndex = 0;

            serialPort = new SerialPort();

            timerSend = new System.Windows.Threading.DispatcherTimer();
            timerSend.Tick += DispatcherTimerSend_Tick;
            timerSend.Interval = new TimeSpan(0, 0, periodSendig);
            timerSend.Start();

            LogAdd("Start");
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            LogAdd("Disconnect");
            if (serialPort.IsOpen) serialPort.Close();
        }

        private void btnConnect_Click(object sender, RoutedEventArgs ea)
        {            
            LogAdd("Connect: ", LogFlags.noReturn);
            isConnected = false;

            if (isSerialPortMode) //подключение - последовательный порт
            {
                string name = cbSerialPort.Text;
                int speed = int.Parse(cbSpeed.Text);
                Parity parity = (Parity)Enum.Parse( typeof(Parity), cbParity.Text );

                LogAdd($"{name}:{speed}, {parity} - ", LogFlags.noReturn);

                try
                {
                    isConnected = serialPort.ConnectTo(name, speed, parity);
                }
                catch (Exception e)
                {
                    LogAdd(e.Message);
                }
                finally
                {
                    serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                    json["SerialPort"]["LastPort"]["Name"] = name;
                    json["SerialPort"]["LastPort"]["Speed"] = speed;
                    json["SerialPort"]["LastPort"]["Parity"] = parity.ToString();
                    LogAdd("Ok", LogFlags.noTime);
                }
            }
            else //подключение по TCP
            {
                IPAddress ipAddress = IPAddress.Any;
                string IP = cbIP.Text;
                int port, timeout;

                if (cbIP.CheckTextAndAdd() && cbPort.CheckTextAndAdd() && 
                    IPAddress.TryParse(cbIP.Text, out ipAddress) &&
                    int.TryParse(cbPort.Text, out port) && 
                    int.TryParse(cbTimeout.Text, out timeout))
                {
                    LogAdd($"{IP}:{port}, timeout={timeout} ms; ", LogFlags.noReturn);

                    try
                    {
                        if (tcpClient == null)
                            tcpClient = new TcpClient();

                        //tcpClient.Close();
                        tcpClient.SendTimeout = timeout;
                        tcpClient.ReceiveTimeout = timeout;

                        tcpClient.Connect(IP, port);
                    }
                    catch (Exception e)
                    {
                        LogAdd(e.Message);
                    }
                    finally
                    {
                        isConnected = true;
                        json["Network"]["TimeOut"] = timeout;
                        LogAdd("Ok");
                    }
                } else
                    LogAdd("Wrong parameters");
            }
        }

        static readonly object writeLock = new object();

        private void LogAdd(string message, LogFlags flags = LogFlags.None)
        {
            if (fMain.IsInitialized == false) return;

            //add time
            if ((chkLogTime.IsChecked.Value == true) && (flags.HasFlag(LogFlags.noTime) == false))
                message = DateTime.Now.ToString("yyyy-MM-dd HH\\:mm\\:ss") + "> " + message;

            //new line
            if (!flags.HasFlag(LogFlags.noReturn)) message += Environment.NewLine;
            
            //write to file
            if (flags.HasFlag(LogFlags.toFile))
                lock (writeLock)
                    try
                    {
                        File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + "log.txt", message);
                    }
                    catch (Exception e)
                    {
                        message += "[writeFileError]"+e.Message;
                    }
            
            txtLog.AppendText(message);
            txtLog.ScrollToEnd();
        }

        private void btnLogClear_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            //txtLog.Document.Blocks.Clear();
        }

        private void btnMake_Click(object sender, RoutedEventArgs e)
        {
            fMake make = new fMake();
            if (make.ShowDialog() == true)
                cbSend.Text = fMake.SendString;
        }

        void DataReceivedHandler(object sender, SerialDataReceivedEventArgs ea)
        {
            Thread.Sleep(100); //wait all symbols            
            SerialPort sp = (SerialPort)sender;
            string inData = sp.ReadExisting();

            // process data on the GUI thread
            Application.Current.Dispatcher.Invoke(new Action(() =>
                LogAdd("Recv: " + inData.ToHex(isHex))
            ));

            // emulator
            if (isImitation)
            {
                string msg = "Ans : ";
                try
                {
                    string dataString;
                    if (imitationDict.TryGetValue(inData, out dataString))
                    {
                        msg += dataString.ToHex(isHex);
                        serialPort.Write(Regex.Unescape(dataString));
                    }
                }
                catch (Exception e)
                {
                    msg += e.Message;
                }

                Application.Current.Dispatcher.Invoke(new Action(() =>
                    LogAdd(msg)
                ));
            }
        }

        private void rbSerial_Checked(object sender, RoutedEventArgs e)
        {
            isSerialPortMode = true;
        }

        private void chkLogHex_Checked(object sender, RoutedEventArgs e)
        {
            isHex = true;
        }

        private void rbSerial_Unchecked(object sender, RoutedEventArgs e)
        {
            isSerialPortMode = false;
        }

        private void chkRepeat_Checked(object sender, RoutedEventArgs e)
        {
            isRepeat = true;
        }

        private void btnSerialRefresh_Click(object sender, RoutedEventArgs e)
        {
            cbSerialPort.Items.Refresh();
        }

        private void cbSend_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) btnSend_Click(null, null);
        }

        private void fMain_Closing(object sender, System.ComponentModel.CancelEventArgs ea)
        {
            json["Form"]["Width"] = Convert.ToInt32(fMain.Width);
            json["Form"]["Height"] = Convert.ToInt32(fMain.Height);
            json["Form"]["Top"] = Convert.ToInt32(fMain.Top);
            json["Form"]["Left"] = Convert.ToInt32(fMain.Left);

            cbSend.FillJsonFromCombo((JObject)json, "LastMessages", "Message", true);
            cbIP.FillJsonFromCombo( (JObject)json["Network"], "LastAddresses", "Address");
            cbPort.FillJsonFromCombo( (JObject)json["Network"], "LastPorts", "Port");

            //save settings
            string jsonFile = AppDomain.CurrentDomain.BaseDirectory + "settings.json";
            try
            {
                File.WriteAllText(jsonFile, 
                    Newtonsoft.Json.JsonConvert.SerializeObject(json, 
                        Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception e)
            {
                MessageBox.Show("Save config error! "+e.Message);
            }
        }

        private void chkRepeat_Unchecked(object sender, RoutedEventArgs e)
        {
            isRepeat = false;
        }

        private void chkLogHex_Unchecked(object sender, RoutedEventArgs e)
        {
            isHex = false;
        }

        private void cbPeriod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (fMain.IsInitialized == false) return;
            string s = cbPeriod.SelectedValue.ToString();
            if (s.Length > 0)
            {
                periodSendig = int.Parse(s);
                timerSend.Interval = new TimeSpan(0, 0, periodSendig);
            }
        }

        private void rbImitation_Checked(object sender, RoutedEventArgs e)
        {
            isImitation = true;
        }

        private void rbImitaion_Unchecked(object sender, RoutedEventArgs e)
        {
            isImitation = false;
        }

        private void btnClearSend_Click(object sender, RoutedEventArgs e)
        {
            cbSend.Text = "";
        }

        private void btnSend_Click(object sender, RoutedEventArgs ea)
        {
            if (!isConnected)
                LogAdd("Not connected");
            else
            if ( cbSend.CheckTextAndAdd() )
            {
                string dataString = cbSend.Text;   

                isSending = isRepeat;
                
                LogAdd("Send: " + dataString.ToHex(isHex));

                try
                {
                    if (isSerialPortMode)
                        serialPort.Write(Regex.Unescape(dataString));
                    else
                        ;                    
                }
                catch (Exception e)
                {
                    LogAdd("Send: " + e.Message);
                }
            }
        }

        private void DispatcherTimerSend_Tick(object sender, EventArgs e)
        {
            if (isSending) btnSend_Click(null, null);
        }
    }
}
