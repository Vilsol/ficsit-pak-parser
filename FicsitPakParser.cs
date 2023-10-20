using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FicsitPakParser;

public static class Program
{
    public static String? usmap;
    
    public static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("please provide path to .usmap");
            Console.WriteLine("FicsitPakParser <usmap>");
            return;
        }

        usmap = args[0];
        
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaxReceiveMessageSize = 1024 * 1024 * 1024; // 1 GB
            options.MaxSendMessageSize = 1024 * 1024 * 1024; // 1 GB
        });
        builder.Services.AddGrpcReflection();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Limits.MaxRequestBodySize = int.MaxValue;
            serverOptions.ListenAnyIP(50051, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();

        app.MapGrpcReflectionService();
        
        app.MapGrpcService<ParserService>();

        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

        app.Run();
    }
}