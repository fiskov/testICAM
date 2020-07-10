using Newtonsoft.Json.Linq;
using System;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace testICAM
{
    public static class Helper
    {
        public static bool CheckTextAndAdd(this ComboBox cb, int size = 10)
        {
            string dataString = cb.Text;
            bool res = false;

            if (dataString.Length > 0)
            {
                //add sending-string to combobox items
                if (cb.Items.Contains(dataString) == false)
                    cb.Items.Insert(0, dataString);
                else
                {
                    cb.Items.Remove(dataString);
                    cb.Items.Insert(0, dataString);
                    cb.SelectedIndex = 0;
                }
                while (cb.Items.Count > size) cb.Items.RemoveAt(cb.Items.Count - 1);
                res = true;
            }

            return res;
        }

        public static void FillJsonFromCombo(this ComboBox cb,
                JObject j, string sArray, string sField, bool escaped = false)
        {
            j[sArray].Value<JArray>().Clear();

            foreach (string s in cb.Items)
                j[sArray].Value<JArray>().Add(
                    new JObject(
                        new JProperty(sField,
                            (escaped ? Regex.Unescape(s) : s)
                        )
                    )
                );
        }

        public static void FillComboFromJson(this ComboBox cb, 
            JObject j, string sArray, string sField, bool escaped = false)
        {
            string s;
            cb.Items.Clear();

            foreach (var currentItem in j[sArray])
            {
                s = currentItem.Value<string>(sField);
                if (escaped) s = Regex.Escape(s);
                cb.Items.Add(s);
            }

            //if (cb.Items.Count > 0) 
            cb.SelectedIndex = 0;
        }

        public static string ToHex(this string message, bool toHex)
        {            
            if (toHex)
            {
                var res = new StringBuilder();
                byte[] bytes = Encoding.ASCII.GetBytes(message);

                foreach (byte b in bytes)
                    res.Append(string.Format("{0:X2} ", b));

                return res.ToString();
            } else
            {
                return message;
            }            
        }

        public static bool ConnectTo(this SerialPort serialPort, string name, int speed, Parity parity)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Не указан последовательный порт");

            serialPort.Close();
            serialPort.PortName = name;
            serialPort.BaudRate = speed;
            serialPort.Parity = parity;
            serialPort.WriteTimeout = 500;

            try
            {
                serialPort.Open();
            }
            catch (Exception e)
            {
                throw new Exception("Ошибка открытия последовательного порта! " + e.Message);
            }
            return serialPort.IsOpen;
        }
    }
}
