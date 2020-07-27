using System.Text.RegularExpressions;
using System.Windows;

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

            SendString = Regex.Escape(s);

            this.DialogResult = true;
        }

        private void btnAddAll_Click(object sender, RoutedEventArgs e)
        {
            string s = "";
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 5; i++) s += "AHAH" + j + "2" + i + "/\r";
                for (int i = 0; i < 5; i++) s += "AHAH" + j + "4" + i + "/\r";
            }

            SendString = Regex.Escape(s);

            this.DialogResult = true;
        }

        public static string SendString
        {
            get; set;
        }
    }

}
