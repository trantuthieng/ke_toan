using Anthropic;
using Anthropic.Models.Messages;
using System.Text;
using System.Text.Json;

namespace QuanLyNo.Services;

public class ClaudeImageParser
{
    private readonly IConfiguration _configuration;

    public ClaudeImageParser(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<ImageParseResult> ParseAsync(string imagePath, string mimeType, string importType,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Claude:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            return new ImageParseResult
            {
                IsConfigured = false,
                Error = "Claude chưa được cấu hình. Đặt Claude:ApiKey hoặc ANTHROPIC_API_KEY để kích hoạt đọc ảnh.",
                Rows = []
            };

        var model = _configuration["Claude:Model"];
        if (string.IsNullOrWhiteSpace(model))
            model = Environment.GetEnvironmentVariable("CLAUDE_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "claude-opus-4-8";

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var imageBase64 = Convert.ToBase64String(bytes);
            var prompt = BuildPrompt(importType);

            var client = new AnthropicClient { ApiKey = apiKey };

            var parameters = new MessageCreateParams
            {
                Model = model,
                MaxTokens = 8000,
            
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = new List<ContentBlockParam>
                        {
                            new ImageBlockParam
                            {
                                Source = new Base64ImageSource
                                {
                                    MediaType = GetMediaType(mimeType),
                                    Data = imageBase64
                                }
                            },
                            new TextBlockParam { Text = prompt }
                        }
                    }
                ]
            };

            var jsonBuilder = new StringBuilder();
            await foreach (var streamEvent in client.Messages.CreateStreaming(parameters))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                    delta.Delta.TryPickText(out var textDelta))
                {
                    jsonBuilder.Append(textDelta.Text);
                }
            }

            var jsonText = jsonBuilder.ToString();
            if (string.IsNullOrWhiteSpace(jsonText))
                return ImageParseResult.Failed("Claude không trả về kết quả văn bản.");

            var parsed = JsonSerializer.Deserialize<ImageParseResult>(CleanJson(jsonText),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
                return ImageParseResult.Failed("Không thể phân tích JSON từ Claude.");

            parsed.RawText ??= jsonText;
            parsed.Rows ??= [];
            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImageParseResult.Failed(ex.Message);
        }
    }

    private static MediaType GetMediaType(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/png" => MediaType.ImagePng,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImageJpeg
        };

    private static string BuildPrompt(string importType) =>
        GeminiImageParser.BuildPrompt(importType);

    private static string CleanJson(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine >= 0 && lastFence > firstNewLine)
            return trimmed[(firstNewLine + 1)..lastFence].Trim();

        return trimmed.Trim('`').Trim();
    }
}
