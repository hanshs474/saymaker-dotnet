# SayMaker for .NET

Generate images and video on [SayMaker](https://saymaker.ai/?utm_source=nuget&utm_medium=package) from your own code: one API key runs Veo 3.1, Kling 3.0, Seedance 2.0, MiniMax H3, Nano Banana 2, GPT Image 2.5, Seedream 5.0 and the rest of the shelf on your own credits.

```csharp
using SayMaker;

var sm = new SayMakerClient(Environment.GetEnvironmentVariable("SAYMAKER_API_KEY"));
var url = await sm.GenerateAsync("a paper-cut layered mountain range at dusk");
var clip = await sm.GenerateVideoAsync("a paper boat drifting down a rain gutter, low angle");
```

Each call waits for the run and returns the URL of the finished file. Runs on a free account wait in a queue before they start, and the wait grows with each run of the day; a paid plan starts at once. The default timeouts cover the longest wait.

## Install

```bash
dotnet add package SayMaker
```

Targets `net8.0` and `netstandard2.0`. Pass the key to the constructor or set `SAYMAKER_API_KEY`.

## API key

Sign up on [saymaker.ai](https://saymaker.ai/?utm_source=nuget&utm_medium=package) (new accounts get sign-up credits), create a key at [saymaker.ai/settings/apikeys](https://saymaker.ai/settings/apikeys?utm_source=nuget&utm_medium=package), then pass it or export it:

```bash
export SAYMAKER_API_KEY=sk-...
```

A key runs on your account exactly like the site does: same models, same credit prices, same plan, and every run lands in your library at [saymaker.ai/history](https://saymaker.ai/history?utm_source=nuget&utm_medium=package).

## Images

```csharp
await sm.GenerateAsync("isometric diorama of a ramen shop at night", new ImageOptions { AspectRatio = "16:9" });
await sm.GenerateAsync("product shot of a glass perfume bottle on wet slate", new ImageOptions { Model = "gpt-image-2-5-flare" });
```

The default model is `saymaker-image-v1`, the cheapest text-to-image run. Pass a model id for `nano-banana-2`, `gpt-image-2-5-flare`, `seedream-5-pro`, `qwen-image-3-pro` and the rest; see the [image models](https://saymaker.ai/image?utm_source=nuget&utm_medium=package).

### Editing a photo

Pass an image URL and the prompt describes the change rather than the picture:

```csharp
await sm.GenerateAsync(
    "change the jacket to dark green, keep the face, pose and background exactly as they are",
    new ImageOptions { ImageUrl = "https://example.com/portrait.jpg" });
```

Naming what must stay is what decides whether the edit holds. Prompts that only name the change tend to drift the whole picture.

## Video

```csharp
await sm.GenerateVideoAsync("a fox trotting through fresh snow at dawn, tracking shot", new VideoOptions { Duration = 6 });
await sm.GenerateVideoAsync("the camera slowly orbits the statue", new VideoOptions { ImageUrl = "https://example.com/statue.jpg" });
await sm.GenerateVideoAsync("a street drummer in the rain", new VideoOptions { Model = "veo-3-1", Resolution = "1080p" });
```

The default model is `minimax-h3-fast`, the one a free account can run (480p or 768p, 4 to 15 seconds). Veo 3.1, Kling 3.0, Seedance 2.0 and the other video models need a plan or a credit pack; see [pricing](https://saymaker.ai/pricing?utm_source=nuget&utm_medium=package). Each model's options are on its page: [Veo 3.1](https://saymaker.ai/video/veo-3-1?utm_source=nuget&utm_medium=package), [Kling 3.0](https://saymaker.ai/video/kling-3-0?utm_source=nuget&utm_medium=package), [Seedance 2.0](https://saymaker.ai/video/seedance-2?utm_source=nuget&utm_medium=package).

## Errors worth catching

Every failure is a `SayMakerException` with a `Kind`:

- `Auth` — no key, or the key is wrong or deleted
- `Quota` — the account is out of credits
- `Plan` — that model needs a paid plan or a credit pack
- `Busy` — another run on this account is still rendering; free accounts run one at a time
- `Rejected` — the content filter or the model refused the prompt; rewrite it rather than retrying

The API answers refusals with HTTP 200 and an error code in the body, so a client that only checks the status code reports a refusal as success. This one reads the body.

Free-account output carries a watermark; a paid plan returns the clean file.

MIT licensed. Made by [SayMaker](https://saymaker.ai/?utm_source=nuget&utm_medium=package), the AI video generator agent.
