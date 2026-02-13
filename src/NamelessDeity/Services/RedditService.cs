using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NamelessDeity.Configuration;
using NamelessDeity.Helpers;
using NamelessDeity.Models;

namespace NamelessDeity.Services;

public interface IRedditService
{
    Task<RedditVideoInfo?> GetVideoInfoAsync(string redditUrl, CancellationToken cancellationToken = default);
}

public class RedditService : IRedditService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotConfiguration _config;
    private readonly ILogger<RedditService> _logger;

    public RedditService(
        IHttpClientFactory httpClientFactory,
        IOptions<BotConfiguration> config,
        ILogger<RedditService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<RedditVideoInfo?> GetVideoInfoAsync(string redditUrl, CancellationToken cancellationToken = default)
    {
        if (!RedditUrlHelper.IsRedditPostUrl(redditUrl))
        {
            _logger.LogWarning("Invalid Reddit URL: {Url}", redditUrl);
            return null;
        }

        try
        {
            // Resolve share URLs (/s/) to actual post URLs (/comments/)
            var resolvedUrl = await ResolveRedditUrl(redditUrl, cancellationToken);
            if (string.IsNullOrEmpty(resolvedUrl))
            {
                _logger.LogWarning("Could not resolve Reddit URL: {Url}", redditUrl);
                return null;
            }

            var jsonUrl = RedditUrlHelper.ToJsonUrl(resolvedUrl);
            _logger.LogInformation("Fetching Reddit JSON from: {Url}", jsonUrl);

            var client = _httpClientFactory.CreateClient("Reddit");
            var response = await client.GetAsync(jsonUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch Reddit JSON. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            var listings = await response.Content.ReadFromJsonAsync<RedditListing[]>(cancellationToken: cancellationToken);
            if (listings == null || listings.Length == 0)
            {
                _logger.LogWarning("No data found in Reddit response");
                return null;
            }

            var postData = FindMediaPost(listings[0].Data.Children[0].Data);

            if (postData == null)
            {
                return null;
            }

            return await ExtractMediaInfo(postData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Reddit media info");
            return null;
        }
    }

    private async Task<string?> ResolveRedditUrl(string url, CancellationToken cancellationToken)
    {
        // If it's already a direct post URL (contains /comments/), use it directly
        if (url.Contains("/comments/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // For share URLs (/s/) and short URLs (redd.it), follow redirects
        try
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_config.RedditUserAgent);
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(url, cancellationToken);

            // Follow redirects manually to get final URL
            while (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                   response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                   response.StatusCode == System.Net.HttpStatusCode.Found ||
                   response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect)
            {
                var redirectUrl = response.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(redirectUrl))
                    break;

                // Handle relative redirects
                if (!redirectUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUri = new Uri(url);
                    redirectUrl = new Uri(baseUri, redirectUrl).ToString();
                }

                _logger.LogInformation("Following redirect to: {Url}", redirectUrl);
                response = await client.GetAsync(redirectUrl, cancellationToken);
                url = redirectUrl;
            }

            // If we didn't get a /comments/ URL, convert share URL format to /comments/ format
            if (!url.Contains("/comments/", StringComparison.OrdinalIgnoreCase))
            {
                // Handle /s/ format: https://reddit.com/r/sub/s/id -> https://reddit.com/r/sub/comments/id
                if (url.Contains("/s/", StringComparison.OrdinalIgnoreCase))
                {
                    var formattedUrl = url.Replace("/s/", "/comments/");
                    _logger.LogInformation("Converted share URL to comments URL: {Url}", formattedUrl);
                    return formattedUrl;
                }

                _logger.LogWarning("Unable to resolve to /comments/ URL: {Url}", url);
                return null;
            }

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving Reddit URL");
            return null;
        }
    }

    private RedditPostData? FindMediaPost(RedditPostData data)
    {
        if (data.IsVideo)
            return data;

        if (!string.IsNullOrEmpty(data.Url) || !string.IsNullOrEmpty(data.UrlOverriddenByDest))
            return data;

        if (data.CrosspostParentList != null && data.CrosspostParentList.Count > 0)
        {
            foreach (var parent in data.CrosspostParentList)
            {
                var found = FindMediaPost(parent);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private async Task<RedditVideoInfo?> ExtractMediaInfo(RedditPostData postData, CancellationToken cancellationToken)
    {
        if (postData.IsVideo)
        {
            return await ExtractVideoInfo(postData, cancellationToken);
        }
        else
        {
            return ExtractImageInfo(postData);
        }
    }

    private async Task<RedditVideoInfo?> ExtractVideoInfo(RedditPostData postData, CancellationToken cancellationToken)
    {
        var videoUrl = ExtractVideoUrl(postData);
        if (string.IsNullOrEmpty(videoUrl))
        {
            return null;
        }

        var audioUrl = string.Empty;
        if (!postData.IsGif)
        {
            audioUrl = await ExtractAudioUrl(videoUrl, cancellationToken);
        }

        return new RedditVideoInfo
        {
            VideoUrl = videoUrl,
            AudioUrl = audioUrl,
            PostTitle = postData.Title,
            IsGif = postData.IsGif,
            IsImage = false
        };
    }

    private RedditVideoInfo? ExtractImageInfo(RedditPostData postData)
    {
        var imageUrl = ExtractImageUrl(postData);
        if (string.IsNullOrEmpty(imageUrl))
        {
            return null;
        }

        return new RedditVideoInfo
        {
            ImageUrl = imageUrl,
            PostTitle = postData.Title,
            IsImage = true,
            IsGif = false
        };
    }

    private string? ExtractVideoUrl(RedditPostData postData)
    {
        var media = postData.SecureMedia ?? postData.Media;
        if (media?.RedditVideo?.FallbackUrl != null)
        {
            return media.RedditVideo.FallbackUrl;
        }

        return null;
    }

    private string? ExtractImageUrl(RedditPostData postData)
    {
        if (!string.IsNullOrEmpty(postData.UrlOverriddenByDest))
        {
            return postData.UrlOverriddenByDest;
        }

        if (postData.Preview?.Images != null && postData.Preview.Images.Count > 0)
        {
            var previewImage = postData.Preview.Images[0];
            if (previewImage.Source?.Url != null)
            {
                return previewImage.Source.Url;
            }
        }

        if (!string.IsNullOrEmpty(postData.Url))
        {
            return postData.Url;
        }

        return null;
    }

    private async Task<string> ExtractAudioUrl(string videoUrl, CancellationToken cancellationToken)
    {
        var uri = new Uri(videoUrl);
        var baseUrl = $"{uri.Scheme}://{uri.Host}";
        var pathParts = uri.AbsolutePath.Split('/');
        var videoId = pathParts[1];

        var videoBaseUrl = $"{baseUrl}/{videoId}/";
        var dashUrl = $"{videoBaseUrl}DASHPlaylist.mpd";

        try
        {
            var client = _httpClientFactory.CreateClient("Reddit");
            var response = await client.GetAsync(dashUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var audioFileName = ParseDashManifest(content);
                if (!string.IsNullOrEmpty(audioFileName))
                {
                    return $"{videoBaseUrl}{audioFileName}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DASH manifest, trying fallback patterns");
        }

        return await TryAudioFallbackPatterns(videoBaseUrl, cancellationToken);
    }

    private string? ParseDashManifest(string xmlContent)
    {
        try
        {
            var doc = XDocument.Parse(xmlContent);
            var ns = XNamespace.Get("urn:mpeg:dash:schema:mpd:2011");

            var audioAdaptation = doc.Descendants(ns + "AdaptationSet")
                .FirstOrDefault(a => a.Attribute("contentType")?.Value == "audio");

            if (audioAdaptation != null)
            {
                var baseUrl = audioAdaptation.Descendants(ns + "BaseURL").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    return baseUrl;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse DASH manifest XML");
        }

        return null;
    }

    private async Task<string> TryAudioFallbackPatterns(string videoBaseUrl, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Reddit");
        var patterns = new[]
        {
            "DASH_AUDIO_128.mp4",
            "DASH_AUDIO_64.mp4",
            "DASH_audio.mp4",
            "audio.mp4",
            "audio"
        };

        foreach (var pattern in patterns)
        {
            var audioUrl = $"{videoBaseUrl}{pattern}";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, audioUrl);
                var response = await client.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength > 0)
                {
                    return audioUrl;
                }
            }
            catch
            {
                continue;
            }
        }

        return string.Empty;
    }
}
