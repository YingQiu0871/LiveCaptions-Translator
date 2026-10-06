using System.Diagnostics;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using ComboBox = System.Windows.Controls.ComboBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace LiveCaptionsTranslator
{
    // Dialogs for naming a recording and choosing its course.
    public static class LectureDialogs
    {
        // Asks for the course and the name. Returns null if cancelled.
        public static async Task<(string Course, string Name)?> AskName(string title, string course, string name,
            string primaryText, string closeText)
        {
            var host = (Application.Current.MainWindow as MainWindow)?.DialogHostContainer;
            if (host == null)
                return null;

            List<string> courses;
            try
            {
                courses = (await LectureStore.LoadCourses()).Select(c => c.Name).ToList();
            }
            catch (Exception)
            {
                courses = new List<string>();
            }

            var courseBox = new ComboBox
            {
                IsEditable = true,
                ItemsSource = courses,
                Text = course,
                MinWidth = 320,
            };
            var nameBox = new TextBox { Text = name, MinWidth = 320 };
            nameBox.Loaded += (s, e) =>
            {
                nameBox.Focus();
                nameBox.SelectAll();
            };
            var panel = new StackPanel { MinWidth = 340 };
            panel.Children.Add(new TextBlock { Text = "本节课名称（也用作历史里的标题和文件名）", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(nameBox);
            panel.Children.Add(new TextBlock { Text = "放进哪个课程文件夹（可直接输入新课程）", Margin = new Thickness(0, 12, 0, 4) });
            panel.Children.Add(courseBox);

            var dialog = new ContentDialog
            {
                Title = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Regular },
                Content = panel,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Primary,
                DialogHost = host,
                Padding = new Thickness(8, 4, 8, 8),
            };

            host.Visibility = Visibility.Visible;
            var result = await dialog.ShowAsync();
            host.Visibility = Visibility.Collapsed;
            if (result != ContentDialogResult.Primary)
                return null;

            string newName = nameBox.Text.Trim();
            return (LectureStore.CleanCourseName(courseBox.Text), newName.Length == 0 ? name : newName);
        }

        // After "停止": name the recording, then save it once its last section is summarized.
        public static async Task SaveAfterStop(long lectureId)
        {
            if (lectureId < 0)
                return;
            var finishing = ClassSession.FinishLecture(lectureId);

            try
            {
                var lecture = await LectureStore.GetLecture(lectureId);
                if (lecture != null)
                {
                    var answer = await AskName("保存本节课", lecture.CourseName, lecture.Name, "保存", "用默认名称");
                    if (answer is { } chosen)
                    {
                        await LectureStore.RenameLecture(lectureId, chosen.Name, chosen.Course);
                        Translator.Setting.Lecture.CurrentCourse = chosen.Course;
                    }
                }

                await finishing;
                lecture = await LectureStore.GetLecture(lectureId);
                if (lecture == null)
                    return;
                string path = await LectureDocument.Save(lecture);
                SnackbarHost.Show("本节课已保存。", path, SnackbarType.Success, timeout: 3);
                if (Translator.Setting.Lecture.NotesDraft)
                    await WriteNotesDraft(lecture);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 保存本节课失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
            }
        }

        public static async Task<string?> WriteNotesDraft(LectureRecord lecture)
        {
            SnackbarHost.Show("正在生成笔记初稿……", "整节课的内容较多，大约需要半分钟到一两分钟。", SnackbarType.Info, timeout: 3);
            try
            {
                string path = await LectureNotes.Generate(lecture);
                SnackbarHost.Show("笔记初稿已生成。", path, SnackbarType.Success, timeout: 3);
                return path;
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 笔记初稿生成失败。", ex.Message, SnackbarType.Error, timeout: 3, closeButton: true);
                return null;
            }
        }

        public static void ShowInFolder(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                {
                    string folder = Directory.Exists(path) ? path : LectureDocument.SaveFolder;
                    Directory.CreateDirectory(folder);
                    Process.Start("explorer.exe", $"\"{folder}\"");
                }
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 打不开文件夹。", ex.Message, SnackbarType.Error, timeout: 2);
            }
        }
    }
}
