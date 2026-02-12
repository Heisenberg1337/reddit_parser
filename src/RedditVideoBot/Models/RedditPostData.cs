using System.Text.Json.Serialization;

namespace RedditVideoBot.Models;

public class RedditListing
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public RedditListingData Data { get; set; } = new();
}

public class RedditListingData
{
    [JsonPropertyName("children")]
    public List<RedditChild> Children { get; set; } = new();
}

public class RedditChild
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public RedditPostData Data { get; set; } = new();
}

public class RedditPostData
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("is_video")]
    public bool IsVideo { get; set; }

    [JsonPropertyName("is_gif")]
    public bool IsGif { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("url_overridden_by_dest")]
    public string UrlOverriddenByDest { get; set; } = string.Empty;

    [JsonPropertyName("preview")]
    public RedditPreview? Preview { get; set; }

    [JsonPropertyName("media")]
    public RedditMedia? Media { get; set; }

    [JsonPropertyName("secure_media")]
    public RedditMedia? SecureMedia { get; set; }

    [JsonPropertyName("crosspost_parent_list")]
    public List<RedditPostData>? CrosspostParentList { get; set; }
}

public class RedditMedia
{
    [JsonPropertyName("reddit_video")]
    public RedditVideo? RedditVideo { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class RedditVideo
{
    [JsonPropertyName("fallback_url")]
    public string? FallbackUrl { get; set; }

    [JsonPropertyName("dash_url")]
    public string? DashUrl { get; set; }

    [JsonPropertyName("hls_url")]
    public string? HlsUrl { get; set; }
}

public class RedditPreview
{
    [JsonPropertyName("images")]
    public List<RedditPreviewImage>? Images { get; set; }
}

public class RedditPreviewImage
{
    [JsonPropertyName("source")]
    public RedditPreviewSource? Source { get; set; }
}

public class RedditPreviewSource
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class RedditVideoInfo
{
    public string VideoUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string PostTitle { get; set; } = string.Empty;
    public bool IsGif { get; set; }
    public bool IsImage { get; set; }

    public bool HasAudio => !string.IsNullOrEmpty(AudioUrl);
    public bool HasVideo => !string.IsNullOrEmpty(VideoUrl);
    public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
}
