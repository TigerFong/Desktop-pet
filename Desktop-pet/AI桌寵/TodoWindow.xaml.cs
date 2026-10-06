using System;
using System.Windows;

namespace SmartDesktopPet
{
    public partial class TodoWindow : Window
    {
        public string TaskName { get; private set; } = "";
        public string ReminderTimeStr { get; private set; } = "";

        public TodoWindow()
        {
            InitializeComponent();

            // 預設填入當前時間之後的 1 小時作為範例提示
            TimeInputBox.Text = DateTime.Now.AddHours(1).ToString("yyyy/MM/dd/HH/mm");
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            string task = TaskInputBox.Text.Trim();
            string timeStr = TimeInputBox.Text.Trim();

            if (string.IsNullOrEmpty(task))
            {
                // 🌸 明確指定使用 System.Windows.MessageBox 避免混淆
                System.Windows.MessageBox.Show("請輸入待辦事項名稱！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!DateTime.TryParseExact(timeStr, "yyyy/MM/dd/HH/mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            {
                // 🌸 明確指定使用 System.Windows.MessageBox 避免混淆
                System.Windows.MessageBox.Show("時間格式錯誤！請依照 yyyy/MM/dd/HH/mm 格式輸入（例如：2026/06/08/15/30）", "格式錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TaskName = task;
            ReminderTimeStr = timeStr;

            DialogResult = true;
            Close();
        }
    }
}