using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Dialogs for naming a recording and choosing its course; the save flow itself is in LectureSaving.
    public static class LectureDialogs
    {
        // Shows `content` with a primary and a close button. True if the primary one was chosen.
        public static async Task<bool> Ask(string title, Control content, string primary, string close = "取消",
            Control? focus = null)
        {
            var owner = Shell.Main;
            bool result = false;
            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                MinWidth = 380,
            };

            var primaryButton = new Button { Content = primary, IsDefault = true, MinWidth = 80 };
            primaryButton.Classes.Add("accent");
            primaryButton.Click += (s, e) =>
            {
                result = true;
                window.Close();
            };
            var closeButton = new Button { Content = close, IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            closeButton.Click += (s, e) => window.Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { primaryButton, closeButton },
            };
            window.Content = new StackPanel
            {
                Margin = new Thickness(20),
                MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, Margin = new Thickness(0, 0, 0, 12) },
                    content,
                    buttons,
                },
            };
            window.Opened += (s, e) =>
            {
                (focus ?? primaryButton).Focus();
                if (focus is TextBox box)
                    box.SelectAll();
            };

            if (owner != null)
                await window.ShowDialog(owner);
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (s, e) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
            return result;
        }

        public static Task<bool> Confirm(string title, string message, string primary)
        {
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            return Ask(title, text, primary);
        }

        public static async Task<string?> AskText(string title, string label, string text)
        {
            var box = new TextBox { Text = text, MinWidth = 320 };
            var panel = new StackPanel
            {
                Children = { new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }, box },
            };
            return await Ask(title, panel, "确定", focus: box) ? box.Text?.Trim() ?? string.Empty : null;
        }

        // Asks for the course and the name. Returns null if cancelled.
        public static async Task<(string Course, string Name)?> AskName(string title, string course, string name,
            string primaryText, string closeText)
        {
            List<string> courses;
            try
            {
                courses = (await LectureStore.LoadCourses()).Select(c => c.Name).ToList();
            }
            catch (Exception)
            {
                courses = new List<string>();
            }

            var courseBox = new EditableCombo { ItemsSource = courses, Text = course, MinWidth = 320 };
            var nameBox = new TextBox { Text = name, MinWidth = 320 };
            var panel = new StackPanel
            {
                MinWidth = 340,
                Children =
                {
                    new TextBlock { Text = "本节课名称（也用作历史里的标题和文件名）", Margin = new Thickness(0, 0, 0, 4) },
                    nameBox,
                    new TextBlock { Text = "放进哪个课程文件夹（可直接输入新课程）", Margin = new Thickness(0, 12, 0, 4) },
                    courseBox,
                },
            };

            if (!await Ask(title, panel, primaryText, closeText, nameBox))
                return null;

            string newName = nameBox.Text?.Trim() ?? string.Empty;
            return (LectureStore.CleanCourseName(courseBox.Text), newName.Length == 0 ? name : newName);
        }

        public static void ShowInFolder(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    string folder = Directory.Exists(path) ? path : LectureDocument.SaveFolder;
                    Directory.CreateDirectory(folder);
                    path = folder;
                }
                Shell.ShowInFolder(path);
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 打不开文件夹。", ex.Message, SnackbarType.Error, timeout: 2);
            }
        }
    }
}
