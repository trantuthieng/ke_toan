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
        var provider = _configuration["AI:Provider"] ?? Environment.GetEnvironmentVariable("AI_PROVIDER");
        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "Claude", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(provider) && HasClaudeKey()))
        {
            return await ParseWithClaudeAsync(imagePath, mimeType, importType, cancellationToken);
        }

        return await ParseWithGeminiAsync(imagePath, mimeType, importType, cancellationToken);
    }

    private bool HasClaudeKey()
    {
        return !string.IsNullOrWhiteSpace(_configuration["Claude:ApiKey"])
            || !string.IsNullOrWhiteSpace(_configuration["Anthropic:ApiKey"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
    }

    private async Task<ImageParseResult> ParseWithGeminiAsync(string imagePath, string mimeType, string importType,
        CancellationToken cancellationToken)
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

    private async Task<ImageParseResult> ParseWithClaudeAsync(string imagePath, string mimeType, string importType,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["Claude:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = _configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ImageParseResult.NotConfigured(
                "Claude is not configured. Set ANTHROPIC_API_KEY to enable Claude image parsing.");
        }

        var model = _configuration["Claude:Model"];
        if (string.IsNullOrWhiteSpace(model))
            model = _configuration["Anthropic:Model"];
        if (string.IsNullOrWhiteSpace(model))
            model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "claude-sonnet-4-6";

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var imageBase64 = Convert.ToBase64String(bytes);
            var prompt = BuildPrompt(importType);

            var payload = new
            {
                model,
                max_tokens = 2048,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = NormalizeClaudeMimeType(mimeType),
                                    data = imageBase64
                                }
                            },
                            new { type = "text", text = prompt }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(payload);

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ImageParseResult.Failed($"Claude error {(int)response.StatusCode}: {responseBody}");

            var jsonText = ExtractClaudeText(responseBody);
            if (string.IsNullOrWhiteSpace(jsonText))
                return ImageParseResult.Failed("Claude did not return JSON text.");

            var parsed = JsonSerializer.Deserialize<ImageParseResult>(CleanJson(jsonText),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
                return ImageParseResult.Failed("Cannot parse Claude JSON response.");

            parsed.RawText ??= jsonText;
            parsed.Rows ??= new List<ImageParseRow>();
            return parsed;
        }
        catch (Exception ex)
        {
            return ImageParseResult.Failed(ex.Message);
        }
    }

    internal static string BuildPrompt(string importType)
    {
        var isTraNo = importType == "TraNoHomNay";
        var task = isTraNo
            ? "ảnh trả nợ hôm nay: đọc tên khách hàng và số tiền trả"
            : "ảnh nhập nợ mới: đọc tên khách mua và số kg từng lần mua";

        var soLuongRule = isTraNo
            ? "- soLuongAnh: luôn null (không có cột kg trong ảnh trả nợ)."
            : "- soLuongAnh: số kg/số lượng của dòng đó, dùng dấu chấm thập phân.";

        var soTienRule = isTraNo
            ? "- soTienTra: số tiền trả của dòng đó."
            : "- soTienTra: luôn null (không có cột tiền trong ảnh nhập nợ).";

        var schema = "{\"rawText\":\"toàn bộ chữ/số đọc được\",\"rows\":[{\"imageOrder\":1,\"tenLai\":null,\"tenKhach\":\"tên khách\",\"soLuongAnh\":12.3,\"soTienTra\":null,\"confidence\":0.9,\"rawLine\":\"nguyên dòng\"}]}";

        return $"""
            Bạn là bộ đọc dữ liệu kế toán từ ảnh chụp sổ tay tiếng Việt viết tay.
            Nhiệm vụ: {task}.

            Chỉ trả về JSON hợp lệ, không markdown, theo schema:
            {schema}

            ════ CẤU TRÚC TRANG ════
            Mỗi trang ghi nhiều khách hàng. Cấu trúc điển hình một nhóm:
              [Tên]  [kg1]  [kg2] ]  ×[giá]   [thành tiền 5-6 chữ số]
                     [kg3]  [kg4] ]

            ════ NHÓM NGOẶC — QUAN TRỌNG ════
            - Khi nhiều dòng số kg gộp trong dấu ngoặc vuông "]" với một tên duy nhất ở đầu nhóm:
              → Dòng đầu có tên: dùng tên đó, tạo row với soLuongAnh
              → Các dòng tiếp theo KHÔNG CÓ TÊN trong cùng nhóm: kế thừa tên dòng đầu nhóm, tạo row riêng
            - Nhiều số kg trên CÙNG MỘT DÒNG (vd "695 683", "829, 878", "1036 96"):
              → Tạo NHIỀU row riêng, mỗi số một row, cùng tenKhach

            ════ BỎ QUA — KHÔNG TẠO ROW ════
            - Dòng điều chỉnh/khấu trừ: bắt đầu bằng "-" (vd "-2", "-808", "-0.5", "-3kolu", "-1lu")
            - Đơn giá: số ngay sau "×" hoặc "x" (vd "×75", "×82") — đây là GIÁ/KG, không phải kg
            - Thành tiền: số 5-6 chữ số đứng cuối dòng/nhóm (vd 15142, 12206, 21940) — là tổng tiền
            - Đơn vị lượt: số dạng "Nlu", "1lu", "2lu" — là số chuyến, không phải kg
            - Dòng tổng kết: chứa "✿", "★", "※", ký hiệu "→" kèm số lớn, hoặc dạng "NNc = M.MMM → K.KKK"
            - Số trong ngoặc đầu dòng: "(8)", "(4.5)", "(4đ)", "(1.5)" — số lần mua/ngày, không phải kg

            ════ TÊN KHÁCH ════
            - tenKhach: tên người/khách đọc được đầu dòng, giữ nguyên dấu tiếng Việt.
            - Tên thường là tên người Việt (Thúy, Hương, Lan, Dung, Huê...) hoặc mã ngắn (T10, V1, V2).
              Nếu đọc ra chuỗi không giống tên Việt (vd "Play", "Job"), hãy đọc lại cẩn thận và để confidence thấp.
            - tenLai: LUÔN null. Chỉ điền khi ảnh ghi rõ "Lái:" hoặc "lái:" trước tên.
            - Bỏ ký hiệu tiền tố R, J, S, x, ×, u, δ, + đứng trước tên — không đưa vào tenKhach.
            - Dòng chỉ có số trong ngoặc không kèm tên → tenKhach = null.
            - Dòng phân cách "—" hay chỉ dấu gạch ngang → tenKhach = null (khách ẩn danh).

            ════ ĐỌC SỐ ════
            - Số có dấu chấm ĐẦU: ".601", ".88", ".95" → đọc là 601, 88, 95 (dấu chấm là ký hiệu ngăn cách, KHÔNG phải thập phân).
            - Số thập phân thật: "88.3", "92,5" → đọc đúng là 88.3 / 92.5.
            - Ký tự mơ hồ (C↔6, O↔0, l↔1, n↔u): đoán theo ngữ cảnh kg hợp lý (thường 10–2000), ghi rawLine.
            {soLuongRule}
            {soTienRule}
            - KHÔNG trả null cho soLuongAnh nếu dòng có số rõ ràng; không chắc thì confidence thấp, vẫn điền.

            ════ CHUNG ════
            - confidence: 0.0–1.0, phản ánh độ chắc chắn đọc dòng.
            - rawLine: ghi nguyên văn ký tự đọc được từ dòng đó.
            - Giữ đúng thứ tự dòng từ trên xuống dưới.
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

    private static string? ExtractClaudeText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "text", StringComparison.OrdinalIgnoreCase) &&
                item.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }

        return null;
    }

    private static string NormalizeClaudeMimeType(string mimeType)
    {
        var normalized = mimeType.ToLowerInvariant();
        return normalized switch
        {
            "image/jpg" => "image/jpeg",
            "image/jpeg" or "image/png" or "image/gif" or "image/webp" => normalized,
            _ => "image/jpeg"
        };
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

    public static ImageParseResult NotConfigured(string? error = null) => new()
    {
        IsConfigured = false,
        Error = error ?? "Gemini is not configured. Set Gemini:ApiKey or GEMINI_API_KEY to enable image parsing.",
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
