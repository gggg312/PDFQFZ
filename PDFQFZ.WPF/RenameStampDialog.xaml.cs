using System.Windows;

namespace PDFQFZ.WPF
{
    /// <summary>印章重命名/重名导入弹窗：输入新名称（1-30 字符），确定返回输入内容。</summary>
    public partial class RenameStampDialog : Window
    {
        /// <summary>确定后返回输入的名称（已 Trim）。</summary>
        public string ResultName => txtName.Text.Trim();

        public RenameStampDialog(string message, string initialName)
        {
            InitializeComponent();
            txtMsg.Text = message;
            txtName.Text = initialName ?? string.Empty;
            Loaded += (s, e) =>
            {
                txtName.Focus();
                txtName.SelectAll();
            };
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
