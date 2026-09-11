using System.Windows;
using System.Windows.Controls;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 通用确认弹窗（V83 新增，替代 MessageBox）：CenterOwner 打开，覆盖在父窗口上方居中。
    /// WPF 原生 MessageBox 不保证居中于 owner（弹窗内再弹时会跑到屏幕中间），
    /// 凡需正确归属父窗口的确认场景统一使用本弹窗；按钮顺序与 Windows 惯例一致（是/否/取消，取消最右）。
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        /// <summary>用户点击的按钮结果（默认 Cancel）。</summary>
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public ConfirmDialog(string title, string message, MessageBoxButton buttons)
        {
            InitializeComponent();
            Title = title;
            txtMsg.Text = message;

            if (buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel)
            {
                AddButton("确定", MessageBoxResult.OK, true, buttons == MessageBoxButton.OK);
                if (buttons == MessageBoxButton.OKCancel)
                {
                    AddButton("取消", MessageBoxResult.Cancel, false, true);
                }
            }
            else
            {
                AddButton("是", MessageBoxResult.Yes, true, false);
                AddButton("否", MessageBoxResult.No, false, false);
                if (buttons == MessageBoxButton.YesNoCancel)
                {
                    AddButton("取消", MessageBoxResult.Cancel, false, true);
                }
            }
        }

        private void AddButton(string text, MessageBoxResult result, bool isDefault, bool isCancel)
        {
            var btn = new Button
            {
                Content = text,
                Width = 72,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Tag = result,
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            btn.Click += OnBtnClick;
            btnPanel.Children.Add(btn);
        }

        private void OnBtnClick(object sender, RoutedEventArgs e)
        {
            Result = (MessageBoxResult)((Button)sender).Tag;
            DialogResult = true;
        }
    }
}
