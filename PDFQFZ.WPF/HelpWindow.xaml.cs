using System.Windows;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 使用说明窗口。
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
