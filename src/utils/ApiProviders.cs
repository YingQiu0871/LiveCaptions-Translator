using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator.utils
{
    // What the settings page of each platform knows about the LLM providers of card ① and the translation APIs.
    public static class ApiProviders
    {
        public record Provider(string Name, string ApiUrl, string[] Models);

        // Index matches the items of the provider box. The last one is "any OpenAI-compatible API".
        public static readonly Provider[] All =
        {
            new("DeepSeek", "https://api.deepseek.com/chat/completions",
                new[] { "deepseek-chat" }),
            new("Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
                new[] { "qwen-plus", "qwen-turbo", "qwen-max" }),
            new("Custom", string.Empty, Array.Empty<string>()),
        };

        // Display names for the translation APIs. DeepSeek / Qwen go through the OpenAI-compatible config of card ①.
        public static readonly Dictionary<string, string> ApiNames = new()
        {
            [Summarizer.SUMMARY_API] = "DeepSeek / 通义千问（用 ① 的设置）",
            ["Google"] = "Google 翻译（免费）",
            ["Google2"] = "Google 翻译 2（免费）",
        };

        // The provider the configured URL belongs to: the first one when nothing is configured yet, the custom
        // one when the URL is on none of their hosts.
        public static int IndexOf(string? apiUrl)
        {
            for (int i = 0; i < All.Length - 1; i++)
            {
                if (string.IsNullOrEmpty(apiUrl) ||
                    apiUrl.Contains(new Uri(All[i].ApiUrl).Host, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return All.Length - 1;
        }

        public static void Apply(Provider provider, OpenAIConfig config)
        {
            if (!string.IsNullOrEmpty(provider.ApiUrl))
                config.ApiUrl = provider.ApiUrl;
            if (provider.Models.Length > 0 && !provider.Models.Contains(config.ModelName))
                config.ModelName = provider.Models[0];
        }

        // Pasted keys sometimes carry spaces, line breaks or invisible characters.
        public static string CleanKey(string text)
        {
            return new string(text.Where(c => c > ' ' && c < 0x7F).ToArray());
        }

        // Shows that a key is stored without revealing it.
        public static string MaskKey(string key)
        {
            return $"{key[..Math.Min(3, key.Length)]}…{key[Math.Max(0, key.Length - 4)..]}（{key.Length} 位）";
        }
    }
}
