using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace testICAM
{
    /// <summary>
    /// Логика взаимодействия для fMake.xaml
    /// </summary>
    public partial class fMake : Window
    {
        public fMake()
        {
            InitializeComponent();
        }

        private void btnAddSingle_Click(object sender, RoutedEventArgs e)
        {
            string s = "AHAH";
            s += (cbType.SelectedIndex + 1).ToString("X");
            Regex regex = new Regex(@"[^\d]");
            s += regex.Replace(cbLine.Text, "");
            s += $"/\x0D";

            s = Regex.Escape(s);
            //MainWindow.ViewModel.SendString = s;

            Close();
        }

        private void btnAddAll_Click(object sender, RoutedEventArgs e)
        {
            string s = "";
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 5; i++) s += "AHAH" + j + "2" + i + "/\r";
                for (int i = 0; i < 5; i++) s += "AHAH" + j + "4" + i + "/\r";
            }

            s = Regex.Escape(s);

            //MainWindow.ViewModel.SendString = s;

            Close();
        }
    }
}
