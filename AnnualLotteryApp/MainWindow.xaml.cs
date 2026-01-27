using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace AnnualLotteryApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        /// <summary>
        /// 窗口加载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 先尝试从 data.conf 读取奖项和人数配置
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var confPath = System.IO.Path.Combine(baseDir, "data", "data.conf");
                if (File.Exists(confPath) && DataContext is MainViewModel vm)
                {
                    var lines = File.ReadAllLines(confPath)
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                                    .ToList();

                    if (lines.Count > 0)
                    {
                        vm.LoadPrizeConfig(lines);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 /data/data.conf 配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 再读取 /data/user.txt 名单
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var userFilePath = System.IO.Path.Combine(baseDir, "data", "user.txt");

                if (File.Exists(userFilePath) && DataContext is MainViewModel vm)
                {
                    var lines = File.ReadAllLines(userFilePath)
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToList();

                    if (lines.Count > 0)
                    {
                        vm.LoadParticipantsFromList(lines);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 /data/user.txt 名单失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 窗口关闭时，确保所有窗口都关闭，应用退出
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // 关闭除主窗口外的所有其他窗口
            foreach (Window win in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (!ReferenceEquals(win, this))
                {
                    win.Close();
                }
            }

            // 确保应用退出
            Application.Current.Shutdown();
        }
    }
}