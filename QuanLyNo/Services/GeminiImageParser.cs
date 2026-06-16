using System.Net.Http.Json;
using System.Text.Json;

namespace QuanLyNo.Services;

public class GeminiImageParser
{
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;

    public GeminiImageParser(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<ImageParseResult> ParseAsync(string imagePath, string mimeType, string importType,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            return ImageParseResult.NotConfigured();

        var model = _configuration["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(model))
            model = Environment.GetEnvironmentVariable("GEMINI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gemini-2.5-flash";

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var imageBase64 = Convert.ToBase64String(bytes);
            var prompt = BuildPrompt(importType);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = imageBase64
                                }
                            },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ImageParseResult.Failed($"Gemini error {(int)response.StatusCode}: {responseBody}");

            var jsonText = ExtractCandidateText(responseBody);
            if (string.IsNullOrWhiteSpace(jsonText))
                return ImageParseResult.Failed("Gemini did not return JSON text.");

            var parsed = JsonSerializer.Deserialize<ImageParseResult>(CleanJson(jsonText),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
                return ImageParseResult.Failed("Cannot parse Gemini JSON response.");

            parsed.RawText ??= jsonText;
            parsed.Rows ??= new List<ImageParseRow>();
            return parsed;
        }
        catch (Exception ex)
        {
            return ImageParseResult.Failed(ex.Message);
        }
    }

    private static string BuildPrompt(string importType)
    {
        var isTraNo = importType == "TraNoHomNay";
        var task = isTraNo
            ? "ảnh trả nợ hôm nay: đọc tên khách hàng và số tiền trả"
            : "ảnh nhập nợ mới: đọc tên khách mua và số kg từng lần mua";

        var soLuongRule = isTraNo
            ? "- soLuongAnh: luôn null (không có cột kg trong ảnh trả nợ)."
            : "- soLuongAnh: số kg hoặc số lượng trên dòng đó, dùng dấu chấm thập phân.";

        var soTienRule = isTraNo
            ? "- soTienTra: số tiền trả của dòng đó."
            : "- soTienTra: luôn null (không có cột tiền trong ảnh nhập nợ).";

        var schema = "{\"rawText\":\"toàn bộ chữ/số đọc được\",\"rows\":[{\"imageOrder\":1,\"tenLai\":null,\"tenKhach\":\"tên khách\",\"soLuongAnh\":12.3,\"soTienTra\":null,\"confidence\":0.9,\"rawLine\":\"nguyên dòng\"}]}";

        return $"""
            Bạn là bộ đọc dữ liệu kế toán từ ảnh chụp sổ tay tiếng Việt.
            Nhiệm vụ: {task}.

            Chỉ trả về JSON hợp lệ, không markdown, theo schema:
            {schema}

            Quy tắc bắt buộc:
            - tenKhach: tên người/khách hàng đọc được trên dòng đó. Giữ nguyên dấu tiếng Việt.
            - tenLai: LUÔN để null. Chỉ điền nếu ảnh ghi rõ nhãn "Lái:" hoặc "lái:" trước tên.
            - Ký hiệu tiền tố như R, J, S, x, ×, u, δ, + đứng trước tên là ký hiệu loại hàng — BỎ QUA, không đưa vào tenKhach.
            {soLuongRule}
            {soTienRule}
            - confidence: từ 0.0 đến 1.0, phản ánh độ chắc chắn khi đọc dòng đó.
            - Giữ đúng thứ tự dòng từ trên xuống dưới.
            - Nếu không chắc thì để null và confidence thấp, không tự đoán.
            """;
    }

    private static string? ExtractCandidateText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts))
            return null;

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
                return text.GetString();
        }

        return null;
    }

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

public class ImageParseResult
{
    public string? RawText { get; set; }
    public List<ImageParseRow>? Rows { get; set; } = new();
    public string? Error { get; set; }
    public bool IsConfigured { get; set; } = true;

    public static ImageParseResult NotConfigured() => new()
    {
        IsConfigured = false,
        Error = "Gemini is not configured. Set Gemini:ApiKey or GEMINI_API_KEY to enable image parsing.",
        Rows = new List<ImageParseRow>()
    };

    public static ImageParseResult Failed(string error) => new()
    {
        Error = error,
        Rows = new List<ImageParseRow>()
    };
}

public class ImageParseRow
{
    public int ImageOrder { get; set; }
    public string? TenLai { get; set; }
    public string? TenKhach { get; set; }
    public decimal? SoLuongAnh { get; set; }
    public decimal? SoTienTra { get; set; }
    public decimal? Confidence { get; set; }
    public string? RawLine { get; set; }
}
