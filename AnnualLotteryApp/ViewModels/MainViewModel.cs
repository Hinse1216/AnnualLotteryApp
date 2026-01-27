using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AnnualLotteryApp
{
    /// <summary>
    /// 抽奖应用主 ViewModel，负责人员名单、奖项配置、抽奖流程以及与前端窗口的交互。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly Random _random = new Random();
        private readonly ObservableCollection<string> _allParticipants = new ObservableCollection<string>();
        private readonly ObservableCollection<WinnerItem> _allWinnerItems = new ObservableCollection<WinnerItem>();

        // 奖项 -> 配置的应抽人数
        private readonly Dictionary<string, int> _prizeDrawCounts = new Dictionary<string, int>();

        // 仅保留一个抽奖大屏和一个中奖墙窗口实例，避免重复打开多个窗口
        private Views.DrawWallWindow _drawWallWindow;

        private Views.WinnerWallWindow _winnerWallWindow;

        // 标记当前打开的抽奖大屏是否已经完成过一次抽奖
        private bool _hasDrawnInCurrentWall;

        // 标记当前是否已经打开过抽奖大屏（DrawWallWindow）
        private bool _isDrawWallOpened;

        // 抽奖主界面标题文本，可通过配置文件或“恢复默认”进行修改
        private string _titleText = "2026 公司年会抽奖";

        /// <summary>
        /// 抽奖主界面标题文本（绑定到窗口标题或页面标题）。
        /// </summary>
        public string TitleText
        {
            get => _titleText;
            set
            {
                if (_titleText == value) return;
                _titleText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 当前可用的奖项列表，顺序用于界面展示和默认选中。
        /// </summary>
        public ObservableCollection<string> PrizeLevels { get; } = new ObservableCollection<string>()
        {
            "特等奖",
            "一等奖",
            "二等奖",
            "三等奖",
            "幸运奖"
        };

        // 当前选中的奖项名称
        private string _selectedPrizeLevel = "三等奖";

        /// <summary>
        /// 当前选中的奖项，用于控制每轮抽奖对应的奖项名称和人数配置。
        /// </summary>
        public string SelectedPrizeLevel
        {
            get => _selectedPrizeLevel;
            set
            {
                if (_selectedPrizeLevel == value) return;
                _selectedPrizeLevel = value;
                OnPropertyChanged();

                // 当奖项切换时，如果 data.conf 中配置了该奖项的应抽人数，则自动填充到 DrawCount
                if (!string.IsNullOrEmpty(_selectedPrizeLevel)
                    && _prizeDrawCounts.TryGetValue(_selectedPrizeLevel, out var configuredCount)
                    && configuredCount > 0)
                {
                    DrawCount = configuredCount;
                }

                OnPropertyChanged(nameof(CurrentPrizeDrawCount));
            }
        }

        // 当前轮计划抽取的人数（可能由配置自动带出，也可以手动调整）
        private int _drawCount = 1;

        /// <summary>
        /// 当前轮拟抽取人数，最小值为 1。
        /// </summary>
        public int DrawCount
        {
            get => _drawCount;
            set
            {
                _drawCount = value <= 0 ? 1 : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPrizeDrawCount));
            }
        }

        // 当前奖项“应抽人数”，目前等同于 DrawCount，预留扩展点以支持更复杂逻辑
        public int CurrentPrizeDrawCount => DrawCount;

        /// <summary>
        /// 抽奖大屏上滚动显示的姓名集合，随计时器实时更新。
        /// </summary>
        public ObservableCollection<string> RollingDisplayNames { get; } = new ObservableCollection<string>();

        /// <summary>
        /// 当前轮次中实际中奖的姓名列表。
        /// </summary>
        public ObservableCollection<string> CurrentWinners { get; } = new ObservableCollection<string>();

        /// <summary>
        /// 全部中奖记录（包含奖项名称和姓名），用于中奖墙等展示。
        /// </summary>
        public ObservableCollection<WinnerItem> AllWinnersForWall => _allWinnerItems;

        /// <summary>
        /// 当前参与抽奖的总人数（去除空行）。
        /// </summary>
        public int TotalParticipants => _allParticipants.Count;

        /// <summary>
        /// 剩余尚未中奖的人数，用于控制是否还能继续抽奖。
        /// </summary>
        public int RemainingParticipants => _allParticipants.Count - _allWinnerItems.Count;

        private bool _isDrawing;

        /// <summary>
        /// 是否处于抽奖滚动状态，用于控制开始/停止按钮以及前端动画。
        /// </summary>
        public bool IsDrawing
        {
            get => _isDrawing;
            private set { _isDrawing = value; OnPropertyChanged(); }
        }

        private bool _hasStoppedOnce;

        /// <summary>
        /// 当前应用运行期间是否至少停止过一次抽奖，用于前端界面联动（如按钮状态）。
        /// </summary>
        public bool HasStoppedOnce
        {
            get => _hasStoppedOnce;
            private set { _hasStoppedOnce = value; OnPropertyChanged(); }
        }

        private readonly DispatcherTimer _rollingTimer;

        /// <summary>
        /// 开始抽奖命令：只有在已打开抽奖大屏且尚未抽过、并且仍有可抽人员时才能执行。
        /// </summary>
        public ICommand StartDrawCommand { get; }

        /// <summary>
        /// 停止抽奖命令：用于结束当前滚动并生成中奖结果。
        /// </summary>
        public ICommand StopDrawCommand { get; }

        /// <summary>
        /// 导入名单命令：从外部文本文件加载参与人员。
        /// </summary>
        public ICommand ImportCommand { get; }

        /// <summary>
        /// 导出中奖结果命令：导出结果文件到用户指定位置。
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// 显示全部中奖记录的命令，以弹窗形式列出所有中奖名单。
        /// </summary>
        public ICommand ShowAllWinnersCommand { get; }

        /// <summary>
        /// 打开/激活中奖墙窗口的命令。
        /// </summary>
        public ICommand ShowWinnerWallCommand { get; }

        /// <summary>
        /// 打开/激活抽奖大屏窗口的命令。
        /// </summary>
        public ICommand ShowDrawWallCommand { get; }

        /// <summary>
        /// 恢复默认标题、奖项配置并清空中奖记录的命令。
        /// </summary>
        public ICommand ResetConfigCommand { get; }

        /// <summary>
        /// 初始化 ViewModel：绑定命令、读取配置文件、准备默认数据以及滚动计时器。
        /// </summary>
        public MainViewModel()
        {
            // 初始化命令（去掉对 _isDrawWallOpened/_hasDrawnInCurrentWall 的直接字段引用，使用属性或保持不变）
            StartDrawCommand = new RelayCommand(
                _ => StartDraw(),
                _ => !_isDrawing && RemainingParticipants > 0 && !_hasDrawnInCurrentWall && _isDrawWallOpened);
            StopDrawCommand = new RelayCommand(_ => StopDraw(), _ => _isDrawing);
            ImportCommand = new RelayCommand(_ => ImportNames());
            ExportCommand = new RelayCommand(_ => ExportResults(), _ => _allWinnerItems.Count > 0);
            ShowAllWinnersCommand = new RelayCommand(_ => ShowAllWinners(), _ => _allWinnerItems.Count > 0);
            ShowWinnerWallCommand = new RelayCommand(_ => ShowWinnerWall(), _ => true);
            ShowDrawWallCommand = new RelayCommand(_ => ShowDrawWall());
            ResetConfigCommand = new RelayCommand(_ => ResetToDefaultConfig());

            _rollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _rollingTimer.Tick += (s, e) =>
            {
                if (_isDrawing)
                {
                    UpdateRollingDisplay();
                }
            };

            // 先加载 data.conf：同时解析标题和奖项配置
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataConfigPath = Path.Combine(baseDir, "data", "data.conf");
                if (File.Exists(dataConfigPath))
                {
                    var lines = File.ReadAllLines(dataConfigPath, Encoding.UTF8);
                    // 先处理标题，再把剩余行作为奖项配置传入 LoadPrizeConfig
                    var prizeLines = new List<string>();

                    foreach (var raw in lines)
                    {
                        var line = (raw ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            // 注释行直接跳过
                            continue;
                        }

                        // 优先判断 Title 配置行：Title=xxxx 或 Title：xxxx
                        var titleParts = line.Split('=', '\uff1a');
                        if (titleParts.Length >= 2 &&
                            titleParts[0].Trim().Equals("Title", StringComparison.OrdinalIgnoreCase))
                        {
                            var title = titleParts[1].Trim();
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                TitleText = title;
                            }
                            // 标题行不再进入奖项解析
                            continue;
                        }

                        // 其余行保留给奖项解析
                        prizeLines.Add(line);
                    }

                    if (prizeLines.Count > 0)
                    {
                        LoadPrizeConfig(prizeLines);
                    }
                }
            }
            catch
            {
                // 忽略配置读取失败，保留默认标题与默认奖项设置
            }

            // 默认一些演示数据，避免空界面（如外部未导入名单时）
            if (_allParticipants.Count == 0)
            {
                _allParticipants.Add("张三");
                _allParticipants.Add("李四");
                _allParticipants.Add("王五");
                _allParticipants.Add("赵六");
                _allParticipants.Add("孙七");
            }

            OnPropertyChanged(nameof(TotalParticipants));
            OnPropertyChanged(nameof(RemainingParticipants));

            UpdateRollingDisplay();
        }

        /// <summary>
        /// 从外部提供的姓名列表加载人员名单，一行一个姓名。
        /// 用于从 /data/user.txt 初始化名单。
        /// </summary>
        public void LoadParticipantsFromList(IEnumerable<string> names)
        {
            _allParticipants.Clear();
            _allWinnerItems.Clear();
            CurrentWinners.Clear();
            RollingDisplayNames.Clear();

            foreach (var n in names)
            {
                var name = (n ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _allParticipants.Add(name);
                }
            }

            OnPropertyChanged(nameof(TotalParticipants));
            OnPropertyChanged(nameof(RemainingParticipants));
            OnCommandsCanExecuteChanged();
            UpdateRollingDisplay();
        }

        /// <summary>
        /// 从配置文件行列表加载奖项配置。
        /// 每行格式："奖项名称=人数"，如："一等奖=3"。
        /// 同时支持 "=" 或全角冒号 "：" 分隔，支持前后空白。
        /// 调用方需已过滤掉 Title= 等非奖项配置行，但这里仍做一次防御性过滤。
        /// </summary>
        public void LoadPrizeConfig(IEnumerable<string> lines)
        {
            var prizes = new ObservableCollection<string>();
            int firstPrizeCount = 1;
            string firstPrizeName = null;

            _prizeDrawCounts.Clear();

            foreach (var raw in lines)
            {
                var line = (raw ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                // 防御性处理：如果误把 Title 行传进来，这里直接跳过，避免被当作奖项名
                var probeParts = line.Split('=', '\uff1a');
                if (probeParts.Length >= 2 &&
                    probeParts[0].Trim().Equals("Title", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = probeParts;
                if (parts.Length >= 1)
                {
                    var name = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    int count = 1;
                    if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var cnt) && cnt > 0)
                    {
                        count = cnt;
                    }

                    prizes.Add(name);
                    _prizeDrawCounts[name] = count;

                    if (firstPrizeName == null)
                    {
                        firstPrizeName = name;
                        firstPrizeCount = count;
                    }
                }
            }

            if (prizes.Count == 0) return;

            PrizeLevels.Clear();
            foreach (var p in prizes)
            {
                PrizeLevels.Add(p);
            }

            if (firstPrizeName != null)
            {
                SelectedPrizeLevel = firstPrizeName;
                DrawCount = firstPrizeCount;
            }

            OnPropertyChanged(nameof(CurrentPrizeDrawCount));
        }

        /// <summary>
        /// 开始新一轮抽奖：校验状态、清空当前轮结果并启动滚动显示计时器。
        /// </summary>
        private void StartDraw()
        {
            if (!_isDrawWallOpened)
            {
                MessageBox.Show("请先打开抽奖大屏，然后再开始抽奖。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_hasDrawnInCurrentWall)
            {
                MessageBox.Show("本次抽奖大屏已经完成一次抽奖，如需再次抽取请重新打开抽奖大屏。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (RemainingParticipants <= 0)
            {
                MessageBox.Show("没有可抽取的人员，请先导入名单。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsDrawing = true;
            CurrentWinners.Clear();
            UpdateRollingDisplay();
            _rollingTimer.Start();
            OnCommandsCanExecuteChanged();
        }

        /// <summary>
        /// 停止当前抽奖：从未中奖人员中随机选出本轮中奖名单，并立即追加保存到本地 CSV 文件。
        /// </summary>
        private void StopDraw()
        {
            if (!IsDrawing) return;

            IsDrawing = false;
            HasStoppedOnce = true;
            _rollingTimer.Stop();
            CurrentWinners.Clear();

            var available = new ObservableCollection<string>();
            foreach (var name in _allParticipants)
            {
                if (!_allWinnerItems.Any(w => w.Name == name))
                    available.Add(name);
            }

            var count = Math.Min(DrawCount, available.Count);
            RollingDisplayNames.Clear();

            var newWinners = new List<WinnerItem>();
            for (int i = 0; i < count; i++)
            {
                if (available.Count == 0) break;
                var index = _random.Next(available.Count);
                var winner = available[index];
                available.RemoveAt(index);

                var fullText = winner;
                CurrentWinners.Add(fullText);
                RollingDisplayNames.Add(fullText);

                var item = new WinnerItem { Prize = SelectedPrizeLevel, Name = winner };
                _allWinnerItems.Add(item);
                newWinners.Add(item);
            }

            // 标记当前大屏已经完成一次抽奖
            _hasDrawnInCurrentWall = true;

            // 将本轮新产生的中奖结果立即追加写入 /data/result.csv，防止程序异常导致结果丢失
            try
            {
                if (newWinners.Count > 0)
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var dataDir = Path.Combine(baseDir, "data");
                    if (!Directory.Exists(dataDir))
                    {
                        Directory.CreateDirectory(dataDir);
                    }

                    var csvPath = Path.Combine(dataDir, "result.csv");
                    var now = DateTime.Now;

                    var sb = new StringBuilder();

                    // 如果文件不存在，先写入表头
                    if (!File.Exists(csvPath))
                    {
                        sb.AppendLine("奖项,姓名,中奖时间");
                    }

                    foreach (var item in newWinners)
                    {
                        // 简单的 CSV 转义：用双引号包起来，并将内部双引号替换为两个双引号
                        string Esc(string v) => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";

                        sb.AppendLine(string.Join(",", new[]
                        {
                            Esc(item.Prize),
                            Esc(item.Name),
                            Esc(now.ToString("yyyy-MM-dd HH:mm:ss"))
                        }));
                    }

                    File.AppendAllText(csvPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 实时保存失败时不影响前台逻辑，最多丢失本轮的追加记录
            }

            OnPropertyChanged(nameof(RemainingParticipants));
            OnCommandsCanExecuteChanged();
        }

        /// <summary>
        /// 供抽奖大屏窗口在关闭时调用：如果仍在抽奖中，则中止本轮并不记录中奖结果，
        /// 但本轮视为已经占用过一次抽奖机会。
        /// </summary>
        public void CancelCurrentDrawOnWallClosing()
        {
            // 无论当前是否在抽奖中，都将本轮视为已完成，防止再次开始
            _hasDrawnInCurrentWall = true;

            if (!IsDrawing)
            {
                OnCommandsCanExecuteChanged();
                return;
            }

            // 只是停掉动画和状态，不生成中奖名单
            IsDrawing = false;
            _rollingTimer.Stop();
            CurrentWinners.Clear();
            RollingDisplayNames.Clear();

            OnCommandsCanExecuteChanged();
        }

        /// <summary>
        /// 更新前端滚动显示名单：
        /// 抽奖中随机滚动；未开始时显示所有待抽人员；停止后显示本轮中奖人员。
        /// </summary>
        private void UpdateRollingDisplay()
        {
            RollingDisplayNames.Clear();

            if (IsDrawing)
            {
                // 抽奖中：随机滚动显示待抽奖人员
                var candidates = _allParticipants.Where(p => !_allWinnerItems.Any(w => w.Name == p)).ToList();
                if (candidates.Count == 0)
                    return;

                int showCount = Math.Min(50, candidates.Count);
                for (int i = 0; i < showCount; i++)
                {
                    var name = candidates[_random.Next(candidates.Count)];
                    RollingDisplayNames.Add(name);
                }
            }
            else if (CurrentWinners.Count == 0)
            {
                // 未开始：展示所有待抽奖人员
                var candidates = _allParticipants.Where(p => !_allWinnerItems.Any(w => w.Name == p)).ToList();
                foreach (var name in candidates)
                {
                    RollingDisplayNames.Add(name);
                }
            }
            else
            {
                // 停止后：展示本轮中奖人员
                foreach (var item in CurrentWinners)
                {
                    RollingDisplayNames.Add(item);
                }
            }
        }

        /// <summary>
        /// 通过文件对话框导入参与名单，每行一个姓名，导入后会清空历史中奖记录。
        /// </summary>
        private void ImportNames()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
                };
                if (dialog.ShowDialog() == true)
                {
                    var lines = File.ReadAllLines(dialog.FileName, Encoding.UTF8);
                    _allParticipants.Clear();
                    foreach (var line in lines)
                    {
                        var name = line.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            _allParticipants.Add(name);
                    }
                    _allWinnerItems.Clear();
                    CurrentWinners.Clear();
                    UpdateRollingDisplay();
                    OnPropertyChanged(nameof(TotalParticipants));
                    OnPropertyChanged(nameof(RemainingParticipants));
                    OnCommandsCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导出当前中奖结果：
        /// 1. 优先将内部 /data/result.csv 拷贝到用户选择目录；
        /// 2. 同时弹出导出成功提示。
        /// </summary>
        private void ExportResults()
        {
            try
            {
                var filename = $"年会抽奖结果_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = filename
                };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        var dataDir = Path.Combine(baseDir, "data");
                        var csvPath = Path.Combine(dataDir, "result.csv");
                        if (File.Exists(csvPath))
                        {
                            var targetDir = Path.GetDirectoryName(dialog.FileName) ?? baseDir;
                            var csvTargetName = filename;
                            var csvTargetPath = Path.Combine(targetDir, csvTargetName);
                            File.Copy(csvPath, csvTargetPath, overwrite: false);
                        }
                    }
                    catch
                    {
                        // csv 导出失败不影响主结果导出
                    }

                    MessageBox.Show("导出成功!", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 显示或激活中奖墙窗口，用于大屏展示全部中奖名单。
        /// </summary>
        private void ShowWinnerWall()
        {
            // 如果已有窗口实例且还在显示，则直接激活
            if (_winnerWallWindow != null && _winnerWallWindow.IsVisible)
            {
                _winnerWallWindow.Activate();
                return;
            }

            _winnerWallWindow = new Views.WinnerWallWindow(this);
            _winnerWallWindow.Closed += (s, e) =>
            {
                _winnerWallWindow = null;
            };
            _winnerWallWindow.Show();
        }

        /// <summary>
        /// 显示或激活抽奖大屏窗口，并在打开前重置本轮抽奖状态。
        /// </summary>
        private void ShowDrawWall()
        {
            // 如果已有抽奖大屏且仍在显示，则直接激活，不再新开
            if (_drawWallWindow != null && _drawWallWindow.IsVisible)
            {
                _drawWallWindow.Activate();
                return;
            }

            // 打开抽奖大屏前，恢复到待抽奖状态，并重置一次抽奖标记
            IsDrawing = false;
            HasStoppedOnce = false;
            _rollingTimer.Stop();
            CurrentWinners.Clear();
            _hasDrawnInCurrentWall = false;
            _isDrawWallOpened = true;
            UpdateRollingDisplay();
            OnCommandsCanExecuteChanged();

            _drawWallWindow = new Views.DrawWallWindow(this);
            _drawWallWindow.Closed += (s, e) =>
            {
                // 关闭时通知 VM 取消当前大屏的抽奖轮次标记，并清空引用
                CancelCurrentDrawOnWallClosing();
                _isDrawWallOpened = false;
                _drawWallWindow = null;
            };
            _drawWallWindow.Show();
        }

        /// <summary>
        /// 弹窗展示所有中奖记录，按“[奖项] 姓名”的形式列出。
        /// </summary>
        private void ShowAllWinners()
        {
            if (_allWinnerItems.Count == 0)
            {
                MessageBox.Show("当前还没有任何中奖记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("全部中奖名单：");
            foreach (var winner in _allWinnerItems)
            {
                sb.AppendLine($"[{winner.Prize}] {winner.Name}");
            }

            MessageBox.Show(sb.ToString(), "中奖记录", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 恢复默认标题和奖项配置，清空内存中奖记录，并重置 data.conf 与 result.csv。
        /// </summary>
        private void ResetToDefaultConfig()
        {
            // 二次确认，防止误操作
            var result = MessageBox.Show(
                "确定要恢复默认配置吗？\r\n\r\n这将重置标题和奖项配置、清空中奖记录，并清空本地中奖结果文件（data/result.csv）。",
                "确认恢复默认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 恢复默认标题（代码中的内置默认值）
            TitleText = "2026 公司年会抽奖";

            // 恢复默认奖项和人数配置（与 PrizeLevels 初始值一致）
            PrizeLevels.Clear();
            PrizeLevels.Add("特等奖");
            PrizeLevels.Add("一等奖");
            PrizeLevels.Add("二等奖");
            PrizeLevels.Add("三等奖");
            PrizeLevels.Add("幸运奖");

            _prizeDrawCounts.Clear();
            _prizeDrawCounts["特等奖"] = 1;
            _prizeDrawCounts["一等奖"] = 3;
            _prizeDrawCounts["二等奖"] = 5;
            _prizeDrawCounts["三等奖"] = 10;
            _prizeDrawCounts["幸运奖"] = 20;

            SelectedPrizeLevel = "三等奖";
            DrawCount = 10;

            OnPropertyChanged(nameof(CurrentPrizeDrawCount));

            // 清空当前轮和历史中奖记录，并刷新界面显示
            CurrentWinners.Clear();
            _allWinnerItems.Clear();
            RollingDisplayNames.Clear();
            OnPropertyChanged(nameof(RemainingParticipants));
            OnCommandsCanExecuteChanged();

            // 同步覆盖写入 data/data.conf 为默认配置，保证下次启动与当前一致
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataDir = Path.Combine(baseDir, "data");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                var confPath = Path.Combine(dataDir, "data.conf");
                var sbConf = new StringBuilder();
                sbConf.AppendLine("# 抽奖配置：由应用在“恢复默认”操作时自动生成");
                sbConf.AppendLine("# 标题配置");
                sbConf.AppendLine("Title=" + TitleText);
                sbConf.AppendLine();
                sbConf.AppendLine("# 奖项配置示例（名称=人数）");
                sbConf.AppendLine("特等奖=1");
                sbConf.AppendLine("一等奖=3");
                sbConf.AppendLine("二等奖=5");
                sbConf.AppendLine("三等奖=10");
                sbConf.AppendLine("幸运奖=20");

                File.WriteAllText(confPath, sbConf.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 配置文件写入失败不影响内存中已恢复的默认配置，仅可能导致下次启动仍按旧配置读取
            }

            // 删除 data/result.csv，清空历史中奖结果的持久化文件
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataDir = Path.Combine(baseDir, "data");
                var csvPath = Path.Combine(dataDir, "result.csv");
                if (File.Exists(csvPath))
                {
                    File.Delete(csvPath);
                }
            }
            catch
            {
                // 删除失败不影响前台逻辑，仅可能残留旧文件
            }

            MessageBox.Show("已恢复默认标题和奖项配置，清空中奖记录，并清空本地中奖结果文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 手动触发各个命令的 CanExecuteChanged 事件，
        /// 以便在状态变化时及时刷新按钮的可用性。
        /// </summary>
        private void OnCommandsCanExecuteChanged()
        {
            (StartDrawCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopDrawCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowAllWinnersCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowWinnerWallCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 属性变化通知事件，实现 INotifyPropertyChanged 以支持 WPF 数据绑定。
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 触发指定属性的 PropertyChanged 事件，默认使用调用方成员名称。
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 单条中奖记录，包含奖项名称和中奖人姓名。
    /// </summary>
    public class WinnerItem
    {
        /// <summary>
        /// 中奖对应的奖项名称，如“特等奖”、“三等奖”。
        /// </summary>
        public string Prize { get; set; } = string.Empty;

        /// <summary>
        /// 中奖人姓名。
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}