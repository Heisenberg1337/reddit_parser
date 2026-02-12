using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RedditVideoBot.Services;

public interface IInstagramService
{
    Task<InstagramMediaResult?> GetMediaAsync(string url, CancellationToken cancellationToken = default);
}

public class InstagramMediaResult
{
    public List<InstagramMediaItem> Items { get; set; } = new();
    public string? Title { get; set; }
}

public class InstagramMediaItem
{
    public string Url { get; set; } = string.Empty;
    public MediaType Type { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public enum MediaType
{
    Video,
    Image
}

public class InstagramService : IInstagramService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InstagramService> _logger;

    private static readonly Regex ShortcodeRegex = new(
        @"instagram\.com/(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public InstagramService(
        IHttpClientFactory httpClientFactory,
        ILogger<InstagramService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<InstagramMediaResult?> GetMediaAsync(string url, CancellationToken cancellationToken = default)
    {
        var shortcode = ExtractShortcode(url);
        if (string.IsNullOrEmpty(shortcode))
        {
            _logger.LogWarning("Could not extract shortcode from URL: {Url}", url);
            return null;
        }

        _logger.LogInformation("Extracting Instagram media for shortcode: {Shortcode}", shortcode);

        // Try multiple methods to get media
        var result = await TryEmbedEndpoint(shortcode, cancellationToken);
        if (result != null && result.Items.Count > 0)
        {
            _logger.LogInformation("Embed endpoint succeeded with {Count} items", result.Items.Count);
            return result;
        }

        result = await TryWebPageScrape(shortcode, cancellationToken);
        if (result != null && result.Items.Count > 0)
        {
            _logger.LogInformation("Web page scrape succeeded with {Count} items", result.Items.Count);
            return result;
        }

        _logger.LogWarning("All methods failed to extract Instagram media");
        return null;
    }

    private string? ExtractShortcode(string url)
    {
        var match = ShortcodeRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<InstagramMediaResult?> TryEmbedEndpoint(string shortcode, CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var embedUrl = $"https://www.instagram.com/p/{shortcode}/embed/captioned/";
            
            _logger.LogDebug("Trying embed URL: {Url}", embedUrl);
            
            var response = await client.GetAsync(embedUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Embed request failed with status: {Status}", response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseEmbedHtml(html, shortcode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embed endpoint failed");
            return null;
        }
    }

    private InstagramMediaResult? ParseEmbedHtml(string html, string shortcode)
    {
        var result = new InstagramMediaResult();

        // Try to find the embedded data JSON
        var jsonMatch = Regex.Match(html, @"window\.__additionalDataLoaded\s*\(\s*'[^']*'\s*,\s*(\{.+?\})\s*\)\s*;", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            try
            {
                var json = jsonMatch.Groups[1].Value;
                var parsed = ParseSharedData(json);
                if (parsed != null && parsed.Items.Count > 0)
                    return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse additionalDataLoaded JSON");
            }
        }

        // First priority: Find video_url in escaped JSON format (video_url\":\"...)
        // This pattern handles: video_url\":\"https:\\/\\/... or "video_url":"https://...
        var videoUrlMatch = Regex.Match(html, @"[""\\]video_url[""\\]*\s*[:\\]+\s*[""\\]+([^""\\]+(?:\\.[^""\\]+)*)", RegexOptions.IgnoreCase);
        if (videoUrlMatch.Success)
        {
            var videoUrl = DecodeEscapedUrl(videoUrlMatch.Groups[1].Value);
            _logger.LogDebug("Found video_url: {Url}", videoUrl);
            
            if (!string.IsNullOrEmpty(videoUrl) && videoUrl.Contains("cdninstagram"))
            {
                result.Items.Add(new InstagramMediaItem
                {
                    Url = videoUrl,
                    Type = MediaType.Video
                });
                
                // Extract caption
                ExtractCaption(html, result);
                return result;
            }
        }

        // Second: Look for .mp4 URLs directly
        var mp4Match = Regex.Match(html, @"(https?:[^""'\s\\]*?\.mp4[^""'\s\\]*)", RegexOptions.IgnoreCase);
        if (mp4Match.Success)
        {
            var videoUrl = DecodeEscapedUrl(mp4Match.Groups[1].Value);
            _logger.LogDebug("Found mp4 URL: {Url}", videoUrl);
            
            if (!string.IsNullOrEmpty(videoUrl))
            {
                result.Items.Add(new InstagramMediaItem
                {
                    Url = videoUrl,
                    Type = MediaType.Video
                });
                
                ExtractCaption(html, result);
                return result;
            }
        }

        // Third: Check for is_video flag and extract accordingly
        if (html.Contains("\"is_video\":true") || html.Contains("\\\"is_video\\\":true"))
        {
            // It's a video, try harder to find the URL
            var allUrls = Regex.Matches(html, @"https?:\\?/\\?/[^""'\s<>]+cdninstagram[^""'\s<>]+");
            foreach (Match urlMatch in allUrls)
            {
                var url = DecodeEscapedUrl(urlMatch.Value);
                if (url.Contains(".mp4") || url.Contains("/v/"))
                {
                    result.Items.Add(new InstagramMediaItem
                    {
                        Url = url,
                        Type = MediaType.Video
                    });
                    
                    ExtractCaption(html, result);
                    return result;
                }
            }
        }

        // If no video found, try to find display_url for image
        var displayUrlMatch = Regex.Match(html, @"[""\\]display_url[""\\]*\s*[:\\]+\s*[""\\]+([^""\\]+(?:\\.[^""\\]+)*)", RegexOptions.IgnoreCase);
        if (displayUrlMatch.Success)
        {
            var imageUrl = DecodeEscapedUrl(displayUrlMatch.Groups[1].Value);
            _logger.LogDebug("Found display_url: {Url}", imageUrl);
            
            if (!string.IsNullOrEmpty(imageUrl))
            {
                result.Items.Add(new InstagramMediaItem
                {
                    Url = imageUrl,
                    Type = MediaType.Image
                });
            }
        }

        // Try to find image in embed MediaImage class
        if (result.Items.Count == 0)
        {
            var imgMatch = Regex.Match(html, @"<img[^>]*class=""[^""]*EmbeddedMediaImage[^""]*""[^>]*src=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!imgMatch.Success)
            {
                imgMatch = Regex.Match(html, @"<img[^>]*src=""([^""]+)""[^>]*class=""[^""]*EmbeddedMediaImage", RegexOptions.IgnoreCase);
            }
            if (imgMatch.Success)
            {
                var imageUrl = System.Net.WebUtility.HtmlDecode(imgMatch.Groups[1].Value);
                result.Items.Add(new InstagramMediaItem
                {
                    Url = imageUrl,
                    Type = MediaType.Image
                });
            }
        }

        // Last resort: find any high-quality image from CDN
        if (result.Items.Count == 0)
        {
            var anyImgMatch = Regex.Match(html, @"(https?:[^""'\s\\]*cdninstagram[^""'\s\\]*\.(?:jpg|jpeg|png|webp)[^""'\s\\]*)", RegexOptions.IgnoreCase);
            if (anyImgMatch.Success)
            {
                result.Items.Add(new InstagramMediaItem
                {
                    Url = DecodeEscapedUrl(anyImgMatch.Groups[1].Value),
                    Type = MediaType.Image
                });
            }
        }

        ExtractCaption(html, result);
        return result.Items.Count > 0 ? result : null;
    }

    private void ExtractCaption(string html, InstagramMediaResult result)
    {
        if (!string.IsNullOrEmpty(result.Title))
            return;

        var captionMatch = Regex.Match(html, @"<div[^>]*class=""[^""]*Caption[^""]*""[^>]*>.*?<div[^>]*>([^<]+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (captionMatch.Success)
        {
            result.Title = System.Net.WebUtility.HtmlDecode(captionMatch.Groups[1].Value.Trim());
        }
    }

    private static string DecodeEscapedUrl(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;
        
        // Handle multiple levels of escaping: \\\\/ -> \\/ -> /
        // Keep replacing until no more changes
        string previous;
        do
        {
            previous = result;
            result = result.Replace("\\\\/", "/");
            result = result.Replace("\\/", "/");
            result = result.Replace("\\\\", "\\");
        } while (result != previous);
        
        // Handle unicode escape sequences (both \u0026 and \\u0026)
        result = Regex.Replace(result, @"\\+u([0-9A-Fa-f]{4})", match =>
        {
            var code = int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
            return char.ConvertFromUtf32(code);
        });

        // HTML decode
        result = System.Net.WebUtility.HtmlDecode(result);
        
        // URL should now be properly decoded, but clean up any remaining escapes
        result = result.Replace("\\", "");
        
        return result;
    }

    private async Task<InstagramMediaResult?> TryWebPageScrape(string shortcode, CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateClient();
            var pageUrl = $"https://www.instagram.com/p/{shortcode}/";
            
            _logger.LogDebug("Trying web page: {Url}", pageUrl);
            
            var response = await client.GetAsync(pageUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Web page request failed with status: {Status}", response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Try to find shared data in script tag
            var sharedDataMatch = Regex.Match(html, @"<script[^>]*>window\._sharedData\s*=\s*(\{.+?\});</script>", RegexOptions.Singleline);
            if (sharedDataMatch.Success)
            {
                return ParseSharedData(sharedDataMatch.Groups[1].Value);
            }

            // Try alternate format
            var altDataMatch = Regex.Match(html, @"<script[^>]*type=""application/ld\+json""[^>]*>(\{.+?\})</script>", RegexOptions.Singleline);
            if (altDataMatch.Success)
            {
                return ParseLdJson(altDataMatch.Groups[1].Value);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Web page scrape failed");
            return null;
        }
    }

    private InstagramMediaResult? ParseSharedData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Navigate to the media object
            JsonElement media;
            if (root.TryGetProperty("entry_data", out var entryData) &&
                entryData.TryGetProperty("PostPage", out var postPage) &&
                postPage.GetArrayLength() > 0)
            {
                var post = postPage[0];
                if (!post.TryGetProperty("graphql", out var graphql) ||
                    !graphql.TryGetProperty("shortcode_media", out media))
                {
                    return null;
                }
            }
            else if (root.TryGetProperty("graphql", out var graphql) &&
                     graphql.TryGetProperty("shortcode_media", out media))
            {
                // Direct format
            }
            else if (root.TryGetProperty("shortcode_media", out media))
            {
                // Even more direct
            }
            else
            {
                return null;
            }

            return ExtractFromMedia(media);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse shared data");
            return null;
        }
    }

    private InstagramMediaResult? ExtractFromMedia(JsonElement media)
    {
        var result = new InstagramMediaResult();

        // Get caption
        if (media.TryGetProperty("edge_media_to_caption", out var captionEdge) &&
            captionEdge.TryGetProperty("edges", out var edges) &&
            edges.GetArrayLength() > 0 &&
            edges[0].TryGetProperty("node", out var node) &&
            node.TryGetProperty("text", out var text))
        {
            result.Title = text.GetString();
        }

        // Check if it's a carousel (sidecar)
        if (media.TryGetProperty("edge_sidecar_to_children", out var sidecar) &&
            sidecar.TryGetProperty("edges", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                if (child.TryGetProperty("node", out var childNode))
                {
                    var item = ExtractMediaItem(childNode);
                    if (item != null)
                        result.Items.Add(item);
                }
            }
        }
        else
        {
            // Single media item
            var item = ExtractMediaItem(media);
            if (item != null)
                result.Items.Add(item);
        }

        return result.Items.Count > 0 ? result : null;
    }

    private InstagramMediaItem? ExtractMediaItem(JsonElement media)
    {
        var isVideo = media.TryGetProperty("is_video", out var isVideoProp) && isVideoProp.GetBoolean();

        if (isVideo)
        {
            if (media.TryGetProperty("video_url", out var videoUrl))
            {
                return new InstagramMediaItem
                {
                    Url = videoUrl.GetString() ?? "",
                    Type = MediaType.Video
                };
            }
        }
        
        // Get display_url for images (or video thumbnail)
        if (media.TryGetProperty("display_url", out var displayUrl))
        {
            return new InstagramMediaItem
            {
                Url = displayUrl.GetString() ?? "",
                Type = isVideo ? MediaType.Video : MediaType.Image
            };
        }

        return null;
    }

    private InstagramMediaResult? ParseLdJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new InstagramMediaResult();

            if (root.TryGetProperty("name", out var name))
            {
                result.Title = name.GetString();
            }

            // Check for video
            if (root.TryGetProperty("video", out var video) &&
                video.GetArrayLength() > 0 &&
                video[0].TryGetProperty("contentUrl", out var videoUrl))
            {
                result.Items.Add(new InstagramMediaItem
                {
                    Url = videoUrl.GetString() ?? "",
                    Type = MediaType.Video
                });
            }

            // Check for image
            if (result.Items.Count == 0 && root.TryGetProperty("image", out var image))
            {
                var imageUrl = image.ValueKind == JsonValueKind.Array && image.GetArrayLength() > 0
                    ? image[0].GetString()
                    : image.GetString();

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    result.Items.Add(new InstagramMediaItem
                    {
                        Url = imageUrl,
                        Type = MediaType.Image
                    });
                }
            }

            return result.Items.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse LD+JSON");
            return null;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("Instagram");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        return client;
    }
}
