using MineEditor.Core.Documents;
using MineEditor.Core.World;
using WinRT;

// Release builds are trimmed. Objects handed to WinUI as `object` (x:Bind ItemsSource values and the items in them)
// need COM vtables. The CsWinRT generator creates them for partial classes in this project, but types from
// MineEditor.Core can't run the generator, so they're listed here. Everything else (such as generic collections)
// uses CsWinRT's runtime fallback, which is why MineEditor.csproj keeps WinRT.Runtime from being trimmed.
// After binding a new type, check the page in a trimmed Release build.
[assembly: GeneratedWinRTExposedExternalType(typeof(FileNbtDocument))]
[assembly: GeneratedWinRTExposedExternalType(typeof(ChunkNbtDocument))]
[assembly: GeneratedWinRTExposedExternalType(typeof(PlayerEntry))]
[assembly: GeneratedWinRTExposedExternalType(typeof(Dimension))]
