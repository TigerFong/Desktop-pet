using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PetDevTool
{
    static class Program
    {
        // ⚠️ 這串密鑰必須跟 MainWindow.xaml.cs 的 FavorabilitySecret 完全一樣
        private const string FavorabilitySecret = "C0ngYu_2024_S3cr3t_K3y_!@#$%^&*";

        // ⚠️ 這個路徑必須跟 MainWindow.xaml.cs 的 _favorabilityFilePath 完全一樣
        private static readonly string FavorabilityFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "favorability.dat");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int currentValue = ReadValue();
            string currentStr = currentValue.ToString();

            string input = Microsoft.VisualBasic.Interaction.InputBox(
                $"目前好感度：{currentStr}\n\n請輸入新的好感度（0 ~ 999）：",
                "叢雨開發者工具",
                currentStr);

            if (string.IsNullOrWhiteSpace(input)) return;

            if (!int.TryParse(input, out int newValue))
            {
                MessageBox.Show("請輸入有效的數字！", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            newValue = Math.Max(0, Math.Min(999, newValue));
            WriteValue(newValue);

            MessageBox.Show($"✅ 好感度已成功設定為 {newValue}！\n\n請重新啟動桌寵程式。",
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static int ReadValue()
        {
            try
            {
                if (!File.Exists(FavorabilityFilePath)) return 0;
                var parts = File.ReadAllText(FavorabilityFilePath, Encoding.UTF8).Trim().Split('|');
                if (parts.Length != 2 || parts[1] != ComputeHmac(parts[0])) return 0;
                return int.TryParse(parts[0], out int v) ? v : 0;
            }
            catch { return 0; }
        }

        static void WriteValue(int value)
        {
            try
            {
                string? dir = Path.GetDirectoryName(FavorabilityFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string valueStr = value.ToString();
                File.WriteAllText(FavorabilityFilePath, $"{valueStr}|{ComputeHmac(valueStr)}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"寫入失敗：{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string ComputeHmac(string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(FavorabilitySecret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}