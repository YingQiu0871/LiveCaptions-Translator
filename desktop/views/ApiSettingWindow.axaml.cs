using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator
{
    // The configuration of the translation APIs other than the one in the settings page (per API several
    // configurations can be kept; the one shown is the one in use).
    public partial class ApiSettingWindow : Window
    {
        private enum Kind { Text, Number, Combo, ModelCombo }

        private record Field(string Label, string Property, Kind Kind = Kind.Text, string? Info = null,
            string[]? Options = null);

        private record Section(string Api, string Title, string Nav, Field[] Fields);

        private static readonly Section[] SECTIONS =
        {
            new("Ollama", "Ollama", "Ollama", new[]
            {
                new Field("模型名称", "ModelName"),
                new Field("温度", "Temperature", Kind.Number),
                new Field("接口地址", "ApiUrl", Info: "不需要手动加上`api/chat`后缀。"),
            }),
            new("LMStudio", "LMStudio", "LMStudio", new[]
            {
                new Field("模型名称", "ModelName", Kind.ModelCombo),
                new Field("温度", "Temperature", Kind.Number),
                new Field("接口地址（基础）", "ApiUrl",
                    Info: "填写以`api/v1`结尾的基础地址，对话和模型列表路径会自动补全。"),
            }),
            new("OpenAI", "DeepSeek / 通义千问 / OpenAI 兼容接口", "DeepSeek / OpenAI 兼容", new[]
            {
                new Field("模型名称", "ModelName"),
                new Field("温度", "Temperature", Kind.Number),
                new Field("接口地址", "ApiUrl",
                    Info: "请填完整地址（通常以`v1/chat/completions`结尾），不要只填基础地址（通常只以`v1`结尾）。"),
                new Field("API Key", "ApiKey"),
            }),
            new("OpenRouter", "OpenRouter", "OpenRouter", new[]
            {
                new Field("模型名称", "ModelName"),
                new Field("温度", "Temperature", Kind.Number),
                new Field("API Key", "ApiKey"),
            }),
            new("DeepL", "DeepL", "DeepL", new[]
            {
                new Field("接口地址", "ApiUrl"),
                new Field("API Key", "ApiKey"),
            }),
            new("Youdao", "Youdao", "Youdao", new[]
            {
                new Field("接口地址", "ApiUrl"),
                new Field("APP Key", "AppKey"),
                new Field("APP Secret", "AppSecret"),
            }),
            new("MTranServer", "MTranServer", "MTranServer", new[]
            {
                new Field("源语言", "SourceLanguage", Kind.Combo, Options: new[] { "en", "ja", "fr", "ko", "de" }),
                new Field("接口地址", "ApiUrl"),
                new Field("API Key", "ApiKey"),
            }),
            new("Baidu", "Baidu", "Baidu", new[]
            {
                new Field("接口地址", "ApiUrl"),
                new Field("APP ID", "AppId"),
                new Field("APP Secret", "AppSecret"),
            }),
            new("LibreTranslate", "LibreTranslate", "LibreTranslate", new[]
            {
                new Field("接口地址", "ApiUrl"),
                new Field("API Key", "ApiKey"),
            }),
        };

        private static ApiSettingWindow? instance;

        private readonly Dictionary<string, Control> sectionControls = new();
        private readonly Dictionary<string, TextBlock> indexLabels = new();
        private readonly Dictionary<string, TextBlock> messageLabels = new();
        private readonly Dictionary<string, List<Action>> refreshers = new();
        private Button? selectedNav;
        private bool filling = false;

        public static void ShowSingle()
        {
            if (instance != null)
            {
                instance.Activate();
                return;
            }
            instance = new ApiSettingWindow();
            instance.Closed += (s, e) => instance = null;
            instance.Show();
        }

        public ApiSettingWindow()
        {
            InitializeComponent();
            var promptButton = AddNav("提示词", "Prompt");
            Sections.Children.Add(BuildPromptSection());
            foreach (var section in SECTIONS)
            {
                if (TranslateAPI.NO_CONFIG_APIS.Contains(section.Api) || !Translator.Setting.Configs.ContainsKey(section.Api))
                    continue;
                AddNav(section.Nav, section.Api);
                Sections.Children.Add(BuildSection(section));
                SwitchConfig(section.Api, Translator.Setting.ConfigIndices[section.Api]);
            }
            SelectNav(promptButton);
        }

        private Button AddNav(string text, string tag)
        {
            var button = new Button
            {
                Content = text,
                Tag = tag,
                Margin = new Thickness(10, 5, 5, 0),
                Padding = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
            };
            button.Click += (s, e) =>
            {
                SelectNav(button);
                if (sectionControls.TryGetValue(tag, out var control))
                    control.BringIntoView();
            };
            NavPanel.Children.Add(button);
            return button;
        }

        private void SelectNav(Button button)
        {
            if (selectedNav != null)
                selectedNav.Background = Brushes.Transparent;
            button.Background = new SolidColorBrush(Color.Parse("#22808080"));
            selectedNav = button;
        }

        private static TextBlock Heading(string text) => new()
        {
            Text = text,
            Margin = new Thickness(10),
            FontSize = 20,
            FontWeight = FontWeight.Medium,
        };

        private static Border Card(Control content) => new()
        {
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#33808080")),
            Child = content,
        };

        private Control BuildPromptSection()
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Text = Translator.Setting.Prompt,
            };
            box.TextChanged += (s, e) => Translator.Setting.Prompt = box.Text ?? string.Empty;
            var panel = new StackPanel
            {
                Margin = new Thickness(15, 0),
                Children =
                {
                    new TextBlock
                    {
                        Margin = new Thickness(2.5, 0, 0, 5),
                        TextWrapping = TextWrapping.Wrap,
                        Inlines = new Avalonia.Controls.Documents.InlineCollection
                        {
                            new Avalonia.Controls.Documents.Run("注意 1：") { FontWeight = FontWeight.Bold },
                            new Avalonia.Controls.Documents.Run("提示词里的 {0} 代表目标语言，请确保提示词中包含 {0}。"),
                            new Avalonia.Controls.Documents.Run("\n注意 2：") { FontWeight = FontWeight.Bold },
                            new Avalonia.Controls.Documents.Run("原文用 🔤 包裹。"),
                        },
                    },
                    box,
                },
            };
            var section = new StackPanel { Margin = new Thickness(20), Children = { Heading("提示词"), Card(panel) } };
            sectionControls["Prompt"] = section;
            return section;
        }

        private Control BuildSection(Section spec)
        {
            string api = spec.Api;
            var indexLabel = new TextBlock { Text = "1/1", VerticalAlignment = VerticalAlignment.Center };
            indexLabels[api] = indexLabel;
            var message = new TextBlock { Foreground = Brushes.OrangeRed, VerticalAlignment = VerticalAlignment.Center };
            messageLabels[api] = message;

            var prior = new Button { Content = "‹", Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 10, 0) };
            prior.Click += (s, e) => SwitchConfig(api, Translator.Setting.ConfigIndices[api] - 1);
            var next = new Button { Content = "›", Padding = new Thickness(8, 4), Margin = new Thickness(10, 0, 0, 0) };
            next.Click += (s, e) => SwitchConfig(api, Translator.Setting.ConfigIndices[api] + 1);
            var create = new Button { Content = "新建", Margin = new Thickness(0, 0, 10, 0) };
            create.Click += (s, e) => NewConfig(api);
            var delete = new Button { Content = "删除" };
            delete.Click += (s, e) => DeleteConfig(api);

            var header = new Grid { Margin = new Thickness(15, 0, 15, 10), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "当前配置：", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                    },
                    prior, indexLabel, next,
                    new TextBlock { Text = "", Width = 12 }, message,
                },
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { create, delete } };
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);

            var fields = new WrapPanel { Margin = new Thickness(15, 0, 0, 0) };
            refreshers[api] = new List<Action>();
            foreach (var field in spec.Fields)
                fields.Children.Add(BuildField(api, field));

            var section = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    Heading(spec.Title),
                    Card(new StackPanel { Children = { header, new Separator { Margin = new Thickness(0, 0, 0, 10) }, fields } }),
                },
            };
            sectionControls[api] = section;
            return section;
        }

        private Control BuildField(string api, Field field)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(2.5, 0, 0, 5) });
            if (field.Info != null)
            {
                var info = new TextBlock { Text = " ⓘ", Opacity = 0.7 };
                ToolTip.SetTip(info, field.Info);
                label.Children.Add(info);
            }

            Control editor;
            switch (field.Kind)
            {
                case Kind.Number:
                    {
                        var box = new NumericUpDown
                        {
                            Width = 130,
                            Minimum = 0,
                            Maximum = 2,
                            Increment = 0.1m,
                            FormatString = "0.0#",
                        };
                        box.ValueChanged += (s, e) =>
                        {
                            if (!filling && box.Value is decimal value)
                                SetProperty(api, field.Property, (double)value);
                        };
                        refreshers[api].Add(() => box.Value = Convert.ToDecimal(GetProperty(api, field.Property) ?? 1.0));
                        editor = box;
                        break;
                    }
                case Kind.Combo:
                case Kind.ModelCombo:
                    {
                        var box = new EditableCombo { Width = field.Kind == Kind.Combo ? 130 : 250 };
                        if (field.Options != null)
                            box.ItemsSource = field.Options;
                        box.TextEdited += text =>
                        {
                            if (!filling)
                                SetProperty(api, field.Property, text);
                        };
                        refreshers[api].Add(() => box.Text = GetProperty(api, field.Property) as string ?? string.Empty);
                        editor = box;
                        if (field.Kind == Kind.ModelCombo)
                        {
                            var load = new Button { Content = "加载模型", Margin = new Thickness(10, 0, 0, 0) };
                            var result = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                            load.Click += async (s, e) => await LoadModels(api, box, load, result);
                            editor = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, load, result } };
                        }
                        break;
                    }
                default:
                    {
                        var box = new TextBox { Width = field.Property == "ModelName" ? 160 : 200 };
                        box.TextChanged += (s, e) =>
                        {
                            if (!filling)
                                SetProperty(api, field.Property, box.Text ?? string.Empty);
                        };
                        refreshers[api].Add(() => box.Text = GetProperty(api, field.Property) as string ?? string.Empty);
                        editor = box;
                        break;
                    }
            }

            return new StackPanel
            {
                Margin = new Thickness(0, 0, 15, 10),
                Children = { label, editor },
            };
        }

        private async Task LoadModels(string api, EditableCombo box, Button button, TextBlock result)
        {
            string baseUrl = (Translator.Setting[api] as LMStudioConfig)?.ApiUrl ?? "";
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                result.Text = "请先填写接口地址。";
                return;
            }

            button.IsEnabled = false;
            try
            {
                var models = await ModelsApiService.FetchModelsAsync(api, baseUrl);
                box.ItemsSource = models.Select(model => model.Id).ToList();
                result.Text = models.Count > 0 ? $"已加载 {models.Count} 个模型。" : "没有找到模型或无法连接，请确认服务已启动。";
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private static PropertyInfo? Property(string api, string name) => Translator.Setting[api].GetType().GetProperty(name);

        private static object? GetProperty(string api, string name) => Property(api, name)?.GetValue(Translator.Setting[api]);

        private static void SetProperty(string api, string name, object value)
        {
            var property = Property(api, name);
            if (property == null)
                return;
            var config = Translator.Setting[api];
            if (!Equals(property.GetValue(config), value))
                property.SetValue(config, value);
        }

        private void NewConfig(string api)
        {
            var configs = Translator.Setting.Configs[api];
            int configIndex = Translator.Setting.ConfigIndices[api];

            var type = Type.GetType($"LiveCaptionsTranslator.models.{api}Config");
            if (type == null || Activator.CreateInstance(type) is not TranslateAPIConfig config)
                return;
            configs.Insert(configIndex + 1, config);
            SwitchConfig(api, configIndex + 1);

            Translator.Setting.OnPropertyChanged("Configs");
        }

        private void DeleteConfig(string api)
        {
            var configs = Translator.Setting.Configs[api];
            int configIndex = Translator.Setting.ConfigIndices[api];

            if (configs.Count <= 1)
            {
                messageLabels[api].Text = "至少要保留一个配置。";
                return;
            }
            configs.RemoveAt(configIndex);
            SwitchConfig(api, Math.Max(0, Math.Min(configs.Count - 1, configIndex)));

            Translator.Setting.OnPropertyChanged("Configs");
        }

        private void SwitchConfig(string api, int index)
        {
            if (index < 0 || index >= Translator.Setting.Configs[api].Count)
                return;

            if (Translator.Setting.ConfigIndices[api] != index)
                Translator.Setting.ConfigIndices[api] = index;

            if (indexLabels.TryGetValue(api, out var indexLabel))
                indexLabel.Text = $"{index + 1}/{Translator.Setting.Configs[api].Count}";
            if (messageLabels.TryGetValue(api, out var message))
                message.Text = string.Empty;

            filling = true;
            try
            {
                foreach (var refresh in refreshers.GetValueOrDefault(api) ?? new List<Action>())
                    refresh();
            }
            finally
            {
                filling = false;
            }
            Translator.Setting.OnPropertyChanged(null);
        }
    }
}
