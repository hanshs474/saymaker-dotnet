// SAYMAKER_API_KEY=sk-... dotnet run --project examples/Generate
using SayMaker;

try
{
    var url = await new SayMakerClient().GenerateAsync("a paper-cut layered mountain range at dusk");
    Console.WriteLine(url);
}
catch (SayMakerException e)
{
    Console.Error.WriteLine($"{e.Kind}: {e.Message}");
    return 1;
}
return 0;
