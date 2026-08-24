using System;
using System.Drawing;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    internal sealed class PageRangeDialog : Form
    {
        private readonly PageNumberInput startPageInput;
        private readonly PageNumberInput endPageInput;

        public PageRangeDialog(int pageCount, PageRange currentRange)
        {
            Text = "指定范围页盖章";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(420, 225);

            Label startLabel = new Label { AutoSize = true, Text = "起始页", Location = new Point(30, 34) };
            Label endLabel = new Label { AutoSize = true, Text = "结尾页", Location = new Point(30, 92) };
            startPageInput = CreateInput(pageCount, currentRange == null ? 1 : currentRange.StartPage, new Point(110, 22));
            endPageInput = CreateInput(pageCount, currentRange == null ? pageCount : currentRange.EndPage, new Point(110, 80));

            Label hint = new Label
            {
                AutoSize = true,
                Text = "页码范围：1 - " + pageCount,
                ForeColor = SystemColors.GrayText,
                Location = new Point(30, 145)
            };

            Button okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(240, 178), Size = new Size(72, 32) };
            Button cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(322, 178), Size = new Size(72, 32) };
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Controls.AddRange(new Control[] { startLabel, endLabel, startPageInput, endPageInput, hint, okButton, cancelButton });
        }

        public int StartPage => startPageInput.Value;
        public int EndPage => endPageInput.Value;

        private static PageNumberInput CreateInput(int pageCount, int value, Point location)
        {
            return new PageNumberInput(1, Math.Max(1, pageCount), value)
            {
                Location = location,
                Size = new Size(280, 46)
            };
        }

        private sealed class PageNumberInput : UserControl
        {
            private readonly int minimum;
            private readonly int maximum;
            private readonly TextBox textBox;

            public PageNumberInput(int minimum, int maximum, int value)
            {
                this.minimum = minimum;
                this.maximum = maximum;

                textBox = new TextBox
                {
                    Text = Clamp(value).ToString(),
                    TextAlign = HorizontalAlignment.Center,
                    Font = new Font("Microsoft YaHei UI", 12F),
                    Location = new Point(0, 7),
                    Size = new Size(190, 29)
                };
                textBox.KeyPress += (sender, args) =>
                {
                    if (!char.IsDigit(args.KeyChar) && args.KeyChar != (char)Keys.Back && args.KeyChar != (char)Keys.Delete)
                    {
                        args.Handled = true;
                    }
                };

                Button downButton = new Button
                {
                    Text = "−",
                    Font = new Font("Microsoft YaHei UI", 13F),
                    Location = new Point(198, 2),
                    Size = new Size(38, 40),
                    TabStop = false
                };
                Button upButton = new Button
                {
                    Text = "+",
                    Font = new Font("Microsoft YaHei UI", 13F),
                    Location = new Point(240, 2),
                    Size = new Size(38, 40),
                    TabStop = false
                };
                downButton.Click += (sender, args) => Value--;
                upButton.Click += (sender, args) => Value++;
                Controls.AddRange(new Control[] { textBox, downButton, upButton });
            }

            public int Value
            {
                get
                {
                    return int.TryParse(textBox.Text, out int value) ? value : 0;
                }
                set
                {
                    textBox.Text = Clamp(value).ToString();
                }
            }

            private int Clamp(int value)
            {
                return Math.Max(minimum, Math.Min(maximum, value));
            }
        }
    }
}
