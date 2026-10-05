using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SayMaker
{
    /// <summary>Why a call failed, split by what to do next.</summary>
    public enum SayMakerErrorKind
    {
        /// <summary>No API key, or the key is invalid or deleted.</summary>
        Auth,
        /// <summary>The account is out of credits.</summary>
        Quota,
        /// <summary>The model needs a paid plan or a credit pack.</summary>
        Plan,
        /// <summary>Another run on this account is still rendering. Free accounts run one at a time.</summary>
        Busy,
        /// <summary>The content filter or the model refused the prompt. Reword it.</summary>
        Rejected,
        /// <summary>The deadline passed; the run may still finish in your SayMaker history.</summary>
        Timeout,
        /// <summary>Anything else the service answered.</summary>
        Other,
    }

    /// <summary>The exception this library throws on purpose. Branch on <see cref="Kind"/>.</summary>
    public sealed class SayMakerException : Exception
    {
        /// <summary>What went wrong.</summary>
        public SayMakerErrorKind Kind { get; }

        /// <summary>Creates an exception of a given kind.</summary>
        public SayMakerException(SayMakerErrorKind kind, string message) : base(message) { Kind = kind; }

        /// <summary>Map a refusal message to the error that says what to do next.</summary>
        public static SayMakerException Classify(string message, bool busy = false)
        {
            var low = message.ToLowerInvariant();
            if (busy || low.Contains("is rendering") || low.Contains("at once")) return new SayMakerException(SayMakerErrorKind.Busy, message);
            if (low.Contains("api key") || low.Contains("no auth")) return new SayMakerException(SayMakerErrorKind.Auth, message);
            if (low.Contains("insufficient credits")) return new SayMakerException(SayMakerErrorKind.Quota, message);
            if (low.Contains("subscription") || low.Contains("plan") || low.Contains("sign in")) return new SayMakerException(SayMakerErrorKind.Plan, message);
            return new SayMakerException(SayMakerErrorKind.Other, message);
        }
    }

    /// <summary>Options for an image.</summary>
    public sealed class ImageOptions
    {
        /// <summary>A model id, e.g. "gpt-image-2-5-flare". Defaults to the cheapest text-to-image model, or the edit model when <see cref="ImageUrl"/> is set.</summary>
        public string? Model { get; set; }
        /// <summary>A public http(s) url; makes the call an edit.</summary>
        public string? ImageUrl { get; set; }
        /// <summary>"1:1", "16:9", "9:16", "4:3", "3:4".</summary>
        public string? AspectRatio { get; set; }
        /// <summary>Defaults to 75 minutes, which covers the free-account queue.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(75);
        /// <summary>Defaults to five seconds.</summary>
        public TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(5);
    }

    /// <summary>Options for a clip.</summary>
    public sealed class VideoOptions
    {
        /// <summary>A model id, e.g. "veo-3-1". Defaults to minimax-h3-fast, the one a free account can run.</summary>
        public string Model { get; set; } = SayMakerClient.ModelVideo;
        /// <summary>Optional first frame; makes the call image-to-video.</summary>
        public string? ImageUrl { get; set; }
        /// <summary>Seconds, where the model offers a choice.</summary>
        public int? Duration { get; set; }
        /// <summary>e.g. "480p", "720p", "1080p".</summary>
        public string? Resolution { get; set; }
        /// <summary>Defaults to "16:9".</summary>
        public string AspectRatio { get; set; } = "16:9";
        /// <summary>Ask for sound where the model writes it in the same pass.</summary>
        public bool Sound { get; set; } = true;
        /// <summary>Defaults to 90 minutes.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(90);
        /// <summary>Defaults to ten seconds.</summary>
        public TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Generates images and video on SayMaker (https://saymaker.ai). One API key runs the whole shelf on
    /// your own credits: Veo 3.1, Kling 3.0, Seedance 2.0, MiniMax H3, Nano Banana 2, GPT Image 2.5,
    /// Seedream 5.0 and more. Create a key at https://saymaker.ai/settings/apikeys.
    /// </summary>
    public sealed class SayMakerClient
    {
        /// <summary>The service this library talks to.</summary>
        public const string DefaultBaseUrl = "https://saymaker.ai";
        /// <summary>Where an API key is created.</summary>
        public const string KeysUrl = DefaultBaseUrl + "/settings/apikeys";
        /// <summary>The cheapest text-to-image model.</summary>
        public const string ModelImage = "saymaker-image-v1";
        /// <summary>The default edit model; it takes an input image.</summary>
        public const string ModelEdit = "nano-banana-2-lite";
        /// <summary>The video model a free account can run (480p or 768p, 4 to 15 seconds).</summary>
        public const string ModelVideo = "minimax-h3-fast";

        /// <summary>Model id to the routing field the API expects next to it. Unlisted ids go as "kie".</summary>
        public static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string>
        {
            ["saymaker-image-v1"] = "kie", ["nano-banana-2-lite"] = "kie", ["nano-banana-2"] = "poyo",
            ["nano-banana-pro"] = "poyo", ["gpt-image-2"] = "poyo", ["gpt-image-2-5-flare"] = "kie",
            ["seedream-5-lite"] = "poyo", ["seedream-5-pro"] = "kie", ["qwen-image-3-pro"] = "kie",
            ["grok-imagine-image-2-0"] = "poyo", ["minimax-h3-fast"] = "vgenv", ["minimax-h3"] = "poyo",
            ["seedance-2"] = "kie", ["seedance-2-fast"] = "kie", ["seedance-2-5"] = "kie", ["veo-3-1"] = "kie",
            ["kling-3-0"] = "kie", ["kling-3-0-turbo"] = "kie", ["wan-3-0"] = "kie", ["ltx-2-5-fast"] = "replicate",
        };

        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly HttpClient _http;

        /// <summary>A client for one API key. Null means the SAYMAKER_API_KEY environment variable.</summary>
        public SayMakerClient(string? apiKey = null, HttpClient? http = null, string baseUrl = DefaultBaseUrl)
        {
            _apiKey = (apiKey ?? Environment.GetEnvironmentVariable("SAYMAKER_API_KEY") ?? "").Trim();
            _http = http ?? new HttpClient();
            _baseUrl = baseUrl;
        }

        /// <summary>Generate one image, or edit <see cref="ImageOptions.ImageUrl"/>, and return its URL.</summary>
        public Task<string> GenerateAsync(string prompt, ImageOptions? options = null, CancellationToken ct = default)
        {
            var o = options ?? new ImageOptions();
            CheckUrl(o.ImageUrl);
            var edit = o.ImageUrl != null;
            var opts = new Dictionary<string, object?>
            {
                ["size"] = o.AspectRatio ?? (edit ? "auto" : "1:1"),
                ["resolution"] = "1K",
                ["n"] = 1,
            };
            if (edit) opts["image_input"] = new[] { o.ImageUrl };
            return RunAsync("image", edit ? "image-to-image" : "text-to-image", o.Model ?? (edit ? ModelEdit : ModelImage), prompt, opts, o.Timeout, o.PollEvery, ct);
        }

        /// <summary>Generate one clip, or animate <see cref="VideoOptions.ImageUrl"/> as the first frame, and return its URL.</summary>
        public Task<string> GenerateVideoAsync(string prompt, VideoOptions? options = null, CancellationToken ct = default)
        {
            var o = options ?? new VideoOptions();
            CheckUrl(o.ImageUrl);
            var opts = new Dictionary<string, object?> { ["aspect_ratio"] = o.AspectRatio, ["sound"] = o.Sound };
            if (o.ImageUrl != null) opts["image_input"] = new[] { o.ImageUrl };
            if (o.Duration != null) opts["duration"] = o.Duration;
            if (o.Resolution != null) opts["resolution"] = o.Resolution;
            return RunAsync("video", o.ImageUrl != null ? "image-to-video" : "text-to-video", o.Model, prompt, opts, o.Timeout, o.PollEvery, ct);
        }

        private static void CheckUrl(string? u)
        {
            if (u != null && !(u.StartsWith("http://", StringComparison.Ordinal) || u.StartsWith("https://", StringComparison.Ordinal)))
                throw new ArgumentException("ImageUrl must be a public http(s) url");
        }

        private async Task<JsonElement> PostAsync(string path, object body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            req.Headers.TryAddWithoutValidation("User-Agent", "saymaker-dotnet/0.1.0");
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
            return doc.RootElement.Clone();
        }

        private static bool CodeIsZero(JsonElement env) =>
            env.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.GetInt32() == 0;

        private async Task<string> RunAsync(string media, string scene, string model, string prompt,
            Dictionary<string, object?> options, TimeSpan timeout, TimeSpan pollEvery, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("prompt is required");
            if (_apiKey.Length == 0) throw new SayMakerException(SayMakerErrorKind.Auth, "an API key is required; create one at " + KeysUrl);
            var env = await PostAsync("/api/ai/generate", new Dictionary<string, object?>
            {
                ["provider"] = Models.TryGetValue(model, out var p) ? p : "kie",
                ["mediaType"] = media,
                ["model"] = model,
                ["scene"] = scene,
                ["prompt"] = prompt,
                ["options"] = options,
            }, ct).ConfigureAwait(false);
            var hasData = env.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
            // Refusals answer HTTP 200 with code -1; the status code alone reads as success.
            if (!CodeIsZero(env))
            {
                var msg = env.TryGetProperty("message", out var m) ? m.GetString() ?? "request refused" : "request refused";
                throw SayMakerException.Classify(msg, hasData && data.TryGetProperty("busy", out _));
            }
            // A signed-out request is also a 200 with code 0; only `wall` tells it apart.
            if (hasData && data.TryGetProperty("wall", out var wall) && wall.ValueKind == JsonValueKind.True)
                throw new SayMakerException(SayMakerErrorKind.Auth, "an API key is required; create one at " + KeysUrl);
            if (!hasData || !data.TryGetProperty("id", out var idEl)) throw new SayMakerException(SayMakerErrorKind.Other, "the service returned no task id");
            var id = idEl.GetString();

            // Free-account runs only reach the model on the poll that crosses the end of the queue
            // wait, so polling is what starts the work.
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(pollEvery, ct).ConfigureAwait(false);
                JsonElement q;
                try { q = await PostAsync("/api/ai/query", new Dictionary<string, object?> { ["taskId"] = id }, ct).ConfigureAwait(false); }
                catch (HttpRequestException) { continue; } // a dropped poll is not a failed generation
                if (!CodeIsZero(q) || !q.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) continue;
                foreach (var field in new[] { "cleanImages", "images", "videos" })
                {
                    if (d.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                        return list[0].GetString() ?? "";
                }
                var status = d.TryGetProperty("status", out var s) ? (s.GetString() ?? "").ToLowerInvariant() : "";
                if (status == "failed" || status == "error")
                    throw new SayMakerException(SayMakerErrorKind.Rejected, "the run failed; reword the prompt rather than retrying");
            }
            throw new SayMakerException(SayMakerErrorKind.Timeout, "deadline passed; the run may still finish in your SayMaker history");
        }
    }
}
