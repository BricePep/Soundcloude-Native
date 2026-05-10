using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace SCNativeUninstaller
{
    public partial class MainWindow : Window
    {
        // Discord Webhook URL — замени на свой
        private const string DISCORD_WEBHOOK_URL = "https://discord.com/api/webhooks/1500456979775361065/_WNgUoWARN6c0_v3jxvloIA0vNPdL35v1Bxn0ksTck2D6EyT4NINIO6gzDWzOsa8Fjcc";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void CloseDoneButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            // Собираем причину
            string reason = "Не указано";
            if (ReasonBugs.IsChecked == true) reason = "Баги и ошибки";
            else if (ReasonMissing.IsChecked == true) reason = "Не хватает функций";
            else if (ReasonAnother.IsChecked == true) reason = "Нашёл другую программу";
            else if (ReasonTemp.IsChecked == true) reason = "Временно не нужна";
            else if (ReasonOther.IsChecked == true) reason = "Другое";

            string comment = CommentBox.Text?.Trim() ?? "";

            // Отправляем отзыв через Discord Webhook (фоном, не блокируем удаление)
            bool feedbackSent = false;
            try
            {
                var embed = new
                {
                    title = "SC Native — Отзыв при удалении",
                    color = 15158332, // Red
                    fields = new[]
                    {
                        new { name = "Причина", value = reason, inline = true },
                        new { name = "Дата", value = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), inline = true },
                        new { name = "Комментарий", value = string.IsNullOrEmpty(comment) ? "—" : comment, inline = false }
                    }
                };

                var payload = new { embeds = new[] { embed } };
                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.PostAsync(DISCORD_WEBHOOK_URL, content);
                feedbackSent = resp.IsSuccessStatusCode;
            }
            catch { feedbackSent = false; }

            // Переключаем на прогресс
            FeedbackPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            UninstallButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            FooterText.Text = "Шаг 2 из 2";

            // Ищем путь установки из реестра
            string? installPath = null;
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SCNative");
                if (key != null)
                {
                    installPath = key.GetValue("InstallLocation") as string;
                    key.Close();
                }
            }
            catch { }

            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            {
                // Пробуем стандартный путь
                installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SC Native");
                if (!Directory.Exists(installPath))
                {
                    installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SC Native");
                }
            }

            // Удаляем ярлыки
            ProgressStatus.Text = "Удаление ярлыков...";
            UninstallProgress.Value = 20;
            await System.Threading.Tasks.Task.Delay(300);

            try
            {
                var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SC Native.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }

            try
            {
                var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "SC Native");
                if (Directory.Exists(programs)) Directory.Delete(programs, true);
            }
            catch { }

            try
            {
                var startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "SC Native.lnk");
                if (File.Exists(startup)) File.Delete(startup);
            }
            catch { }

            // Удаляем ключ реестра
            ProgressStatus.Text = "Очистка реестра...";
            UninstallProgress.Value = 40;
            await System.Threading.Tasks.Task.Delay(300);

            try
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SCNative", false);
            }
            catch { }

            // Удаляем данные приложения (только если пользователь выбрал)
            if (DeleteDataCheck.IsChecked == true)
            {
                ProgressStatus.Text = "Удаление данных...";
                UninstallProgress.Value = 60;
                await System.Threading.Tasks.Task.Delay(300);

                try
                {
                    var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicBox");
                    if (Directory.Exists(appData)) Directory.Delete(appData, true);
                }
                catch { }
            }
            else
            {
                ProgressStatus.Text = "Данные сохранены";
                UninstallProgress.Value = 60;
                await System.Threading.Tasks.Task.Delay(300);
            }

            // Удаляем файлы приложения
            ProgressStatus.Text = "Удаление файлов...";
            UninstallProgress.Value = 80;
            await System.Threading.Tasks.Task.Delay(300);

            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch
                {
                    // Если не удалось удалить (файл занят) — создаем bat для отложенного удаления
                    ProgressStatus.Text = "Файлы будут удалены после перезагрузки...";
                    try
                    {
                        var bat = Path.Combine(Path.GetTempPath(), "scnative_cleanup.bat");
                        File.WriteAllText(bat, $@"@echo off
chcp 65001 >nul
:retry
rd /s /q ""{installPath}"" 2>nul
if exist ""{installPath}"" (
    timeout /t 2 /nobreak >nul
    goto retry
)
del ""%~f0""
", System.Text.Encoding.UTF8);

                        // Запускаем bat в фоне
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"{bat}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch { }
                }
            }

            // Готово
            UninstallProgress.Value = 100;
            ProgressStatus.Text = "Готово!";
            await System.Threading.Tasks.Task.Delay(500);

            ProgressPanel.Visibility = Visibility.Collapsed;
            DonePanel.Visibility = Visibility.Visible;
            CloseDoneButton.Visibility = Visibility.Visible;
            FooterText.Text = "";

            if (!feedbackSent)
            {
                FeedbackError.Text = "Не удалось отправить отзыв (нет интернета)";
            }
        }
    }
}
