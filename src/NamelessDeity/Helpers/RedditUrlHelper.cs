using System.Text.RegularExpressions;

namespace NamelessDeity.Helpers;

public static class RedditUrlHelper
{
    // Matches: https://reddit.com/r/subreddit/comments/id/...
    private static readonly Regex RedditPostUrlPattern = new(
        @"https?://(?:www\.)?reddit\.com/r/[^/]+/comments/[\w]+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Matches: https://reddit.com/r/subreddit/s/shareId (share links)
    private static readonly Regex RedditShareUrlPattern = new(
        @"https?://(?:www\.)?reddit\.com/r/[^/]+/s/[\w]+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Matches: https://v.redd.it/videoId or https://redd.it/id
    private static readonly Regex RedditShortUrlPattern = new(
        @"https?://(?:v\.)?redd\.it/[\w]+[^\s]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool IsRedditPostUrl(string url)
    {
        return !string.IsNullOrWhiteSpace(url) && 
               (RedditPostUrlPattern.IsMatch(url) || RedditShareUrlPattern.IsMatch(url) || RedditShortUrlPattern.IsMatch(url));
    }

    public static List<string> ExtractRedditUrls(string text)
    {
        var urls = new List<string>();
        
        if (string.IsNullOrWhiteSpace(text))
            return urls;

        // Extract full reddit.com URLs (comments)
        foreach (Match match in RedditPostUrlPattern.Matches(text))
        {
            urls.Add(CleanUrl(match.Value));
        }

        // Extract Reddit share URLs (/s/ format)
        foreach (Match match in RedditShareUrlPattern.Matches(text))
        {
            urls.Add(CleanUrl(match.Value));
        }

        // Extract short redd.it URLs
        foreach (Match match in RedditShortUrlPattern.Matches(text))
        {
            urls.Add(CleanUrl(match.Value));
        }

        return urls.Distinct().ToList();
    }

    private static string CleanUrl(string url)
    {
        // Remove trailing punctuation that might have been captured
        return url.TrimEnd('.', ',', '!', '?', ')', ']', '}', '"', '\'');
    }

    public static string ToJsonUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        var uri = new Uri(url);
        var baseUrl = uri.GetLeftPart(UriPartial.Path);

        return $"{baseUrl}.json";
    }
}
