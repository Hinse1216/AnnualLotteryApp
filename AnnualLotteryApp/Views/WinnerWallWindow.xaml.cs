using System.Windows;
using System.Windows.Input;

namespace AnnualLotteryApp.Views
{
    public partial class WinnerWallWindow : Window
    {
        public WinnerWallWindow(object dataContext)
        {
            InitializeComponent();
            DataContext = dataContext;
        }

        /// <summary>
        /// 按 Esc 关闭中奖大屏
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}