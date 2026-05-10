using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Window = System.Windows.Window;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SCNativeInstaller
{
    public partial class MainWindow : Window
    {
        private int _currentStep = 0;
        private const int TotalSteps = 4;
        private bool _isInstalling = false;

        // Цвета шагов
        private readonly System.Windows.Media.Color _activeColor = System.Windows.Media.Color.FromRgb(0xCC, 0x7B, 0x3A);
        private readonly System.Windows.Media.Color _doneColor = System.Windows.Media.Color.FromRgb(0xCC, 0x7B, 0x3A);
        private readonly System.Windows.Media.Color _inactiveColor = System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44);

        // Имя встроенного ресурса (app.zip внутри установщика)
        private const string PayloadResourceName = "app.zip";

        public MainWindow()
        {
            InitializeComponent();
            UpdateStepIndicator();
            UpdateButtons();
        }

        // ═══════════════════════════════════════
        // Навигация по шагам
        // ═══════════════════════════════════════

        private void GoToStep(int step)
        {
            StepWelcome.Visibility = Visibility.Collapsed;
            StepPath.Visibility = Visibility.Collapsed;
            StepInstalling.Visibility = Visibility.Collapsed;
            StepComplete.Visibility = Visibility.Collapsed;

            _currentStep = step;

            switch (step)
            {
                case 0: StepWelcome.Visibility = Visibility.Visible; break;
                case 1: StepPath.Visibility = Visibility.Visible; break;
                case 2: StepInstalling.Visibility = Visibility.Visible; break;
                case 3: StepComplete.Visibility = Visibility.Visible; break;
            }

            UpdateStepIndicator();
            UpdateButtons();
        }

        private void UpdateStepIndicator()
        {
            StepIndicator.Items.Clear();

            for (int i = 0; i < TotalSteps; i++)
            {
                var isDone = i < _currentStep;
                var isActive = i == _currentStep;
                var color = isActive ? _activeColor : isDone ? _doneColor : _inactiveColor;
                var text = isDone ? "✓" : (i + 1).ToString();

                var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

                var circle = new Border
                {
                    Width = 28, Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(color),
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = isDone ? 13 : 12,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    }
                };
                panel.Children.Add(circle);

                if (i < TotalSteps - 1)
                {
                    var line = new Border
                    {
                        Width = 40, Height = 2,
                        Background = new SolidColorBrush(i < _currentStep ? _doneColor : _inactiveColor),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    panel.Children.Add(line);
                }

                StepIndicator.Items.Add(panel);
            }
        }

        private void UpdateButtons()
        {
            BackButton.Visibility = _currentStep > 0 && _currentStep < 3
                ? Visibility.Visible : Visibility.Collapsed;

            CancelButton.Visibility = _currentStep < 3
                ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentStep)
            {
                case 0:
                    NextButton.Content = "Далее";
                    NextButton.IsEnabled = true;
                    break;
                case 1:
                    NextButton.Content = "Установить";
                    NextButton.IsEnabled = true;
                    break;
                case 2:
                    NextButton.Content = "Установка...";
                    NextButton.IsEnabled = false;
                    break;
                case 3:
                    NextButton.Content = "Готово";
                    NextButton.IsEnabled = true;
                    CancelButton.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // ═══════════════════════════════════════
        // Обработчики кнопок
        // ═══════════════════════════════════════

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            switch (_currentStep)
            {
                case 0: GoToStep(1); break;
                case 1:
                    GoToStep(2);
                    _ = StartInstallationAsync();
                    break;
                case 3:
                    if (CheckLaunchApp.IsChecked == true)
                        LaunchApp();
                    Application.Current.Shutdown();
                    break;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0 && _currentStep < 3)
                GoToStep(_currentStep - 1);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling)
            {
                var result = MessageBox.Show(
                    "Установка ещё не завершена. Вы уверены, что хотите прервать?",
                    "Отмена установки",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;

                try
                {
                    var installPath = PathBox.Text;
                    if (Directory.Exists(installPath))
                        Directory.Delete(installPath, true);
                }
                catch { }
            }
            Application.Current.Shutdown();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton_Click(sender, e);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isInstalling)
            {
                var result = MessageBox.Show(
                    "Установка ещё не завершена. Вы уверены, что хотите прервать?",
                    "Отмена установки",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                try
                {
                    var installPath = PathBox.Text;
                    if (Directory.Exists(installPath))
                        Directory.Delete(installPath, true);
                }
                catch { }
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку для установки SC Native",
                UseDescriptionForTitle = true,
                SelectedPath = PathBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                PathBox.Text = dialog.SelectedPath;
        }

        // ═══════════════════════════════════════
        // Установка — распаковка встроенного ресурса
        // ═══════════════════════════════════════

        private async Task StartInstallationAsync()
        {
            _isInstalling = true;
            var installPath = PathBox.Text;

            try
            {
                // 1. Создаём папку
                InstallStatus.Text = "Создание директории...";
                InstallProgress.Value = 5;
                InstallPercent.Text = "5%";
                await Task.Delay(100);

                if (!Directory.Exists(installPath))
                    Directory.CreateDirectory(installPath);

                // 2. Извлекаем встроенный payload (app.zip) на диск, потом распаковываем
                InstallStatus.Text = "Подготовка файлов...";
                InstallProgress.Value = 10;
                InstallPercent.Text = "10%";

                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                
                // Ищем ресурс app.zip — имя может быть "SCNativeInstaller.Payload.app.zip" или просто "app.zip"
                string? zipResourceName = resourceNames.FirstOrDefault(n => n.EndsWith(PayloadResourceName, StringComparison.OrdinalIgnoreCase));
                
                // В single-file режиме GetManifestResourceNames может вернуть пустой массив
                // но GetManifestResourceStream с правильным именем всё равно работает
                if (zipResourceName == null)
                {
                    // Пробуем стандартные имена
                    var candidates = new[]
                    {
                        $"{assembly.GetName().Name}.{PayloadResourceName}",
                        PayloadResourceName,
                        $"SCNativeInstaller.{PayloadResourceName}",
                        $"SCNativeSetup.{PayloadResourceName}"
                    };
                    
                    foreach (var candidate in candidates)
                    {
                        using var testStream = assembly.GetManifestResourceStream(candidate);
                        if (testStream != null)
                        {
                            zipResourceName = candidate;
                            break;
                        }
                    }
                }

                if (zipResourceName != null)
                {
                    // Распаковка из встроенного zip
                    var tempZip = Path.Combine(Path.GetTempPath(), "scnative_install.zip");
                    try
                    {
                        using (var stream = assembly.GetManifestResourceStream(zipResourceName)!)
                        using (var fs = File.Create(tempZip))
                        {
                            await stream.CopyToAsync(fs);
                        }

                        InstallStatus.Text = "Распаковка файлов...";
                        await ExtractWithProgressAsync(tempZip, installPath);
                    }
                    finally
                    {
                        if (File.Exists(tempZip))
                            try { File.Delete(tempZip); } catch { }
                    }
                }
                else
                {
                    // Fallback: проверяем app.zip рядом с exe (для dev-режима)
                    var sideZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PayloadResourceName);
                    if (File.Exists(sideZip))
                    {
                        InstallStatus.Text = "Распаковка файлов...";
                        await ExtractWithProgressAsync(sideZip, installPath);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Файлы приложения не найдены (resources: [{string.Join(", ", resourceNames)}]). " +
                            "Убедитесь, что установщик собран через build-release.bat");
                    }
                }

                // 3. Ярлыки
                InstallStatus.Text = "Создание ярлыков...";
                InstallProgress.Value = 88;
                InstallPercent.Text = "88%";
                await Task.Delay(100);

                var exePath = Path.Combine(installPath, "SoundCloudClient.exe");
                CreateShortcuts(exePath, installPath);

                // 4. Реестр
                InstallStatus.Text = "Регистрация в системе...";
                InstallProgress.Value = 93;
                InstallPercent.Text = "93%";
                await Task.Delay(100);

                RegisterUninstaller(installPath, exePath);

                // 5. Деинсталлятор
                InstallStatus.Text = "Создание деинсталлятора...";
                InstallProgress.Value = 97;
                InstallPercent.Text = "97%";
                await Task.Delay(100);

                CreateUninstaller(installPath);

                // Готово
                InstallProgress.Value = 100;
                InstallPercent.Text = "100%";
                InstallTitle.Text = "Установка завершена!";
                InstallStatus.Text = "";

                // Снимаем флаг ДО перехода на шаг «Готово»,
                // чтобы CancelButton_Click не удалил установленные файлы
                _isInstalling = false;

                await Task.Delay(400);
                GoToStep(3);
            }
            catch (Exception ex)
            {
                InstallTitle.Text = "Ошибка установки";
                InstallTitle.Foreground = new SolidColorBrush(Colors.Red);
                InstallStatus.Text = ex.Message;
                InstallProgress.Value = 0;
                InstallPercent.Text = "!";
                NextButton.IsEnabled = true;
                NextButton.Content = "Повторить";
            }
            finally
            {
                _isInstalling = false;
            }
        }

        private async Task ExtractWithProgressAsync(string zipPath, string destPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var totalEntries = archive.Entries.Count;
            var current = 0;

            foreach (var entry in archive.Entries)
            {
                var destFilePath = Path.Combine(destPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

                if (entry.Length == 0 && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                {
                    // Запись-директория — создаём папку
                    if (!Directory.Exists(destFilePath))
                        Directory.CreateDirectory(destFilePath);
                }
                else
                {
                    // Запись-файл — всегда гарантируем что родительская папка существует
                    var dir = Path.GetDirectoryName(destFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    entry.ExtractToFile(destFilePath, true);
                }

                current++;
                var progress = 10 + (int)((double)current / totalEntries * 75);
                InstallProgress.Value = progress;
                InstallPercent.Text = $"{progress}%";

                // Даём UI обновляться
                if (current % 10 == 0)
                    await Task.Delay(1);
            }
        }

        // ═══════════════════════════════════════
        // Ярлыки
        // ═══════════════════════════════════════

        private void CreateShortcuts(string exePath, string installPath)
        {
            try
            {
                if (CheckDesktopShortcut.IsChecked == true)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktop, "SC Native.lnk"), exePath, installPath, installPath);
                }

                if (CheckStartMenu.IsChecked == true)
                {
                    var programs = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Start Menu", "Programs");
                    var folder = Path.Combine(programs, "SC Native");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    CreateShortcut(Path.Combine(folder, "SC Native.lnk"), exePath, installPath, installPath);

                    var uninstallExe = Path.Combine(installPath, "SCNativeUninstall.exe");
                    CreateShortcut(Path.Combine(folder, "Удалить SC Native.lnk"),
                        uninstallExe, "", installPath);
                }

                if (CheckAutoStart.IsChecked == true)
                {
                    var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    CreateShortcut(Path.Combine(startup, "SC Native.lnk"), exePath, installPath, installPath);
                }
            }
            catch { }
        }

        private void CreateShortcut(string shortcutPath, string targetPath, string args, string workingDir)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                var shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.Arguments = args;
                shortcut.WorkingDirectory = workingDir;
                shortcut.Description = "SoundCloud Native — десктоп-клиент";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        // ═══════════════════════════════════════
        // Реестр
        // ═══════════════════════════════════════

        private void RegisterUninstaller(string installPath, string exePath)
        {
            try
            {
                var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SCNative");
                if (key == null) return;

                key.SetValue("DisplayName", "SC Native");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "SC Native");
                key.SetValue("DisplayIcon", exePath + ",0");
                key.SetValue("InstallLocation", installPath);
                key.SetValue("UninstallString", $"\"{Path.Combine(installPath, "SCNativeUninstall.exe")}\"");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
                key.SetValue("EstimatedSize", CalculateInstallSize(installPath));
                key.Close();
            }
            catch { }
        }

        private int CalculateInstallSize(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;
                var size = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                return (int)(size / 1024);
            }
            catch { return 0; }
        }

        // ═══════════════════════════════════════
        // Деинсталлятор
        // ═══════════════════════════════════════

        private void CreateUninstaller(string installPath)
        {
            try
            {
                var bat = Path.Combine(installPath, "uninstall.bat");
                // Экранируем путь для bat
                var escaped = installPath.Replace("\\", "\\\\");

                var script = $@"@echo off
chcp 65001 >nul
echo Удаление SC Native...
echo.

del /q ""%USERPROFILE%\Desktop\SC Native.lnk"" 2>nul
rd /s /q ""%APPDATA%\Microsoft\Windows\Start Menu\Programs\SC Native"" 2>nul
del /q ""%APPDATA%\Microsoft\Windows\Start Menu\Startup\SC Native.lnk"" 2>nul
reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\SCNative"" /f 2>nul

echo Удаление файлов...
timeout /t 2 /nobreak >nul
cd /d ""{installPath}""
cd ..
rd /s /q ""{installPath}"" 2>nul

echo.
echo SC Native удалён.
pause
";
                File.WriteAllText(bat, script, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ═══════════════════════════════════════
        // Запуск приложения
        // ═══════════════════════════════════════

        private void LaunchApp()
        {
            try
            {
                var exePath = Path.Combine(PathBox.Text, "SoundCloudClient.exe");
                if (File.Exists(exePath))
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
