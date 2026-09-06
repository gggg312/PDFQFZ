using System.Windows;
using System.Windows.Controls;
using PDFQFZ.Library;

namespace PDFQFZ.WPF
{
    /// <summary>指定范围页盖章：输入起始页/结束页。</summary>
    public partial class PageRangeDialog : Window
    {
        private readonly int pageCount;

        public PageRangeDialog(int pageCount)
        {
            InitializeComponent();
            this.pageCount = pageCount;
            txtPageCount.Text = "页码范围：1 - " + pageCount;
            txtStart.Text = "1";
            txtEnd.Text = pageCount.ToString();
            txtStart.Focus();
        }

        internal PDFQFZ.Library.PageRange Result { get; private set; }

        private int ClampPage(int value) => value < 1 ? 1 : (value > pageCount ? pageCount : value);

        private int ParsePage(TextBox tb) => int.TryParse(tb.Text, out int v) ? v : 1;

        private void OnStartDown(object sender, RoutedEventArgs e) => txtStart.Text = ClampPage(ParsePage(txtStart) - 1).ToString();
        private void OnStartUp(object sender, RoutedEventArgs e) => txtStart.Text = ClampPage(ParsePage(txtStart) + 1).ToString();
        private void OnEndDown(object sender, RoutedEventArgs e) => txtEnd.Text = ClampPage(ParsePage(txtEnd) - 1).ToString();
        private void OnEndUp(object sender, RoutedEventArgs e) => txtEnd.Text = ClampPage(ParsePage(txtEnd) + 1).ToString();

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (PDFQFZ.Library.PageRange.TryCreate(txtStart.Text, txtEnd.Text, pageCount, out PDFQFZ.Library.PageRange range, out string error))
            {
                Result = range;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(error, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
