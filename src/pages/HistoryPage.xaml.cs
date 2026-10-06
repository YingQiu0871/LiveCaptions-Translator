using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LiveCaptionsTranslator
{
    public partial class HistoryPage : Page
    {
        public const int MIN_HEIGHT = 300;

        private int currentPage = 1;
        private int searchPage = 1;
        private int maxPage = 1;
        private int maxRowPerPage = 30;

        public string SearchText { get; set; } = string.Empty;

        public HistoryPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            Loaded += async (s, e) =>
            {
                await LoadHistory();
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
                Translator.TranslationLogged += OnTranslationLogged;
            };
            Unloaded += (s, e) =>
            {
                HistoryDataGrid.ItemsSource = null;
                Translator.TranslationLogged -= OnTranslationLogged;
            };

            HistoryMaxRow.SelectionChanged += maxRow_SelectionChanged;
        }

        private async void OnTranslationLogged()
        {
            await LoadHistory();
        }

        private async void PageDown_click(object sender, RoutedEventArgs e)
        {
            if (currentPage - 1 >= 1)
                currentPage--;
            await LoadHistory();
        }

        private async void PageUp_click(object sender, RoutedEventArgs e)
        {
            if (currentPage < maxPage)
                currentPage++;
            await LoadHistory();
        }

        private async void Delete_click(object sender, RoutedEventArgs e)
        {
            var dialogHostContainer = (Application.Current.MainWindow as MainWindow)?.DialogHostContainer;

            var dialog = new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = "要删除全部历史记录吗？",
                    FontSize = 18,
                    FontWeight = FontWeights.Regular
                },
                Content = "时间线上的小节总结也会一起删除，此操作无法撤销！",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                DialogHost = dialogHostContainer,
                Padding = new Thickness(8, 4, 8, 8),
            };

            dialogHostContainer.Visibility = Visibility.Visible;
            var result = await dialog.ShowAsync();
            dialogHostContainer.Visibility = Visibility.Collapsed;

            if (result == ContentDialogResult.Primary)
            {
                currentPage = 1;
                await SQLiteHistoryLogger.ClearHistory();
                // Sections point at history ids, which restart after clearing.
                await SectionLogger.ClearSections();
                Summarizer.ResetCursor();
                await LoadHistory();
            }
        }

        private async void maxRow_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string tag = (e.AddedItems[0] as ComboBoxItem).Tag as string;
            maxRowPerPage = Convert.ToInt32(tag);

            await LoadHistory();

            if (currentPage > maxPage)
            {
                currentPage = maxPage;
                await LoadHistory();
            }
        }

        private async void Refresh_click(object sender, RoutedEventArgs e)
        {
            await LoadHistory();
        }

        private async void Export_click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV 表格 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"exported_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                RestoreDirectory = true,
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    await SQLiteHistoryLogger.ExportToCSV(saveFileDialog.FileName);
                    SnackbarHost.Show("保存成功。", $"文件已保存到：{saveFileDialog.FileName}", SnackbarType.Success);
                }
                catch (Exception ex)
                {
                    SnackbarHost.Show("保存失败。", $"文件保存失败：{ex.Message}", SnackbarType.Error);
                }
            }
        }

        private async void HistorySearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string searchText = (sender as AutoSuggestBox)?.Text ?? "";

            // Clear search by Ctrl+A and Delete and Enter
            if (string.IsNullOrEmpty(searchText))
            {
                SearchText = string.Empty;
                currentPage = searchPage;
            }
            else // Submit search
            {
                if (string.IsNullOrEmpty(SearchText))
                {
                    searchPage = currentPage;
                }
                SearchText = (sender as AutoSuggestBox)?.Text;
                currentPage = 1;
            }
            await LoadHistory();
        }

        private async void HistorySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Press X to clear search box
            if (args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
            {
                if (!string.IsNullOrEmpty(SearchText))
                {
                    SearchText = string.Empty;
                    currentPage = searchPage;
                    await LoadHistory();
                }
            }
        }

        public async Task LoadHistory()
        {
            var data = await SQLiteHistoryLogger.LoadHistoryAsync(currentPage, maxRowPerPage, SearchText);
            List<TranslationHistoryEntry> history = data.Item1;

            maxPage = (data.Item2 > 0) ? data.Item2 : 1;

            await Dispatcher.InvokeAsync(() =>
            {
                HistoryDataGrid.ItemsSource = history;
                PageNumber.Text = currentPage.ToString() + "/" + maxPage.ToString();
            });
        }
    }
}