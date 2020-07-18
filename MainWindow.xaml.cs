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
using System.Threading.Tasks;
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
        static TcpListener tcpListener;
        private CancellationTokenSource cts;
        static IPEndPoint localIP;

        private bool isConnected, isImitation, isAnyIP = true;

        private Dictionary<string, string> imitationDict;
        private int timeout;

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

            string[] ports = SerialPort.GetPortNames();
            cbSerialPort.ItemsSource = ports;
            cbSerialPort.SelectedIndex = 0;

            serialPort = new SerialPort();

            timerSend = new System.Windows.Threading.DispatcherTimer();
            timerSend.Tick += DispatcherTimerSend_Tick;
            timerSend.Interval = new TimeSpan(0, 0, periodSendig);
            timerSend.Start();

            cts = new CancellationTokenSource();

            LogAdd("Start");
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            LogAdd("Disconnect");

            if (serialPort != null)
                if (serialPort.IsOpen) serialPort.Close();
            

            if (tcpListener != null)
            {
                cts.Cancel();
                tcpListener.Stop();
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs ea)
        {                        
            isConnected = false;

            if (isSerialPortMode) //подключение - последовательный порт
            {
                string name = cbSerialPort.Text;
                int speed = int.Parse(cbSpeed.Text);
                Parity parity = (Parity)Enum.Parse( typeof(Parity), cbParity.Text );

                LogAdd($"Connect: {name}:{speed}, {parity} - ", LogFlags.noReturn);

                try
                {
                    isConnected = serialPort.ConnectTo(name, speed, parity);
                    serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                }
                catch (Exception e)
                {
                    LogAdd(e.Message, LogFlags.noTime);
                }

                json["SerialPort"]["LastPort"]["Name"] = name;
                json["SerialPort"]["LastPort"]["Speed"] = speed;
                json["SerialPort"]["LastPort"]["Parity"] = parity.ToString();                               
            }
            else //подключение по TCP
            {
                IPAddress ipAddress = IPAddress.Any;
                string IP = cbIP.Text;
                int port;

                if (cbIP.CheckTextAndAdd() && 
                    cbPort.CheckTextAndAdd() && 
                    IPAddress.TryParse(cbIP.Text, out ipAddress) &&
                    int.TryParse(cbPort.Text, out port) && 
                    int.TryParse(cbTimeout.Text, out timeout))
                {
                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;

                        if (isImitation)
                        {
                            if (isAnyIP) ipAddress = IPAddress.Any;
                            
                            LogAdd($"Listen: {ipAddress}:{port}; ", LogFlags.noReturn);

                            if (tcpListener == null)
                                tcpListener = new TcpListener(ipAddress, port);

                            tcpListener.Start();
                            _ = HandleConnectionAsync(tcpListener, cts.Token);
                            isConnected = true;
                        }
                        else
                        {
                            LogAdd($"Connect: {ipAddress}:{port}, timeout={timeout} ms; ", LogFlags.noReturn);
                            localIP = new IPEndPoint(ipAddress, port);
                            using (var tcpClient = new TcpClient())
                            {
                                var result = tcpClient.BeginConnect(localIP.Address, localIP.Port, null, null);

                                if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeout)))
                                    throw new TimeoutException();
                                
                                tcpClient.EndConnect(result); // we have connected
                                isConnected = tcpClient.Connected;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        LogAdd(e.Message, LogFlags.noTime);
                    }
                } else
                    LogAdd("Wrong parameters", LogFlags.noTime);
            }

            Mouse.OverrideCursor = null;
            if (isConnected) LogAdd("Ok", LogFlags.noTime);
        }

        static readonly object writeLock = new object();

        private void LogAdd(string message, LogFlags flags = LogFlags.None)
        {
            if (fMain.IsInitialized == false) return;

            //add time
            if ((chkLogTime.IsChecked.Value == true) && (flags.HasFlag(LogFlags.noTime) == false))
                message = DateTime.Now.ToString("yyyy-MM-dd HH\\:mm\\:ss.fff") + "> " + message;

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
                    if (imitationDict.TryGetValue(inData, out string dataString))
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
            string[] ports = SerialPort.GetPortNames();
            cbSerialPort.ItemsSource = ports;
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

        private void chkAnyIP_Unchecked(object sender, RoutedEventArgs e)
        {
            isAnyIP = true;
        }

        private void rbImitaion_Unchecked(object sender, RoutedEventArgs e)
        {
            isImitation = false;
        }

        private void chkAnyIP_Checked(object sender, RoutedEventArgs e)
        {
            isAnyIP = false;
        }

        private void btnClearSend_Click(object sender, RoutedEventArgs e)
        {
            cbSend.Text = "";
        }

        private void btnSend_Click(object sender, RoutedEventArgs ea)
        {
            if ( cbSend.CheckTextAndAdd() )
            {
                string dataString = cbSend.Text;   

                isSending = isRepeat;                
                
                dataString = Regex.Unescape(dataString);
                LogAdd("Send: " + dataString.ToHex(isHex));

                try
                {
                    if (isSerialPortMode)
                    {
                        if (!serialPort.IsOpen)
                            btnConnect_Click(sender, null);
                        serialPort.Write(dataString);
                    }
                    else
                    {
                        using (var tcpClient = new TcpClient())
                        {
                            tcpClient.SendTimeout = timeout;
                            tcpClient.ReceiveTimeout = timeout;

                            var result = tcpClient.BeginConnect(localIP.Address, localIP.Port, null, null);

                            if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeout)))
                                throw new TimeoutException();

                            tcpClient.EndConnect(result); // we have connected

                            using (var networkStream = tcpClient.GetStream())
                            using (BinaryWriter writer = new BinaryWriter(networkStream))
                            using (BinaryReader reader = new BinaryReader(networkStream))
                            {
                                writer.Write(dataString);
                                writer.Flush();
                                Byte[] bytes = new byte[4096];

                                var size = reader.Read( bytes, 0, 4096);
                                if (size > 0)
                                {                                    
                                    string inData = Encoding.ASCII.GetString(bytes, 0, size); //the message incoming
                                    LogAdd("Recv: " + inData.ToHex(isHex));
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    LogAdd("Send: |" + e.GetType().ToString() +"| "+ e.Message); ;
                }
            }
        }

        private void DispatcherTimerSend_Tick(object sender, EventArgs e)
        {
            if (isSending) btnSend_Click(null, null);
        }

        async Task HandleConnectionAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                await EchoAsync(client, ct);
            }
        }

        async Task EchoAsync(TcpClient client, CancellationToken ct)
        {
            var buf = new byte[4096];
            var stream = client.GetStream();
            string inData;
            while (!ct.IsCancellationRequested)
            {
                var amountRead = await stream.ReadAsync(buf, 0, buf.Length, ct);
                if (amountRead > 0)
                {
                    inData = Encoding.Default.GetString(buf, 0, amountRead);
                    LogAdd("Recv: " + inData.ToHex(isHex));

                    string msg = "";
                    try
                    {
                        if (imitationDict.TryGetValue(inData, out string dataString))
                        {
                            msg = "Ans : " + dataString.ToHex(isHex);

                            buf = Encoding.ASCII.GetBytes(dataString);
                        
                            await stream.WriteAsync(buf, 0, dataString.Length, ct);
                        } else
                        {
                            msg = "unknown command";
                        }
                    }
                    catch (Exception e)
                    {
                        msg += e.Message;
                    }
                    finally
                    {
                        LogAdd(msg);
                    }
                }

                if (amountRead == 0) break; //end of stream.
            }
        }
    }
}
