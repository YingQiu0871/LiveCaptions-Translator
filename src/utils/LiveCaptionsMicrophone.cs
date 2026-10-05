using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LiveCaptionsTranslator.utils
{
    // Turns "Include microphone audio" of Windows LiveCaptions on or off by driving its settings menu
    // with UI Automation, since LiveCaptions offers no API for it.
    public static class LiveCaptionsMicrophone
    {
        private static readonly string[] MICROPHONE_KEYWORDS =
        {
            "microphone", "麦克风", "麥克風", "マイク", "마이크", "mikrofon", "micrófono", "microfone",
            "microfono", "микрофон"
        };

        private const byte VK_ESCAPE = 0x1B;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // Returns true if the option is now in the wanted state, false if the menu item was not found.
        public static bool Set(AutomationElement window, bool enabled)
        {
            int processId = window.Current.ProcessId;
            LiveCaptionsHandler.RestoreLiveCaptions(window);
            Thread.Sleep(400);
            try
            {
                if (!LiveCaptionsHandler.ClickSettingsButton(window))
                    return false;

                var deadline = DateTime.Now.AddSeconds(4);
                var expanded = new HashSet<string>();
                while (DateTime.Now < deadline)
                {
                    Thread.Sleep(300);
                    var items = FindMenuItems(processId);

                    var microphoneItem = items.FirstOrDefault(IsMicrophoneItem);
                    if (microphoneItem != null)
                    {
                        Toggle(microphoneItem, enabled);
                        return true;
                    }

                    // Open submenus (e.g. "Preferences") to reach the option.
                    foreach (var item in items)
                    {
                        string name = item.Current.Name;
                        if (expanded.Contains(name) ||
                            !item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
                            continue;
                        expanded.Add(name);
                        try
                        {
                            ((ExpandCollapsePattern)pattern).Expand();
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                return false;
            }
            finally
            {
                CloseMenus();
                LiveCaptionsHandler.HideLiveCaptions(window);
            }
        }

        // Shows LiveCaptions with its settings menu open, for the user to change the option by hand.
        public static void ShowSettings(AutomationElement window)
        {
            LiveCaptionsHandler.RestoreLiveCaptions(window);
            Thread.Sleep(400);
            LiveCaptionsHandler.ClickSettingsButton(window);
        }

        private static List<AutomationElement> FindMenuItems(int processId)
        {
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            try
            {
                return AutomationElement.RootElement
                    .FindAll(TreeScope.Descendants, condition)
                    .Cast<AutomationElement>()
                    .ToList();
            }
            catch (Exception)
            {
                return new List<AutomationElement>();
            }
        }

        private static bool IsMicrophoneItem(AutomationElement item)
        {
            try
            {
                string name = item.Current.Name.ToLowerInvariant();
                return MICROPHONE_KEYWORDS.Any(keyword => name.Contains(keyword));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Toggle(AutomationElement item, bool enabled)
        {
            if (item.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
            {
                var toggle = (TogglePattern)pattern;
                if ((toggle.Current.ToggleState == ToggleState.On) != enabled)
                    toggle.Toggle();
            }
            else if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                ((InvokePattern)invoke).Invoke();
        }

        private static void CloseMenus()
        {
            for (int i = 0; i < 3; i++)
            {
                keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(80);
            }
        }
    }
}
