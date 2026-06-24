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
        importType = NormalizeImportType(importType);
        var provider = _configuration["AI:Provider"] ?? Environment.GetEnvironmentVariable("AI_PROVIDER");
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "GPT", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(provider) && !HasClaudeKey() && HasOpenAiKey()))
        {
            return await ParseWithOpenAiAsync(imagePath, mimeType, importType, cancellationToken);
        }

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

    private bool HasOpenAiKey()
    {
        return !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"])
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
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
            return NormalizeResult(parsed, importType);
        }
        catch (Exception ex)
        {
            return ImageParseResult.Failed(ex.Message);
        }
    }

    private async Task<ImageParseResult> ParseWithOpenAiAsync(string imagePath, string mimeType, string importType,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ImageParseResult.NotConfigured(
                "OpenAI is not configured. Set OpenAI:ApiKey or OPENAI_API_KEY to enable OpenAI image parsing.");
        }

        var model = _configuration["OpenAI:Model"];
        if (string.IsNullOrWhiteSpace(model))
            model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-5.4";

        var imageDetail = _configuration["OpenAI:ImageDetail"];
        if (string.IsNullOrWhiteSpace(imageDetail))
            imageDetail = Environment.GetEnvironmentVariable("OPENAI_IMAGE_DETAIL");
        imageDetail = NormalizeOpenAiImageDetail(imageDetail);

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var imageBase64 = Convert.ToBase64String(bytes);
            var prompt = BuildPrompt(importType);
            var imageUrl = $"data:{NormalizeOpenAiMimeType(mimeType)};base64,{imageBase64}";

            var payload = new
            {
                model,
                input = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = prompt },
                            new { type = "input_image", image_url = imageUrl, detail = imageDetail }
                        }
                    }
                },
                max_output_tokens = 8000,
                store = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ImageParseResult.Failed($"OpenAI error {(int)response.StatusCode}: {responseBody}");

            var jsonText = ExtractOpenAiText(responseBody);
            if (string.IsNullOrWhiteSpace(jsonText))
                return ImageParseResult.Failed("OpenAI did not return JSON text.");

            var parsed = JsonSerializer.Deserialize<ImageParseResult>(CleanJson(jsonText),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
                return ImageParseResult.Failed("Cannot parse OpenAI JSON response.");

            parsed.RawText ??= jsonText;
            return NormalizeResult(parsed, importType);
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
                max_tokens = 8000,
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
            return NormalizeResult(parsed, importType);
        }
        catch (Exception ex)
        {
            return ImageParseResult.Failed(ex.Message);
        }
    }

    internal static string BuildPrompt(string importType)
    {
        return NormalizeImportType(importType) == "TraNoHomNay"
            ? BuildTraNoPrompt()
            : BuildNhapNoPrompt();
    }

    internal static ImageParseResult NormalizeResult(ImageParseResult parsed, string importType)
    {
        var isTraNo = NormalizeImportType(importType) == "TraNoHomNay";
        var rows = parsed.Rows ?? new List<ImageParseRow>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.ImageOrder = row.ImageOrder > 0 ? row.ImageOrder : i + 1;
            row.Confidence = row.Confidence.HasValue
                ? Math.Clamp(row.Confidence.Value, 0m, 1m)
                : null;

            if (isTraNo)
                row.SoLuongAnh = null;
            else
                row.SoTienTra = null;
        }

        parsed.Rows = rows
            .Where(row => isTraNo
                ? !string.IsNullOrWhiteSpace(row.TenKhach) && row.SoTienTra > 0
                : row.SoLuongAnh > 0)
            .ToList();

        if (parsed.Rows.Count == 0 && string.IsNullOrWhiteSpace(parsed.Error))
        {
            parsed.Error = isTraNo
                ? "Không đọc được dòng trả nợ hợp lệ (tên khách và số tiền trả)."
                : "Không đọc được dòng nhập nợ hợp lệ (tên khách và số kg).";
        }

        return parsed;
    }

    private static string NormalizeImportType(string? importType) =>
        string.Equals(importType, "TraNoHomNay", StringComparison.OrdinalIgnoreCase)
            ? "TraNoHomNay"
            : "NhapNoMoi";

    private static string BuildNhapNoPrompt()
    {
        const string schema =
            "{\"rawText\":null,\"rows\":[{\"imageOrder\":1,\"tenLai\":null,\"tenKhach\":\"tên khách\",\"soLuongAnh\":12.3,\"soTienTra\":null,\"confidence\":0.9,\"rawLine\":\"tên+số kg\"}]}";

        return $"""
            Bạn là bộ đọc dữ liệu kế toán từ ảnh chụp sổ tay tiếng Việt viết tay.
            LOẠI DỮ LIỆU: NHẬP NỢ MỚI.
            Nhiệm vụ duy nhất: đọc tên khách mua và số kg của từng lần mua.

            Chỉ trả về JSON hợp lệ, không markdown, theo schema:
            {schema}

            QUY TẮC TRƯỜNG:
            - soLuongAnh: bắt buộc là số kg của dòng.
            - soTienTra: luôn null. Tuyệt đối không đưa thành tiền hoặc tiền trả vào trường này.

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
            - soLuongAnh là số kg/số lượng của dòng đó, dùng dấu chấm thập phân.
            - KHÔNG trả null cho soLuongAnh nếu dòng có số rõ ràng; không chắc thì confidence thấp, vẫn điền.

            ════ CHUNG ════
            - confidence: 0.0–1.0, phản ánh độ chắc chắn đọc dòng.
            - rawLine: tối đa 20 ký tự, chỉ ghi tên + số chính (vd "Thúy 96", "695").
            - rawText: để null (không ghi lại toàn bộ trang — tiết kiệm token).
            - Giữ đúng thứ tự dòng từ trên xuống dưới.
            - PHẢI đọc và trả về TẤT CẢ các dòng có số kg trong ảnh, không bỏ sót.
            """;
    }

    private static string BuildTraNoPrompt()
    {
        const string schema =
            "{\"rawText\":null,\"rows\":[{\"imageOrder\":1,\"tenLai\":null,\"tenKhach\":\"tên khách\",\"soLuongAnh\":null,\"soTienTra\":150000,\"confidence\":0.9,\"rawLine\":\"tên+số tiền\"}]}";

        return $"""
            Bạn là bộ đọc dữ liệu kế toán từ ảnh chụp danh sách trả nợ tiếng Việt viết tay.
            LOẠI DỮ LIỆU: TRẢ NỢ HÔM NAY.
            Nhiệm vụ duy nhất: đọc tên khách hàng và SỐ TIỀN khách đã trả.

            Chỉ trả về JSON hợp lệ, không markdown, theo schema:
            {schema}

            QUY TẮC BẮT BUỘC:
            - Mỗi khoản trả nợ tạo đúng một row.
            - tenKhach: tên khách trả tiền, giữ nguyên dấu tiếng Việt.
            - soTienTra: số tiền trả bằng VND, là số nguyên dương.
            - soLuongAnh: luôn null. Ảnh trả nợ không có dữ liệu kg.
            - tenLai: luôn null, trừ khi ảnh ghi rõ "Lái:" trước tên.
            - Không suy luận số kg, đơn giá, thành tiền bán hàng hoặc số lượng hàng.
            - Không tự tính toán hay bù trừ số tiền. Chỉ chép đúng khoản tiền được ghi cạnh tên.

            ĐỌC SỐ TIỀN:
            - "100.000", "100,000", "100 000" → 100000.
            - "100k", "100 K", "100 nghìn" → 100000.
            - "1tr", "1 triệu" → 1000000; "1,5tr" → 1500000.
            - Ký hiệu "đ", "₫", dấu chấm/phẩy phân cách hàng nghìn không được giữ trong JSON.
            - Nếu một tên có nhiều khoản tiền riêng biệt trên nhiều dòng, tạo nhiều row cùng tenKhach.
            - Nếu các dòng số phía dưới thuộc cùng một tên/ngoặc nhóm, kế thừa tenKhach từ đầu nhóm.
            - Bỏ qua dòng tổng cộng, số dư, nợ còn lại, ngày tháng và số thứ tự.
            - Không tạo row nếu không xác định được khoản tiền trả dương.

            TÊN VÀ ĐỘ TIN CẬY:
            - Bỏ ký hiệu trang trí hoặc số thứ tự đứng trước tên.
            - Nếu tên khó đọc, vẫn ghi cách đọc tốt nhất và giảm confidence.
            - confidence: từ 0.0 đến 1.0.
            - rawLine: tối đa 30 ký tự, chỉ ghi tên + số tiền chính.
            - rawText: luôn null.
            - Giữ đúng thứ tự từ trên xuống dưới và không bỏ sót khoản trả nợ.
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

    private static string? ExtractOpenAiText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        var texts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    texts.Add(text.GetString() ?? "");
                }
            }
        }

        return texts.Count > 0 ? string.Join("\n", texts).Trim() : null;
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

    private static string NormalizeOpenAiMimeType(string mimeType)
    {
        var normalized = mimeType.ToLowerInvariant();
        return normalized switch
        {
            "image/jpg" => "image/jpeg",
            "image/jpeg" or "image/png" or "image/gif" or "image/webp" => normalized,
            _ => "image/jpeg"
        };
    }

    private static string NormalizeOpenAiImageDetail(string? detail)
    {
        return string.Equals(detail, "low", StringComparison.OrdinalIgnoreCase)
            ? "low"
            : string.Equals(detail, "original", StringComparison.OrdinalIgnoreCase)
                ? "original"
                : "high";
    }

    private static string CleanJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            trimmed = firstNewLine >= 0 && lastFence > firstNewLine
                ? trimmed[(firstNewLine + 1)..lastFence].Trim()
                : trimmed.Trim('`').Trim();
        }

        var firstObject = trimmed.IndexOf('{');
        var lastObject = trimmed.LastIndexOf('}');
        return firstObject >= 0 && lastObject > firstObject
            ? trimmed[firstObject..(lastObject + 1)].Trim()
            : trimmed;
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
