using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace signalr_management_test
{
    public static class Common
    {
        public static string AzureSignalRServiceConnectionString =
            "Endpoint=https://myservice.service.signalr.net;AccessKey=xxx=;Version=1.0;";

        public static string SignalRHubName = "echo";
    }

    public class SignalRManagementTests
    {
        [Fact]
        public async Task SignalRManagementShouldBeAbleRetrieveExistingGroupAndConnection()
        {
            var hubContext = await CreateSignalRServiceHubContext();

            var server = new TestApiFactory().Server;

            var connection1 = CreateHubClientConnectionAsync(server);
            await connection1.StartAsync();
            await Task.Delay(500);

            var connection2 = CreateHubClientConnectionAsync(server);
            await connection2.StartAsync();
            await Task.Delay(500);

            Assert.True(EchoHub.ConnectionIds.Count == 2);

            await connection1.InvokeAsync("Echo", "message");

            var groupId = EchoHub.GroupIds.First();
            var connectionId = EchoHub.ConnectionIds.First();

            var groupExists = await hubContext.ClientManager.GroupExistsAsync(groupId);
            var connectionExists = await hubContext.ClientManager.ConnectionExistsAsync(connectionId);

            // This is always false
            Assert.True(groupExists);
            // This is also always false
            Assert.True(connectionExists);
        }

        private HubConnection CreateHubClientConnectionAsync(TestServer server)
        {
            var connectionBuilder = new HubConnectionBuilder()
                .WithUrl(server.BaseAddress + Common.SignalRHubName, options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
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

        private static async Task<ServiceHubContext> CreateSignalRServiceHubContext()
        {
            using var serviceManager = new ServiceManagerBuilder()
                .WithOptions(option =>
                {
                    option.ConnectionString = Common.AzureSignalRServiceConnectionString;
                })
                .BuildServiceManager();
            return await serviceManager.CreateHubContextAsync(Common.SignalRHubName, default);
        }
    }

    public class EchoHub : Hub
    {
        public static List<string> GroupIds { get; set; } = new List<string>();
        public static List<string> ConnectionIds { get; set; } = new List<string>();

        public override async Task OnConnectedAsync()
        {
            if (!GroupIds.Any())
            {
                GroupIds.Add("Echo");
            }

            if (!ConnectionIds.Contains(Context.ConnectionId))
            {
                ConnectionIds.Add(Context.ConnectionId);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, "Echo");
            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            if (ConnectionIds.Contains(Context.ConnectionId))
            {
                ConnectionIds.Remove(Context.ConnectionId);
            }

            if (!ConnectionIds.Any())
            {
                GroupIds.Remove("Echo");
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
                endpointBuilder.MapHub<EchoHub>($"/{Common.SignalRHubName}");
            });
            //app.UseAzureSignalR(routeBuilder => routeBuilder.MapHub<EchoHub>($"/{Common.SignalRHubName}"));
        }
    }
}
