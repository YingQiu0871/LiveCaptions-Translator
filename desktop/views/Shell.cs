using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    // Small helpers the pages share: the main window, pickers, opening files and folders, the clipboard.
    public static class Shell
    {
        public static MainWindow? Main =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

        public static FilePickerFileType SlideFiles { get; } = new("课件 (PDF, PowerPoint)")
        {
            Patterns = new[] { "*.pdf", "*.PDF", "*.pptx", "*.PPTX" },
        };

        public static async Task<string?> PickFile(string title, params FilePickerFileType[] types)
        {
            var provider = Main?.StorageProvider;
            if (provider == null)
                return null;
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types.Length == 0 ? null : types,
            });
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }

        public static async Task<string?> PickFolder(string title, string? startFolder)
        {
            var provider = Main?.StorageProvider;
            if (provider == null)
                return null;
            IStorageFolder? start = null;
            if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
                start = await provider.TryGetFolderFromPathAsync(startFolder);
            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        }

        public static async Task<string?> PickSavePath(string title, string fileName, string extension,
            FilePickerFileType type)
        {
            var provider = Main?.StorageProvider;
            if (provider == null)
                return null;
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = fileName,
                DefaultExtension = extension,
                FileTypeChoices = new[] { type },
                ShowOverwritePrompt = true,
            });
            return file?.TryGetLocalPath();
        }

        public static async Task<bool> CopyText(string text)
        {
            var clipboard = Main?.Clipboard;
            if (clipboard == null)
                return false;
            // Another program (clipboard manager, remote desktop) may hold the clipboard for a moment.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await clipboard.SetTextAsync(text);
                    return true;
                }
                catch (Exception) when (attempt < 4)
                {
                    await Task.Delay(150);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // Opens a folder (or the folder of a file, selecting the file where the file manager can) in the file manager.
        public static void ShowInFolder(string path)
        {
            bool isFile = File.Exists(path);
            string folder = isFile ? Path.GetDirectoryName(path)! : path;
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(Exec.Create("open", isFile ? new[] { "-R", path } : new[] { folder }));
                return;
            }
            ShellUtil.Open(folder);
        }

        // Opens the sound page of the system settings; false if there is none we know of.
        public static bool OpenSoundSettings()
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(Exec.Create("open", new[] { "x-apple.systempreferences:com.apple.Sound-Settings.extension" }));
                return true;
            }
            foreach (var (tool, arguments) in new (string, string[])[]
                     {
                         ("pavucontrol", Array.Empty<string>()),
                         ("gnome-control-center", new[] { "sound" }),
                         ("kcmshell6", new[] { "kcm_pulseaudio" }),
                         ("kcmshell5", new[] { "kcm_pulseaudio" }),
                     })
            {
                if (!Exec.Exists(tool))
                    continue;
                Process.Start(Exec.Create(tool, arguments));
                return true;
            }
            return false;
        }

        public static Task OnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }
    }

    // Choices of this desktop version that the Windows settings file has no place for.
    public static class DesktopSettings
    {
        private static readonly object fileLock = new();

        private class Data
        {
            public string MicrophoneId { get; set; } = string.Empty;
        }

        private static string FilePath => Path.Combine(AppPaths.DataDirectory, "desktop.json");

        // The capture source chosen for the microphone; empty: the system default.
        public static string MicrophoneId
        {
            get => Read().MicrophoneId;
            set
            {
                var data = Read();
                data.MicrophoneId = value ?? string.Empty;
                lock (fileLock)
                {
                    try
                    {
                        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static Data Read()
        {
            lock (fileLock)
            {
                try
                {
                    if (File.Exists(FilePath))
                        return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
                }
                catch (Exception)
                {
                }
            }
            return new Data();
        }
    }

    public class ActionCommand : System.Windows.Input.ICommand
    {
        private readonly Action action;

        public ActionCommand(Action action)
        {
            this.action = action;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
