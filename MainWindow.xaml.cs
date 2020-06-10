using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace testICAM
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        static JObject json;

        static bool isSending = false;
        static int periodSendig = 5;
        static System.Windows.Threading.DispatcherTimer timerSend;

        static bool isSerialPortMode = false, isHex = false, isRepeat = false;
        enum ConnectionModeEnum { None = 0, Serial = 1, Network = 2 } ;
        [Flags]
        enum LogFlags { None = 0, noReturn = 1, toFile = 2, toHex = 4, noTime = 8 };
        static ConnectionModeEnum connectionMode;
        static SerialPort _serialPort;

        public class cbStrings
        {
            public string SendString { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();

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
                }
            }
            catch (Exception e)
            {
                LogAdd($"Ошибка загрузки настроек \"{jsonFile}\"\n{e.Message}");
            }
            if (cbSerialPort.Items.Count > 0) cbSerialPort.SelectedIndex = 0;
            _serialPort = new SerialPort();

            timerSend = new System.Windows.Threading.DispatcherTimer();
            timerSend.Tick += dispatcherTimerSend_Tick;
            timerSend.Interval = new TimeSpan(0, 0, periodSendig);
            timerSend.Start();

            LogAdd("Старт");
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            LogAdd("Отключение");
            if (_serialPort.IsOpen) _serialPort.Close();
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            string connectMsg = "Подключение: ";
            Parity[] a = { Parity.None, Parity.Even, Parity.Odd };

            if (isSerialPortMode)
            {
                if (cbSerialPort.Text.Length > 0)
                {
                    connectMsg += $"{cbSerialPort.Text}:{cbSpeed.Text}, {cbParity.Text}; ";
                    

                    _serialPort.Close();
                    _serialPort.PortName = cbSerialPort.Text;
                    _serialPort.BaudRate = Int32.Parse(cbSpeed.Text);
                    _serialPort.Parity = a[cbParity.SelectedIndex];
                    _serialPort.WriteTimeout = 500;
                    _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                    try
                    {
                        _serialPort.Open();
                    }
                    catch (Exception Exception1)
                    {
                        connectMsg += ($"Ошибка открытия последовательного порта! " + Exception1.Message);
                    }
                    if (_serialPort.IsOpen) connectionMode = ConnectionModeEnum.Serial;
                }
                else
                    connectMsg += "Не указан последовательный порт";
            } else
            {                
                int timeout = 3000;
                try
                {
                    timeout = Int32.Parse(tbTimeout.Text);
                }
                catch (Exception)
                {
                    tbTimeout.Text = "3000";          
                }

                connectMsg += $"{cbIP.Text}:{cbPort.Text}, таймаут={timeout}";
            }
            LogAdd(connectMsg); 
            
        }

        static readonly object writeLock = new object();

        private void LogAdd(string message, LogFlags flags = LogFlags.None)
        {
            if (fMain.IsInitialized == false) return;
            if (flags.HasFlag(LogFlags.toHex))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(message);
                message = "";
                foreach (byte b in bytes)
                    message += string.Format("{0:X2} ", b);
            }
            if ((chkLogTime.IsChecked.Value == true) && (flags.HasFlag(LogFlags.noTime) == false))
            {
                message = DateTime.Now.ToString("yyyy-MM-dd HH\\:mm\\:ss") + "> " + message;
            }
            if (!flags.HasFlag(LogFlags.noReturn)) message += Environment.NewLine;

            if (flags.HasFlag(LogFlags.toFile))
                lock (writeLock)
                    try
                    {
                        File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + "log.txt", message);
                    }
                    catch
                    {
                        message += "[writeFileError]";
                    }
            
            /*
            Color color = Color.FromRgb();
            Paragraph paragraph = new Paragraph();
            Run run = new Run(message);
            paragraph.Inlines.Add(run);
            txtLog.Document.Blocks.Add(paragraph);
            */
            txtLog.AppendText(message);
            txtLog.ScrollToEnd();
        }

        private void btnLogClear_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Document.Blocks.Clear();
        }

        private void btnMake_Click(object sender, RoutedEventArgs e)
        {

            fMake make = new fMake();
            make.Owner = this;
            make.Show();
        }

        private void tbTimeout_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            Thread.Sleep(100); //wait all symbols            
            SerialPort sp = (SerialPort)sender;
            string inData = "";   
            
            Application.Current.Dispatcher.Invoke(new Action(() => {
                inData = sp.ReadExisting();
                LogAdd("Recv: ", LogFlags.noReturn);
                LogAdd(inData, (isHex ? LogFlags.toHex : LogFlags.None));
            }));
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

        private void fMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            json["Form"]["Width"] = Convert.ToInt32(fMain.Width);
            json["Form"]["Height"] = Convert.ToInt32(fMain.Height);
            json["Form"]["Top"] = Convert.ToInt32(fMain.Top);
            json["Form"]["Left"] = Convert.ToInt32(fMain.Left);

            //save settings
            string jsonFile = AppDomain.CurrentDomain.BaseDirectory + "settings.json";
            try
            {
                File.WriteAllText(jsonFile, Newtonsoft.Json.JsonConvert.SerializeObject(json, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception)
            {
                MessageBox.Show("Ошибка сохранения настроек");
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
                periodSendig = Int32.Parse(s);
                timerSend.Interval = new TimeSpan(0, 0, periodSendig);
            }
        }

        private void btnClearSend_Click(object sender, RoutedEventArgs e)
        {
            cbSend.Text = "";
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            string dataString = cbSend.Text;
            if (dataString.Length < 1)
            {
                LogAdd("Нечего отправлять");
                return;
            }
            if (cbSend.Items.Contains(dataString) == false)
                cbSend.Items.Insert(0, dataString); else
            {
                cbSend.Items.Remove(dataString);
                cbSend.Items.Insert(0, dataString);
                cbSend.SelectedIndex = 0;
            }
            while (cbSend.Items.Count > 10) cbSend.Items.RemoveAt(cbSend.Items.Count-1);

            isSending = isRepeat;

            if (connectionMode == ConnectionModeEnum.Serial)
            {
                try
                {
                    _serialPort.Write(Regex.Unescape(dataString));
                    LogAdd("Send: ", LogFlags.noReturn);
                    LogAdd(dataString, (isHex ? LogFlags.toHex : LogFlags.None) | LogFlags.noTime);
                }
                catch (Exception exception)
                {
                    LogAdd("Send: " + exception.Message);
                }
            }
        }

        private void dispatcherTimerSend_Tick(object sender, EventArgs e)
        {
            if (isSending) btnSend_Click(null, null);
        }
    }
}
