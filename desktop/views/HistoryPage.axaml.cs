using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class HistoryPage : UserControl
    {
        public const int MIN_HEIGHT = 420;

        private enum View { Courses, Lectures, Detail, All }

        private View view = View.Courses;
        private CourseEntry? currentCourse = null;
        private LectureRecord? currentLecture = null;

        private int currentPage = 1;
        private int searchPage = 1;
        private int maxPage = 1;
        private int maxRowPerPage = 30;

        private string SearchText { get; set; } = string.Empty;

        public HistoryPage()
        {
            InitializeComponent();
            HistoryMaxRow.SelectionChanged += MaxRow_SelectionChanged;
            HistorySearchBox.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter)
                    await SubmitSearch();
            };
            HistorySearchBox.TextChanged += async (s, e) =>
            {
                // Clearing the box clears the search.
                if (string.IsNullOrEmpty(HistorySearchBox.Text) && !string.IsNullOrEmpty(SearchText))
                    await SubmitSearch();
            };
        }

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Shell.Main?.AutoHeightAdjust(minHeight: MIN_HEIGHT);
            Translator.TranslationLogged += OnTranslationLogged;
            await ShowCourses();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            HistoryList.ItemsSource = null;
            Translator.TranslationLogged -= OnTranslationLogged;
        }

        private void OnTranslationLogged()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (view == View.All)
                    await LoadHistory();
            });
        }

        // ---- Navigation: courses > recordings of a course > one recording ----

        private void SetView(View newView)
        {
            view = newView;
            CourseScroll.IsVisible = view == View.Courses;
            LectureScroll.IsVisible = view == View.Lectures;
            DetailScroll.IsVisible = view == View.Detail;
            AllView.IsVisible = view == View.All;

            CrumbSep1.IsVisible = view != View.Courses;
            CrumbCourse.IsVisible = CrumbSep1.IsVisible;
            CrumbCourse.Content = view == View.All ? "全部句子" : currentCourse?.Name ?? string.Empty;
            CrumbSep2.IsVisible = view == View.Detail;
            CrumbLecture.IsVisible = CrumbSep2.IsVisible;
            CrumbLecture.Text = currentLecture?.Name ?? string.Empty;

            NewCourse.IsVisible = view == View.Courses;
            AllSentences.IsVisible = NewCourse.IsVisible;
            RenameLecture.IsVisible = view == View.Detail;
            OpenFile.IsVisible = RenameLecture.IsVisible;
            NotesDraft.IsVisible = RenameLecture.IsVisible;
            Export.IsVisible = view == View.All;
            Delete.IsVisible = Export.IsVisible;
        }

        private async Task ShowCourses()
        {
            currentCourse = null;
            currentLecture = null;
            SetView(View.Courses);
            try
            {
                var courses = await LectureStore.LoadCourses();
                CourseList.Children.Clear();
                foreach (var course in courses)
                    CourseList.Children.Add(BuildCourseCard(course));
                CourseEmptyHint.IsVisible = courses.Count == 0;
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 读取课程失败。", ex.Message, SnackbarType.Error, timeout: 2, closeButton: true);
            }
        }

        private async Task ShowLectures(CourseEntry course)
        {
            currentCourse = course;
            currentLecture = null;
            SetView(View.Lectures);
            try
            {
                var lectures = await LectureStore.LoadLectures(course.Id);
                LectureList.Children.Clear();
                foreach (var lecture in lectures)
                    LectureList.Children.Add(BuildLectureCard(lecture));
                LectureEmptyHint.IsVisible = lectures.Count == 0;
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 读取录音失败。", ex.Message, SnackbarType.Error, timeout: 2, closeButton: true);
            }
        }

        private async Task ShowDetail(LectureRecord lecture)
        {
            currentLecture = lecture;
            SetView(View.Detail);
            DetailList.ItemsSource = null;
            DetailInfo.Text = $"{lecture.CourseName} · {lecture.Info}";
            try
            {
                var sections = await LectureDocument.Build(lecture);
                foreach (var line in sections.SelectMany(section => section.Lines))
                    line.TranslatedText = LectureDocument.CleanTranslation(line.TranslatedText);
                DetailList.ItemsSource = sections;
                DetailEmptyHint.IsVisible = sections.Count == 0;
                DetailScroll.ScrollToHome();
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 读取本节课失败。", ex.Message, SnackbarType.Error, timeout: 2, closeButton: true);
            }
        }

        // ---- Cards of the two folder levels (built here because they carry a context menu) ----

        private Border BuildCard(string title, string info, string? tooltip, double width)
        {
            var text = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = info, Margin = new Thickness(0, 2, 0, 0), FontSize = 12, Opacity = 0.7 },
                },
            };
            if (tooltip != null)
                ToolTip.SetTip(text.Children[0], tooltip);
            var card = new Border
            {
                Padding = new Thickness(12, 8, 12, 10),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = (IBrush?)this.FindResource("SystemControlBackgroundListLowBrush"),
                BorderBrush = (IBrush?)this.FindResource("SystemControlForegroundBaseLowBrush"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text } },
            };
            if (width > 0)
                card.Width = width;
            return card;
        }

        private Border BuildCourseCard(CourseEntry course)
        {
            var card = BuildCard(course.Name, course.Info, course.Name, 200);
            card.Margin = new Thickness(0, 0, 10, 10);
            ((StackPanel)card.Child!).Children.Insert(0, new Border
            {
                // Drawn, not a "▣" character: not every Linux font has that glyph.
                Width = 24,
                Height = 24,
                Margin = new Thickness(2, 0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = (IBrush?)this.FindResource("SystemControlHighlightAccentBrush") ?? Brushes.SteelBlue,
            });
            ((StackPanel)((StackPanel)card.Child).Children[1]).Width = 140;
            AddCardActions(card, () => ShowLectures(course),
                "重命名", () => RenameCourse(course), "删除（仅空文件夹）", () => DeleteCourse(course));
            return card;
        }

        private Border BuildLectureCard(LectureRecord lecture)
        {
            var card = BuildCard(lecture.Name, lecture.Info, null, 0);
            card.Margin = new Thickness(0, 0, 10, 8);
            AddCardActions(card, () => ShowDetail(lecture),
                "重命名 / 移到其他课程", async () => await RenameLectureAndRefresh(lecture),
                "删除这节课", () => DeleteLecture(lecture));
            return card;
        }

        // A left click opens the card; the context menu has a rename and a delete entry.
        private static void AddCardActions(Border card, Func<Task> open, string renameHeader, Action rename,
                                           string deleteHeader, Action delete)
        {
            card.PointerReleased += async (s, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                    await open();
            };
            var renameItem = new MenuItem { Header = renameHeader };
            renameItem.Click += (s, e) => rename();
            var deleteItem = new MenuItem { Header = deleteHeader };
            deleteItem.Click += (s, e) => delete();
            card.ContextMenu = new ContextMenu { Items = { renameItem, deleteItem } };
        }

        private async void CrumbHome_Click(object? sender, RoutedEventArgs e) => await ShowCourses();

        private async void CrumbCourse_Click(object? sender, RoutedEventArgs e)
        {
            if (view == View.Detail && currentCourse != null)
                await ShowLectures(currentCourse);
        }

        private async void AllSentences_Click(object? sender, RoutedEventArgs e)
        {
            SetView(View.All);
            await LoadHistory();
        }

        private async void NewCourse_Click(object? sender, RoutedEventArgs e)
        {
            string? name = await LectureDialogs.AskText("新建课程", "课程名称", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            await LectureStore.AddCourse(name);
            await ShowCourses();
        }

        private async void RenameCourse(CourseEntry course)
        {
            string? name = await LectureDialogs.AskText("重命名课程", "课程名称", course.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == course.Name)
                return;
            try
            {
                await LectureStore.RenameCourse(course.Id, name);
            }
            catch (Exception)
            {
                SnackbarHost.Show("已经有同名的课程了。", "", SnackbarType.Warning, timeout: 2);
            }
            if (Translator.Setting.Lecture.CurrentCourse == course.Name)
                Translator.Setting.Lecture.CurrentCourse = name.Trim();
            await ShowCourses();
        }

        private async void DeleteCourse(CourseEntry course)
        {
            if (!await LectureStore.DeleteCourse(course.Id))
                SnackbarHost.Show("文件夹里还有录音。", "请先进入文件夹删除或移走里面的课。", SnackbarType.Warning, timeout: 2);
            await ShowCourses();
        }

        private async Task RenameLectureAndRefresh(LectureRecord lecture)
        {
            var answer = await LectureDialogs.AskName("重命名", lecture.CourseName, lecture.Name, "确定", "取消");
            if (answer is not { } chosen)
                return;
            try
            {
                await LectureStore.RenameLecture(lecture.Id, chosen.Name, chosen.Course);
                var updated = await LectureStore.GetLecture(lecture.Id);
                if (updated != null && !string.IsNullOrEmpty(updated.FilePath))
                    await LectureDocument.Save(updated);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 重命名失败。", ex.Message, SnackbarType.Error, timeout: 2);
            }

            var courses = await LectureStore.LoadCourses();
            currentCourse = courses.FirstOrDefault(c => c.Name == chosen.Course) ?? currentCourse;
            if (view == View.Detail)
            {
                var updated = await LectureStore.GetLecture(lecture.Id);
                if (updated != null)
                {
                    updated.SentenceCount = lecture.SentenceCount;
                    await ShowDetail(updated);
                }
            }
            else if (currentCourse != null)
                await ShowLectures(currentCourse);
        }

        private async void RenameLecture_Click(object? sender, RoutedEventArgs e)
        {
            if (currentLecture != null)
                await RenameLectureAndRefresh(currentLecture);
        }

        private async void DeleteLecture(LectureRecord lecture)
        {
            if (ClassSession.IsRunning && ClassSession.CurrentLectureId == lecture.Id)
            {
                SnackbarHost.Show("这节课正在录制。", "请先点“停止”。", SnackbarType.Warning, timeout: 2);
                return;
            }
            if (!await LectureDialogs.Confirm($"要删除“{lecture.Name}”吗？",
                    "这节课的原文、译文和小节总结都会删除，已保存的文件不受影响。此操作无法撤销！", "删除"))
                return;
            try
            {
                await LectureStore.DeleteLecture(lecture);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 删除失败。", ex.Message, SnackbarType.Error, timeout: 2);
            }
            if (currentCourse != null)
                await ShowLectures(currentCourse);
        }

        private async void OpenFile_Click(object? sender, RoutedEventArgs e)
        {
            if (currentLecture == null)
                return;
            try
            {
                var lecture = await LectureStore.GetLecture(currentLecture.Id) ?? currentLecture;
                string path = await LectureDocument.Save(lecture);
                currentLecture.FilePath = path;
                LectureDialogs.ShowInFolder(path);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 保存文件失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
            }
        }

        private async void NotesDraft_Click(object? sender, RoutedEventArgs e)
        {
            if (currentLecture == null)
                return;
            NotesDraft.IsEnabled = false;
            try
            {
                var lecture = await LectureStore.GetLecture(currentLecture.Id) ?? currentLecture;
                // The draft goes next to the transcript file, so make sure that exists.
                if (string.IsNullOrEmpty(lecture.FilePath) || !File.Exists(lecture.FilePath))
                    await LectureDocument.Save(lecture);
                string? path = await LectureSaving.WriteNotesDraft(lecture);
                if (path != null)
                    LectureDialogs.ShowInFolder(path);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 笔记初稿生成失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
            }
            finally
            {
                NotesDraft.IsEnabled = true;
            }
        }

        private async void PageDown_Click(object? sender, RoutedEventArgs e)
        {
            if (currentPage - 1 >= 1)
                currentPage--;
            await LoadHistory();
        }

        private async void PageUp_Click(object? sender, RoutedEventArgs e)
        {
            if (currentPage < maxPage)
                currentPage++;
            await LoadHistory();
        }

        private async void Delete_Click(object? sender, RoutedEventArgs e)
        {
            if (await LectureDialogs.Confirm("要删除全部历史记录吗？",
                    "所有课程里的录音、时间线上的小节总结都会一起删除（课程文件夹和已保存的文件保留），此操作无法撤销！", "删除"))
            {
                currentPage = 1;
                await SQLiteHistoryLogger.ClearHistory();
                // Sections point at history ids, which restart after clearing.
                await SectionLogger.ClearSections();
                await LectureStore.ClearLectures();
                Summarizer.ResetCursor();
                await LoadHistory();
            }
        }

        private async void MaxRow_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (HistoryMaxRow.SelectedItem is not ComboBoxItem { Tag: string tag })
                return;
            maxRowPerPage = Convert.ToInt32(tag);

            await LoadHistory();

            if (currentPage > maxPage)
            {
                currentPage = maxPage;
                await LoadHistory();
            }
        }

        private async void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            switch (view)
            {
                case View.Courses:
                    await ShowCourses();
                    break;
                case View.Lectures when currentCourse != null:
                    await ShowLectures(currentCourse);
                    break;
                case View.Detail when currentLecture != null:
                    await ShowDetail(currentLecture);
                    break;
                default:
                    await LoadHistory();
                    break;
            }
        }

        private async void Export_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await Shell.PickSavePath("导出全部句子", $"exported_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv", "csv",
                new FilePickerFileType("CSV 表格（*.csv）") { Patterns = new[] { "*.csv" } });
            if (path == null)
                return;
            try
            {
                await SQLiteHistoryLogger.ExportToCSV(path);
                SnackbarHost.Show("保存成功。", $"文件已保存到：{path}", SnackbarType.Success);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("保存失败。", $"文件保存失败：{ex.Message}", SnackbarType.Error);
            }
        }

        private async Task SubmitSearch()
        {
            string searchText = HistorySearchBox.Text ?? "";

            // Clear search by emptying the box
            if (string.IsNullOrEmpty(searchText))
            {
                SearchText = string.Empty;
                currentPage = searchPage;
            }
            else // Submit search
            {
                if (string.IsNullOrEmpty(SearchText))
                    searchPage = currentPage;
                SearchText = searchText;
                currentPage = 1;
            }
            await LoadHistory();
        }

        private async Task LoadHistory()
        {
            var data = await SQLiteHistoryLogger.LoadHistoryAsync(currentPage, maxRowPerPage, SearchText);
            List<TranslationHistoryEntry> history = data.Item1;

            maxPage = (data.Item2 > 0) ? data.Item2 : 1;

            await Shell.OnUi(() =>
            {
                HistoryList.ItemsSource = history;
                PageNumber.Text = currentPage.ToString() + "/" + maxPage.ToString();
            });
        }
    }
}
