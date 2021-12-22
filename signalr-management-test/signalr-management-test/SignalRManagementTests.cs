using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace signalr_management_test
{
    public static class Common
    {
        public static string AzureSignalRServiceConnectionString =
            "Endpoint=https://myservice.service.signalr.net;AccessKey=xxx=;Version=1.0;";
    }

    public class SignalRManagementTests
    {
        [Fact]
        public async Task Test1()
        {
            await SignalRTest(nameof(EchoHub));
        }

        [Fact]
        public async Task Test2()
        {
            await SignalRTest(nameof(EchoHub));
        }

        [Fact]
        public async Task Test3()
        {
            await SignalRTest(nameof(EchoHub));
        }

        [Fact]
        public async Task Test4()
        {
            await SignalRTest(nameof(EchoHub));
        }

        [Fact]
        public async Task Test5()
        {
            await SignalRTest("NotEchoHub");
        }

        private async Task SignalRTest(string signalRHubName)
        {
            using var apiFactory = new TestApiFactory().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(
                        new Dictionary<string, string>
                        {
                            ["SignalRHubName"] = signalRHubName
                        });
                });
            });
            using var server = apiFactory.Server;

            await using var connection1 = CreateHubClientConnectionAsync(server, signalRHubName);
            await connection1.StartAsync();

            await using var connection2 = CreateHubClientConnectionAsync(server, signalRHubName);
            await connection2.StartAsync();

            Assert.True(EchoHub.ConnectionIds.Count == 2);

            await connection1.InvokeAsync("Echo", "message");

            var groupId = EchoHub.GroupIds.First().Key;
            var connectionId = EchoHub.ConnectionIds.First().Key;
            
            using var serviceManager = new ServiceManagerBuilder()
                .WithOptions(option =>
                {
                    option.ConnectionString = Common.AzureSignalRServiceConnectionString;
                })
                .BuildServiceManager();
            await using var hubContext = await serviceManager.CreateHubContextAsync(signalRHubName, default);
            var groupExists = await hubContext.ClientManager.GroupExistsAsync(groupId);
            var connectionExists = await hubContext.ClientManager.ConnectionExistsAsync(connectionId);
            
            Assert.True(groupExists);
            Assert.True(connectionExists);

            // Manually dispose everything
            await hubContext.DisposeAsync();
            serviceManager.Dispose();
            await connection2.StopAsync();
            await connection1.StopAsync();
            server.Dispose();
            apiFactory.Dispose();

            await Task.Delay(2000);
            Assert.True(EchoHub.ConnectionIds.Count == 0);
            Assert.True(EchoHub.GroupIds.Count == 0);
        }

        private HubConnection CreateHubClientConnectionAsync(TestServer server, string signalRHubName)
        {
            var connectionBuilder = new HubConnectionBuilder()
                .WithUrl(server.BaseAddress + signalRHubName, options =>
                {
                    options.HttpMessageHandlerFactory = externalHandler => new SignalRClientHttpMessageHandler(server.CreateHandler(), externalHandler);
                });
            connectionBuilder.Services.AddLogging();
            var connection = connectionBuilder.Build();
            connection.On<string>("Echo", (receivedMessage) =>
            {
                Assert.Equal("message", receivedMessage);
            });
            return connection;
        }
    }

    public class EchoHub : Hub
    {
        public static ConcurrentDictionary<string, bool> GroupIds { get; set; } = new ConcurrentDictionary<string, bool>();
        public static ConcurrentDictionary<string, bool> ConnectionIds { get; set; } = new ConcurrentDictionary<string, bool>();

        public override async Task OnConnectedAsync()
        {
            var groupId = Guid.NewGuid();
            if (!GroupIds.Any())
            {
                GroupIds.TryAdd(groupId.ToString(), true);
            }

            if (!ConnectionIds.ContainsKey(Context.ConnectionId))
            {
                ConnectionIds.TryAdd(Context.ConnectionId, true);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());
            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (ConnectionIds.ContainsKey(Context.ConnectionId))
            {
                ConnectionIds.TryRemove(Context.ConnectionId, out _);
            }

            if (!ConnectionIds.Any())
            {
                GroupIds.Clear();
            }

            return base.OnDisconnectedAsync(exception);
        }

        public Task Echo(string message)
        {
            return Clients.All.SendAsync("Echo", message);
        }
    }

    public class TestApiFactory : WebApplicationFactory<WebApplicationStartup>
    {
        protected override IHostBuilder CreateHostBuilder()
        {
            return Host
                .CreateDefaultBuilder(Array.Empty<string>())
                .ConfigureWebHostDefaults(webHostBuilder =>
                {
                    webHostBuilder.UseStartup<WebApplicationStartup>();
                });
        }
    }

    public class WebApplicationStartup
    {
        private readonly IConfiguration _configuration;

        public WebApplicationStartup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddLogging();
            services
                .AddSignalR(hubOptions => { hubOptions.EnableDetailedErrors = true; })
                .AddAzureSignalR(Common.AzureSignalRServiceConnectionString);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpointBuilder =>
            {
                var signalRHubName = _configuration["SignalRHubName"];
                endpointBuilder.MapHub<EchoHub>($"/{signalRHubName}");
            });
            //app.UseAzureSignalR(routeBuilder => routeBuilder.MapHub<EchoHub>($"/{Common.SignalRHubName}"));
        }
    }
}
