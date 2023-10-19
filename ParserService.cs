using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.Utils;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SkiaSharp;

namespace FicsitPakParser;

public class ParserService : Parser.ParserBase
{
    private readonly ILogger<ParserService> _logger;
    
    public ParserService(ILogger<ParserService> logger)
    {
        _logger = logger;

        Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
    }

    public override async Task Parse(ParseRequest request, IServerStreamWriter<AssetResponse> responseStream, ServerCallContext context)
    {
        EGame engineVersion;
        switch (request.EngineVersion)
        {
            case "4.26":
                engineVersion = EGame.GAME_UE4_26;
                break;
            case "5.1":
                engineVersion = EGame.GAME_UE5_1;
                break;
            case "5.2":
                engineVersion = EGame.GAME_UE5_2;
                break;
            case "5.3":
                engineVersion = EGame.GAME_UE5_3;
                break;
            default:
                _logger.LogInformation("Unknown engine version: {version}", request.EngineVersion);
                return;
        }

        _logger.LogInformation("Parsing zip {size}", request.ZipData.Length);

        // Create temporary directory
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _logger.LogInformation("Creating temporary directory: {name}", tempDirectory);
        Directory.CreateDirectory(tempDirectory);

        try
        {
            // Extract into temporary directory
            var stream = new MemoryStream(request.ZipData.ToByteArray());
            var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            zip.ExtractToDirectory(tempDirectory);

            // Create asset provider
            var provider = new DefaultFileProvider(tempDirectory, SearchOption.AllDirectories, true, new VersionContainer(engineVersion));

            Debug.Assert(Program.usmap != null, "Program.usmap != null");
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(Program.usmap);
            
            provider.Initialize();
            provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
            provider.LoadLocalization(ELanguage.English);
            
            // Stream all assets
            var entries = new Dictionary<String, UObject[]>();
            foreach (var asset in provider.Files.Values)
            {
                if (asset is not VfsEntry entry || entry.Path.EndsWith(".uexp") || entry.Path.EndsWith(".ubulk") || entry.Path.EndsWith(".uptnl"))
                {
                    continue;
                }
            
                if (!asset.Path.EndsWith(".uasset") && !asset.Path.EndsWith(".umap"))
                {
                    continue;
                }
            
                _logger.LogInformation("Loaded: {name}", entry.Path);
                
                var exports = provider.LoadAllObjects(asset.Path);
                entries[entry.Path] = exports.ToArray();
            
                var directory = entry.Path.SubstringBeforeLast('/');

                if (!asset.Path.EndsWith(".umap"))
                {
                    foreach (var o in entries[entry.Path])
                    {
                        if (o is UTexture2D texture)
                        {
                            var bitmap = texture.Decode();
            
                            if (bitmap == null)
                            {
                                continue;
                            }
            
                            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                            
                            await responseStream.WriteAsync(new AssetResponse 
                            {
                                Path = Path.Join(directory, texture.Name + ".png"),
                                Data = ByteString.CopyFrom(data.ToArray())
                            });
                        }
                    }
                }
            }
            
            await responseStream.WriteAsync(new AssetResponse 
            {
                Path = "metadata.json",
                Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entries, Formatting.None)))
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e.ToString());
        }
        
        // Delete temp directory
        _logger.LogInformation("Removing temporary directory: {name}", tempDirectory);
        Directory.Delete(tempDirectory, true);
    }
}