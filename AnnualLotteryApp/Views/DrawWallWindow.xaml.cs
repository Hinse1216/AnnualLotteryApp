using System.Windows;
using System.Windows.Input;

namespace AnnualLotteryApp.Views
{
    public partial class DrawWallWindow : Window
    {
        public DrawWallWindow(object dataContext)
        {
            InitializeComponent();
            DataContext = dataContext;
            Loaded += DrawWallWindow_Loaded;
            this.Closing += DrawWallWindow_Closing;
        }

        /// <summary>
        /// 窗口加载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DrawWallWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口可以接收键盘焦点
            this.Focusable = true;
            this.Focus();
        }

        /// <summary>
        /// 窗口关闭
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DrawWallWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                // 抽奖窗口关闭时，如果仍在抽奖中，则中止本轮且不记入中奖名单
                vm.CancelCurrentDrawOnWallClosing();
            }
        }

        /// <summary>
        /// 按键按下事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // 按下 ESC 键关闭窗口
            if (e.Key == Key.Escape)
            {
                this.Close();
                return;
            }

            if (DataContext is MainViewModel vm)
            {
                // Ctrl+1 开始抽奖
                if (e.Key == Key.D1 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (vm.StartDrawCommand.CanExecute(null))
                    {
                        vm.StartDrawCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                // Ctrl+2 或空格键 停止抽奖
                else if ((e.Key == Key.D2 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                         || e.Key == Key.Space)
                {
                    if (vm.StopDrawCommand.CanExecute(null))
                    {
                        vm.StopDrawCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}